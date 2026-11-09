using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Yoldas.Api.Common;
using Yoldas.Core;
using Yoldas.Core.Places;
using Yoldas.Core.Trips;
using Yoldas.Core.Users;
using Yoldas.Infrastructure.Persistence;

namespace Yoldas.Api.Features.Trips;

internal static partial class TripEndpoints
{
    public static IEndpointRouteBuilder MapTripEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/places", (string? q, int? limit) => TypedResults.Ok(
                Places.Find(q, limit ?? 8).Select(p => new PlaceResponse(p.Name, p.Province, p.Label, p.Latitude, p.Longitude)).ToList()))
            .WithTags("Places").WithSummary("Provincial centres and towns by name, typed with or without Turkish letters");

        var me = app.MapGroup("/api/me").WithTags("Drivers").RequireAuthorization();
        me.MapGet("/vehicle", GetVehicle).WithSummary("Your car");
        me.MapPut("/vehicle", PutVehicle).WithSummary("Add or change your car");
        me.MapGet("/trips", MyTrips).WithSummary("Trips you drive: upcoming first, or past with when=past");

        var trips = app.MapGroup("/api/trips").WithTags("Trips");
        trips.MapGet("", Search).AllowAnonymous().WithSummary("Trips passing near where you are and where you are going")
            .WithDescription($"""
                A trip matches when its route passes within `radiusKm` (default {TripQueries.DefaultRadiusKm}, at most {TripQueries.MaxRadiusKm}) of
                both places, in that order, with `seats` free for the part you would ride. Each result says where you
                would join and leave, when the car passes there, and your share of the price. `date` is a day in
                Turkey; without it, every upcoming trip. `sort`: Departure, Price or Closest.
                """);
        trips.MapGet("/{tripId:guid}", Get).AllowAnonymous().WithSummary("A trip, its stops and times; with from and to, your part and its price");
        trips.MapPost("", Offer).RequireAuthorization().WithSummary("Offer the seats on a trip you are making")
            .WithDescription($"""
                Two to {Trip.MaxStops} stops in order, at least a kilometre apart. The price is for a seat over the whole
                route, at most 3 TL a kilometre: the point is to share the cost of the trip, not to make a living.
                """);
        trips.MapPut("/{tripId:guid}", Change).RequireAuthorization().WithSummary("Change the time, seats, price or notes");
        trips.MapPost("/{tripId:guid}/cancel", Cancel).RequireAuthorization().WithSummary("Cancel a trip that has not started");
        return app;
    }

    private static async Task<Results<Ok<VehicleResponse>, NotFound>> GetVehicle(ClaimsPrincipal user, YoldasDbContext db, CancellationToken ct)
    {
        var vehicle = await db.Vehicles.AsNoTracking().SingleOrDefaultAsync(v => v.OwnerId == user.UserId(), ct);
        return vehicle is null ? TypedResults.NotFound() : TypedResults.Ok(AsResponse(vehicle));
    }

    private static async Task<Ok<VehicleResponse>> PutVehicle(VehicleRequest request, ClaimsPrincipal user, YoldasDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var userId = user.UserId();
        var now = clock.GetUtcNow();
        var vehicle = await db.Vehicles.SingleOrDefaultAsync(v => v.OwnerId == userId, ct);
        if (vehicle is null)
        {
            vehicle = Vehicle.Register(userId, request.Make, request.Model, request.Colour, request.Plate, request.PassengerSeats, now);
            db.Vehicles.Add(vehicle);
        }
        else
        {
            vehicle.Change(request.Make, request.Model, request.Colour, request.Plate, request.PassengerSeats, now);
        }

        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(AsResponse(vehicle));
    }

    private static VehicleResponse AsResponse(Vehicle v) => new(v.Make, v.Model, v.Colour, v.Plate, v.PassengerSeats);

    private static async Task<Results<Ok<IReadOnlyList<TripSummary>>, ValidationProblem>> Search(
        double? fromLat, double? fromLng, double? toLat, double? toLng, DateOnly? date, int? seats, double? radiusKm, TripSort? sort,
        YoldasDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var validation = new Validation()
            .Require(fromLat is not null && fromLng is not null, "from", "Where you start: fromLat and fromLng.")
            .Require(toLat is not null && toLng is not null, "to", "Where you are going: toLat and toLng.")
            .Require(seats is null or (>= 1 and <= Vehicle.MaxPassengerSeats), "seats", $"1 to {Vehicle.MaxPassengerSeats} seats.");
        if (!validation.IsValid)
        {
            return validation.ToProblem();
        }

        var found = await TripQueries.SearchAsync(
            db, (fromLat!.Value, fromLng!.Value), (toLat!.Value, toLng!.Value), date, seats ?? 1, radiusKm ?? TripQueries.DefaultRadiusKm,
            sort ?? TripSort.Departure, clock.GetUtcNow(), ct);
        return TypedResults.Ok(found);
    }

    private static async Task<Ok<TripResponse>> Get(
        Guid tripId, double? fromLat, double? fromLng, double? toLat, double? toLng, ClaimsPrincipal user, YoldasDbContext db, TimeProvider clock, CancellationToken ct)
    {
        (double, double)? from = fromLat is { } a && fromLng is { } b ? (a, b) : null;
        (double, double)? to = toLat is { } c && toLng is { } d ? (c, d) : null;
        Guid? viewer = user.Identity?.IsAuthenticated == true ? user.UserId() : null;
        return TypedResults.Ok(await TripQueries.DetailAsync(db, tripId, viewer, from, to, clock.GetUtcNow(), ct));
    }

    private static async Task<Ok<IReadOnlyList<TripSummary>>> MyTrips(string? when, ClaimsPrincipal user, YoldasDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var userId = user.UserId();
        var now = clock.GetUtcNow();
        var past = string.Equals(when, "past", StringComparison.OrdinalIgnoreCase);
        var query = db.Trips.AsNoTracking().Where(t => t.DriverId == userId);
        query = past
            ? query.Where(t => t.Status == TripStatus.Finished || t.Status == TripStatus.Cancelled || (t.Status == TripStatus.Scheduled && t.DepartureAt < now - TimeSpan.FromDays(1)))
                .OrderByDescending(t => t.DepartureAt)
            : query.Where(t => t.Status == TripStatus.Started || (t.Status == TripStatus.Scheduled && t.DepartureAt >= now - TimeSpan.FromDays(1)))
                .OrderBy(t => t.DepartureAt);
        var trips = await query.Take(100).ToListAsync(ct);
        var people = await TripQueries.PeopleAsync(db, trips, ct);
        var taken = await TripQueries.TakenAsync(db, trips.Select(t => t.Id).ToList(), ct);
        return TypedResults.Ok<IReadOnlyList<TripSummary>>([.. trips.Select(t =>
            TripQueries.Summary(t, people.Drivers[t.DriverId], people.Vehicles[t.VehicleId], TripQueries.SeatsLeft(t, TripQueries.TakenOn(taken, t.Id)), null))]);
    }

    private static async Task<Results<Created<TripResponse>, ValidationProblem>> Offer(
        TripRequest request, ClaimsPrincipal user, YoldasDbContext db, TimeProvider clock, ILoggerFactory loggers, CancellationToken ct)
    {
        var validation = new Validation().Require(request.Stops is { Count: > 0 }, "stops", "A trip needs its stops.");
        if (!validation.IsValid)
        {
            return validation.ToProblem();
        }

        var userId = user.UserId();
        var vehicle = await db.Vehicles.AsNoTracking().SingleOrDefaultAsync(v => v.OwnerId == userId, ct)
            ?? throw new DomainException(ErrorCodes.NoVehicle, "Add your car first: passengers need to know what to look for.");
        var now = clock.GetUtcNow();
        var trip = Trip.Offer(
            userId, vehicle.Id, vehicle.PassengerSeats, [.. request.Stops!.Select(s => new StopInput(s.Name, s.Latitude, s.Longitude))], request.DepartureAt,
            request.Seats, Money.ToMinor(request.PricePerSeat, "The price"), request.InstantBook, request.Notes, now);
        db.Trips.Add(trip);
        await db.SaveChangesAsync(ct);
        LogOffered(loggers.CreateLogger(typeof(TripEndpoints)), trip.Id, trip.Stops.Count, TripQueries.Km(trip.LengthMeters));
        return TypedResults.Created($"/api/trips/{trip.Id}", await TripQueries.DetailAsync(db, trip.Id, userId, null, null, now, ct));
    }

    private static async Task<Ok<TripResponse>> Change(Guid tripId, TripChangeRequest request, ClaimsPrincipal user, YoldasDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var trip = await OwnAsync(db, tripId, user.UserId(), ct);
        var vehicle = await db.Vehicles.AsNoTracking().SingleAsync(v => v.Id == trip.VehicleId, ct);
        var now = clock.GetUtcNow();
        var hasPassengers = await db.Bookings.AnyAsync(
            b => b.TripId == tripId && (b.Status == Core.Bookings.BookingStatus.Requested || b.Status == Core.Bookings.BookingStatus.Confirmed), ct);
        trip.Change(
            vehicle.PassengerSeats, request.DepartureAt, request.Seats, Money.ToMinor(request.PricePerSeat, "The price"), request.InstantBook, request.Notes, now,
            hasPassengers);
        await SaveAsync(db, ct);
        return TypedResults.Ok(await TripQueries.DetailAsync(db, tripId, trip.DriverId, null, null, now, ct));
    }

    private static async Task<Ok<TripResponse>> Cancel(
        Guid tripId, ClaimsPrincipal user, Bookings.BookingService bookings, YoldasDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var trip = await bookings.CancelTripAsync(tripId, user.UserId(), ct);
        return TypedResults.Ok(await TripQueries.DetailAsync(db, tripId, trip.DriverId, null, null, clock.GetUtcNow(), ct));
    }

    /// <summary>A trip the user drives; anyone else gets 404 for it, and 403 is never a hint that it exists.</summary>
    internal static async Task<Trip> OwnAsync(YoldasDbContext db, Guid tripId, Guid userId, CancellationToken ct) =>
        await db.Trips.SingleOrDefaultAsync(t => t.Id == tripId && t.DriverId == userId, ct)
        ?? throw new DomainException(ErrorCodes.NotFound, "There is no trip of yours with this id.");

    private static async Task SaveAsync(YoldasDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsSeatsTaken())
        {
            throw new DomainException(ErrorCodes.SeatsTaken, "More seats are booked on part of the route than that. Offer at least as many.");
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new DomainException(ErrorCodes.Conflict, "The trip changed meanwhile. Load it again and retry.");
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Trip {TripId} offered with {Stops} stops over {Km} km")]
    private static partial void LogOffered(ILogger logger, Guid tripId, int stops, double km);
}
