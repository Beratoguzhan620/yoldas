using Microsoft.EntityFrameworkCore;
using Yoldas.Api.Common;
using Yoldas.Api.Features.Trips;
using Yoldas.Core;
using Yoldas.Core.Bookings;
using Yoldas.Core.Trips;
using Yoldas.Infrastructure.Persistence;

namespace Yoldas.Api.Features.Bookings;

internal static class BookingQueries
{
    public static async Task<BookingResponse> DetailAsync(YoldasDbContext db, Guid bookingId, Guid viewerId, DateTimeOffset now, CancellationToken ct)
    {
        var booking = await db.Bookings.AsNoTracking().SingleOrDefaultAsync(b => b.Id == bookingId && (b.DriverId == viewerId || b.PassengerId == viewerId), ct)
            ?? throw new DomainException(ErrorCodes.NotFound, "There is no booking with this id.");
        var trip = await db.Trips.AsNoTracking().SingleAsync(t => t.Id == booking.TripId, ct);
        var vehicle = await db.Vehicles.AsNoTracking().SingleAsync(v => v.Id == trip.VehicleId, ct);
        var names = await NamesAsync(db, [booking.DriverId, booking.PassengerId], ct);
        var isDriver = booking.DriverId == viewerId;
        var reviews = await db.Reviews.AsNoTracking().Where(r => r.BookingId == bookingId).ToListAsync(ct);
        var mine = reviews.SingleOrDefault(r => r.AuthorId == viewerId);
        var theirs = reviews.SingleOrDefault(r => r.AuthorId != viewerId);
        BookingReview? AsReview(Core.Reviews.Review? r) => r is null ? null : new BookingReview(r.Stars, r.Comment, r.CreatedAt);
        var started = trip.Status == TripStatus.Started && booking.Status == BookingStatus.Confirmed;
        return new BookingResponse(
            booking.Id, TripOf(trip), new PersonResponse(booking.DriverId, names[booking.DriverId]), new PersonResponse(booking.PassengerId, names[booking.PassengerId]),
            TripQueries.Car(vehicle, showPlate: isDriver || booking.Holds), booking.Seats,
            new MatchPoint(booking.Pickup.Y, booking.Pickup.X, TripQueries.Km(booking.FromMeters), 0, booking.PickupNear, booking.PickupAt),
            new MatchPoint(booking.Dropoff.Y, booking.Dropoff.X, TripQueries.Km(booking.ToMeters), 0, booking.DropoffNear, booking.DropoffAt),
            TripQueries.Km(booking.ToMeters - booking.FromMeters), Money.FromMinor(booking.PricePerSeatMinor), Money.FromMinor(booking.TotalMinor), booking.Status,
            booking.Message, booking.Reason, booking.CancelledBy, booking.LateCancellation, booking.RequestedAt, booking.RespondBy, booking.ConfirmedAt, isDriver,
            isDriver && booking.Status == BookingStatus.Requested && now < booking.RespondBy,
            booking.IsOpen && now < booking.PickupAt,
            !isDriver && booking.Status == BookingStatus.Confirmed ? booking.BoardingCode : null,
            booking.BoardedAt,
            isDriver && started,
            isDriver && started && ((trip.LiveMetersAlong ?? 0) > booking.FromMeters || now >= booking.PickupAt + Booking.NoShowAfter),
            new BookingReviews(mine is null && booking.CanBeReviewed(now), AsReview(mine), AsReview(theirs)));
    }

    public static async Task<IReadOnlyList<BookingSummary>> MineAsync(YoldasDbContext db, Guid passengerId, bool past, DateTimeOffset now, CancellationToken ct)
    {
        var query = db.Bookings.AsNoTracking().Where(b => b.PassengerId == passengerId);
        var since = now - TimeSpan.FromHours(12);
        query = past
            ? query.Where(b => b.DropoffAt < since || !(b.Status == BookingStatus.Requested || b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.Boarded))
                .OrderByDescending(b => b.PickupAt)
            : query.Where(b => b.DropoffAt >= since && (b.Status == BookingStatus.Requested || b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.Boarded))
                .OrderBy(b => b.PickupAt);
        var bookings = await query.Take(100).ToListAsync(ct);
        var tripIds = bookings.Select(b => b.TripId).Distinct().ToList();
        var trips = await db.Trips.AsNoTracking().Where(t => tripIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);
        var names = await NamesAsync(db, bookings.Select(b => b.DriverId), ct);
        return [.. bookings.Select(b => new BookingSummary(
            b.Id, TripOf(trips[b.TripId]), new PersonResponse(b.DriverId, names[b.DriverId]), b.Seats, b.PickupNear, b.PickupAt, b.DropoffNear,
            Money.FromMinor(b.TotalMinor), b.Status, IsDriver: false))];
    }

    /// <summary>The driver's view of who is coming, and how full the car is on each leg.</summary>
    public static async Task<TripPassengersResponse> PassengersAsync(YoldasDbContext db, Guid tripId, Guid driverId, CancellationToken ct)
    {
        var trip = await db.Trips.AsNoTracking().SingleOrDefaultAsync(t => t.Id == tripId && t.DriverId == driverId, ct)
            ?? throw new DomainException(ErrorCodes.NotFound, "There is no trip of yours with this id.");
        var bookings = await db.Bookings.AsNoTracking().Where(b => b.TripId == tripId).OrderBy(b => b.FromMeters).ThenBy(b => b.RequestedAt).ToListAsync(ct);
        var names = await NamesAsync(db, bookings.Select(b => b.PassengerId), ct);
        var passengerIds = bookings.Select(b => b.PassengerId).Distinct().ToList();
        var ratings = await db.Users.AsNoTracking().Where(u => passengerIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => Core.Users.User.Average(u.PassengerRatingSum, u.PassengerRatingCount), ct);
        var holding = bookings.Where(b => b.Holds).Select(b => b.Span).ToList();
        var stops = trip.Stops.OrderBy(s => s.Index).ToList();
        var legs = stops.Zip(stops.Skip(1))
            .Select(p => new LegLoad(p.First.Name, p.Second.Name, Seats.MaxTaken(holding, p.First.MetersAlong, p.Second.MetersAlong), trip.Seats))
            .ToList();
        return new TripPassengersResponse(
            [.. bookings.Select(b => new PassengerResponse(
                b.Id, new PersonResponse(b.PassengerId, names[b.PassengerId]), ratings[b.PassengerId], b.Seats, b.PickupNear, TripQueries.Km(b.FromMeters), b.PickupAt, b.DropoffNear,
                TripQueries.Km(b.ToMeters), Money.FromMinor(b.TotalMinor), b.Status, b.Message, b.RespondBy))],
            legs);
    }

    public static BookingTripResponse TripOf(Trip trip) => new(trip.Id, trip.Stops[0].Name, trip.Stops[^1].Name, trip.DepartureAt, trip.Status);

    private static async Task<Dictionary<Guid, string>> NamesAsync(YoldasDbContext db, IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return await db.Users.AsNoTracking().Where(u => list.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
    }
}
