using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Yoldas.Api.Common;
using Yoldas.Api.Features.Bookings;
using Yoldas.Api.Features.Trips;
using Yoldas.Core;
using Yoldas.Core.Bookings;
using Yoldas.Core.Reviews;
using Yoldas.Core.Trips;
using Yoldas.Core.Users;
using Yoldas.Infrastructure.Persistence;

namespace Yoldas.Api.Features.Live;

public sealed record PositionRequest(double Latitude, double Longitude);

public sealed record CodeRequest(string? Code);

public sealed record ReviewRequest(int Stars, string? Comment);

public sealed record ReviewResponse(Guid Id, string AuthorName, ReviewOf Of, int Stars, string Comment, DateTimeOffset At);

/// <summary>How someone has been as a driver: stars, trips made, and seats they called off late.</summary>
public sealed record AsDriver(double? Rating, int Reviews, int TripsDriven, int LateCancellations);

/// <summary>How someone has been as a passenger: stars, rides made, late cancellations, and times they did not come.</summary>
public sealed record AsPassenger(double? Rating, int Reviews, int Rides, int LateCancellations, int NoShows);

public sealed record ProfileResponse(Guid Id, string Name, DateTimeOffset MemberSince, AsDriver AsDriver, AsPassenger AsPassenger, IReadOnlyList<ReviewResponse> Reviews);

/// <summary>Starting, sharing where the car is, boarding with codes, finishing, and reviewing afterwards.</summary>
internal static class TripDayEndpoints
{
    public static IEndpointRouteBuilder MapTripDayEndpoints(this IEndpointRouteBuilder app)
    {
        var trips = app.MapGroup("/api/trips/{tripId:guid}").WithTags("Trip day").RequireAuthorization();
        trips.MapPost("/start", Start).WithSummary("Start the trip, from half an hour before the departure");
        trips.MapPost("/position", Position).WithSummary("Share where the car is (driver), while the trip is under way")
            .WithDescription($"""
                The position is placed on the route and sent to everyone following the trip on {TripHub.Path}, with the
                times for the stops ahead worked out from the speed so far. Only the latest position is kept; it is
                cleared when the trip finishes.
                """);
        trips.MapGet("/live", Live).WithSummary("The trip as it happens, for the driver and passengers with a confirmed seat");
        trips.MapPost("/finish", Finish).WithSummary("Finish the trip: whoever boarded has made it, a confirmed seat nobody boarded is a no-show");

        var bookings = app.MapGroup("/api/bookings/{bookingId:guid}").WithTags("Trip day").RequireAuthorization();
        bookings.MapPost("/board", Board).WithSummary("A passenger gets in (driver types in the code on their screen)")
            .WithDescription($"After {Booking.CodeTries} wrong codes the passenger gets a new one.");
        bookings.MapPost("/no-show", NoShow).WithSummary("A passenger did not come (driver)")
            .WithDescription("Once the car has passed the pickup, or a quarter of an hour after it was due.");
        bookings.MapPost("/review", WriteReview).WithSummary("Review the other side, once, in the two weeks after the ride");

        app.MapGet("/api/users/{userId:guid}", Profile).WithTags("People").AllowAnonymous()
            .WithSummary("Someone's record as a driver and as a passenger, and what people said");
        return app;
    }

    private static async Task<Ok<LiveView>> Start(Guid tripId, ClaimsPrincipal user, YoldasDbContext db, LivePublisher live, TimeProvider clock, CancellationToken ct)
    {
        var trip = await TripEndpoints.OwnAsync(db, tripId, user.UserId(), ct);
        trip.Start(clock.GetUtcNow());
        await SaveAsync(db, ct);
        await live.PublishAsync(trip);
        return TypedResults.Ok(LiveAccess.View(trip, clock.GetUtcNow()));
    }

    private static async Task<Ok<LiveView>> Position(
        Guid tripId, PositionRequest request, ClaimsPrincipal user, YoldasDbContext db, LivePublisher live, TimeProvider clock, CancellationToken ct)
    {
        var trip = await TripEndpoints.OwnAsync(db, tripId, user.UserId(), ct);
        trip.Report(request.Latitude, request.Longitude, clock.GetUtcNow());
        await SaveAsync(db, ct);
        await live.PublishAsync(trip);
        return TypedResults.Ok(LiveAccess.View(trip, clock.GetUtcNow()));
    }

    private static async Task<Ok<LiveView>> Live(Guid tripId, ClaimsPrincipal user, YoldasDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var trip = await db.Trips.AsNoTracking().SingleOrDefaultAsync(t => t.Id == tripId, ct);
        if (trip is null || !await LiveAccess.MayFollowAsync(db, trip, user.UserId(), ct))
        {
            throw new DomainException(ErrorCodes.NotFound, "There is no trip with this id that you may follow.");
        }

        return TypedResults.Ok(LiveAccess.View(trip, clock.GetUtcNow()));
    }

    private static async Task<Ok<LiveView>> Finish(Guid tripId, ClaimsPrincipal user, TripDayService days, LivePublisher live, TimeProvider clock, CancellationToken ct)
    {
        var trip = await days.FinishAsync(tripId, user.UserId(), ct);
        await live.PublishAsync(trip);
        return TypedResults.Ok(LiveAccess.View(trip, clock.GetUtcNow()));
    }

    private static async Task<Ok<BookingResponse>> Board(
        Guid bookingId, CodeRequest request, ClaimsPrincipal user, YoldasDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var (booking, trip) = await DriverBookingAsync(db, bookingId, user.UserId(), ct);
        try
        {
            booking.Board(user.UserId(), request.Code, trip, clock.GetUtcNow());
        }
        catch (DomainException ex) when (ex.Code == ErrorCodes.WrongCode)
        {
            // The wrong guess is counted, and the code replaced after too many, before the error goes back.
            await db.SaveChangesAsync(ct);
            throw;
        }

        await SaveAsync(db, ct);
        return TypedResults.Ok(await BookingQueries.DetailAsync(db, bookingId, user.UserId(), clock.GetUtcNow(), ct));
    }

    private static async Task<Ok<BookingResponse>> NoShow(Guid bookingId, ClaimsPrincipal user, YoldasDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var (booking, trip) = await DriverBookingAsync(db, bookingId, user.UserId(), ct);
        booking.NoShow(user.UserId(), trip, clock.GetUtcNow());
        await SaveAsync(db, ct);
        return TypedResults.Ok(await BookingQueries.DetailAsync(db, bookingId, user.UserId(), clock.GetUtcNow(), ct));
    }

    /// <summary>
    /// The review and the rating it adds to are written together. Ratings are added in place, so two reviews of the
    /// same person at the same moment both count.
    /// </summary>
    private static async Task<Created<ReviewResponse>> WriteReview(
        Guid bookingId, ReviewRequest request, ClaimsPrincipal user, YoldasDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var me = user.UserId();
        var booking = await db.Bookings.AsNoTracking().SingleOrDefaultAsync(b => b.Id == bookingId && (b.DriverId == me || b.PassengerId == me), ct)
            ?? throw new DomainException(ErrorCodes.NotFound, "There is no booking with this id.");
        var review = Review.Write(booking, me, request.Stars, request.Comment, clock.GetUtcNow());
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        db.Reviews.Add(review);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation("ix_reviews_booking_id_author_id"))
        {
            throw new DomainException(ErrorCodes.AlreadyReviewed, "You already reviewed this ride.");
        }

        var subject = db.Users.Where(u => u.Id == review.SubjectId);
        if (review.Of == ReviewOf.Driver)
        {
            await subject.ExecuteUpdateAsync(u => u.SetProperty(x => x.DriverRatingSum, x => x.DriverRatingSum + review.Stars).SetProperty(x => x.DriverRatingCount, x => x.DriverRatingCount + 1), ct);
        }
        else
        {
            await subject.ExecuteUpdateAsync(u => u.SetProperty(x => x.PassengerRatingSum, x => x.PassengerRatingSum + review.Stars).SetProperty(x => x.PassengerRatingCount, x => x.PassengerRatingCount + 1), ct);
        }

        await transaction.CommitAsync(ct);
        var author = await db.Users.AsNoTracking().Where(u => u.Id == me).Select(u => u.DisplayName).SingleAsync(ct);
        return TypedResults.Created($"/api/users/{review.SubjectId}", new ReviewResponse(review.Id, author, review.Of, review.Stars, review.Comment, review.CreatedAt));
    }

    private static async Task<Ok<ProfileResponse>> Profile(Guid userId, YoldasDbContext db, CancellationToken ct)
    {
        var person = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new DomainException(ErrorCodes.NotFound, "There is no one with this id.");
        var driven = await db.Trips.CountAsync(t => t.DriverId == userId && t.Status == TripStatus.Finished, ct);
        var asDriver = db.Bookings.Where(b => b.DriverId == userId);
        var asPassenger = db.Bookings.Where(b => b.PassengerId == userId);
        var driverLate = await asDriver.CountAsync(b => b.LateCancellation && b.CancelledBy == CancelledBy.Driver, ct);
        var rides = await asPassenger.CountAsync(b => b.Status == BookingStatus.Completed, ct);
        var passengerLate = await asPassenger.CountAsync(b => b.LateCancellation && b.CancelledBy == CancelledBy.Passenger, ct);
        var noShows = await asPassenger.CountAsync(b => b.Status == BookingStatus.NoShow, ct);
        var reviews = await db.Reviews.AsNoTracking().Where(r => r.SubjectId == userId).OrderByDescending(r => r.CreatedAt).Take(10)
            .Select(r => new ReviewResponse(r.Id, db.Users.Where(u => u.Id == r.AuthorId).Select(u => u.DisplayName).First(), r.Of, r.Stars, r.Comment, r.CreatedAt))
            .ToListAsync(ct);
        return TypedResults.Ok(new ProfileResponse(
            person.Id, person.DisplayName, person.CreatedAt,
            new AsDriver(User.Average(person.DriverRatingSum, person.DriverRatingCount), person.DriverRatingCount, driven, driverLate),
            new AsPassenger(User.Average(person.PassengerRatingSum, person.PassengerRatingCount), person.PassengerRatingCount, rides, passengerLate, noShows),
            reviews));
    }

    private static async Task<(Booking Booking, Trip Trip)> DriverBookingAsync(YoldasDbContext db, Guid bookingId, Guid userId, CancellationToken ct)
    {
        var booking = await db.Bookings.SingleOrDefaultAsync(b => b.Id == bookingId && (b.DriverId == userId || b.PassengerId == userId), ct)
            ?? throw new DomainException(ErrorCodes.NotFound, "There is no booking with this id.");
        var trip = await db.Trips.AsNoTracking().SingleAsync(t => t.Id == booking.TripId, ct);
        return (booking, trip);
    }

    private static async Task SaveAsync(YoldasDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new DomainException(ErrorCodes.Conflict, "This changed meanwhile. Load it again and retry.");
        }
    }
}

/// <summary>Finishing trips, by the driver or by the clock, and calling off the ones that never started.</summary>
internal sealed class TripDayService(YoldasDbContext db, TimeProvider clock)
{
    /// <summary>A trip not started this long after its departure did not happen: it is cancelled, as the driver's.</summary>
    public static readonly TimeSpan NeverStarted = TimeSpan.FromHours(6);

    /// <summary>A trip still under way this long after its expected arrival is finished for the driver.</summary>
    public static readonly TimeSpan NeverFinished = TimeSpan.FromHours(12);

    public async Task<Trip> FinishAsync(Guid tripId, Guid driverId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var trip = await TripEndpoints.OwnAsync(db, tripId, driverId, ct);
        await FinishAsync(trip, ct);
        await transaction.CommitAsync(ct);
        return trip;
    }

    /// <summary>Returns the trips the clock finished or cancelled, for the sweeper to announce.</summary>
    public async Task<List<Trip>> LapseAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var notStarted = now - NeverStarted;
        var changed = new List<Trip>();
        var ghosts = await db.Trips.Where(t => t.Status == TripStatus.Scheduled && t.DepartureAt < notStarted).Take(200).ToListAsync(ct);
        foreach (var trip in ghosts)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            trip.Cancel(now);
            var open = await db.Bookings.Where(b => b.TripId == trip.Id && (b.Status == BookingStatus.Requested || b.Status == BookingStatus.Confirmed)).ToListAsync(ct);
            open.ForEach(b => b.TripCancelled(now));
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            changed.Add(trip);
        }

        var started = await db.Trips.Where(t => t.Status == TripStatus.Started).Take(500).ToListAsync(ct);
        foreach (var trip in started.Where(t => t.ArrivalAt + NeverFinished < now))
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await FinishAsync(trip, ct);
            await transaction.CommitAsync(ct);
            changed.Add(trip);
        }

        return changed;
    }

    private async Task FinishAsync(Trip trip, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        trip.Finish(now);
        var bookings = await db.Bookings.Where(b => b.TripId == trip.Id && (b.Status == BookingStatus.Requested || b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.Boarded)).ToListAsync(ct);
        bookings.ForEach(b => b.TripFinished(now));
        await db.SaveChangesAsync(ct);
    }
}
