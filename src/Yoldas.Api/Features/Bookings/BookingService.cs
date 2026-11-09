using Microsoft.EntityFrameworkCore;
using Yoldas.Api.Features.Trips;
using Yoldas.Core;
using Yoldas.Core.Bookings;
using Yoldas.Core.Geo;
using Yoldas.Core.Trips;
using Yoldas.Infrastructure.Persistence;

namespace Yoldas.Api.Features.Bookings;

/// <summary>
/// Asking for seats, answering, cancelling. Whether the seats are free is decided by the database: a trigger locks
/// the trip and adds up the seats taken on every part of the stretch whenever a booking is confirmed, so two people
/// confirmed at the same moment cannot both get the last seat. The checks here only spare people a request that
/// would fail.
/// </summary>
internal sealed partial class BookingService(YoldasDbContext db, TimeProvider clock, ILogger<BookingService> logger)
{
    public async Task<Booking> RequestAsync(Guid passengerId, BookingRequest request, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var trip = await db.Trips.AsNoTracking().SingleOrDefaultAsync(t => t.Id == request.TripId, ct)
            ?? throw new DomainException(ErrorCodes.NotFound, "There is no trip with this id.");
        var pickup = trip.Route.Locate(GeoPoint.At(request.FromLat, request.FromLng));
        var dropoff = trip.Route.Locate(GeoPoint.At(request.ToLat, request.ToLng));
        if (pickup.MetersOff > TripQueries.MaxRadiusKm * 1000 || dropoff.MetersOff > TripQueries.MaxRadiusKm * 1000)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, $"This trip passes more than {TripQueries.MaxRadiusKm} km from there.");
        }

        var booking = Booking.Request(trip, passengerId, request.Seats, pickup, dropoff, request.Message, now);
        var taken = await TakenAsync(trip.Id, null, ct);
        if (Seats.Free(trip.Seats, taken, booking.FromMeters, booking.ToMeters) < booking.Seats)
        {
            throw SeatsTaken();
        }

        db.Bookings.Add(booking);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsSeatsTaken())
        {
            throw SeatsTaken();
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation("ix_bookings_one_per_passenger"))
        {
            throw new DomainException(ErrorCodes.Conflict, "You already have seats on this trip. Cancel them to book different ones.");
        }

        LogBooked(logger, booking.Id, trip.Id, booking.Status, booking.Seats);
        return booking;
    }

    public Task<Booking> AcceptAsync(Guid bookingId, Guid driverId, CancellationToken ct) =>
        ChangeAsync(bookingId, driverId, b => b.Accept(driverId, clock.GetUtcNow()), ct);

    public Task<Booking> DeclineAsync(Guid bookingId, Guid driverId, string? reason, CancellationToken ct) =>
        ChangeAsync(bookingId, driverId, b => b.Decline(driverId, reason, clock.GetUtcNow()), ct);

    public Task<Booking> CancelAsync(Guid bookingId, Guid userId, string? reason, CancellationToken ct) =>
        ChangeAsync(bookingId, userId, b => b.Cancel(userId, reason, clock.GetUtcNow()), ct);

    /// <summary>The driver calls the trip off: every waiting and confirmed seat goes with it, in one transaction.</summary>
    public async Task<Trip> CancelTripAsync(Guid tripId, Guid driverId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var trip = await TripEndpoints.OwnAsync(db, tripId, driverId, ct);
        trip.Cancel(now);
        var open = await db.Bookings.Where(b => b.TripId == tripId && (b.Status == BookingStatus.Requested || b.Status == BookingStatus.Confirmed)).ToListAsync(ct);
        open.ForEach(b => b.TripCancelled(now));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return trip;
    }

    /// <summary>Requests the driver did not answer in time lapse; returns how many.</summary>
    public async Task<int> ExpireAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var due = await db.Bookings.Where(b => b.Status == BookingStatus.Requested && b.RespondBy <= now).OrderBy(b => b.RespondBy).Take(500).ToListAsync(ct);
        due.ForEach(b => b.Expire(now));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Someone answered meanwhile; the next pass takes whatever is still due.
            return 0;
        }

        return due.Count;
    }

    public async Task<List<SeatSpan>> TakenAsync(Guid tripId, Guid? except, CancellationToken ct) =>
        await db.Bookings.AsNoTracking()
            .Where(b => b.TripId == tripId && b.Id != except && (b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.Boarded))
            .Select(b => new SeatSpan(b.FromMeters, b.ToMeters, b.Seats))
            .ToListAsync(ct);

    private async Task<Booking> ChangeAsync(Guid bookingId, Guid userId, Action<Booking> change, CancellationToken ct)
    {
        var booking = await db.Bookings.SingleOrDefaultAsync(b => b.Id == bookingId && (b.DriverId == userId || b.PassengerId == userId), ct)
            ?? throw new DomainException(ErrorCodes.NotFound, "There is no booking with this id.");
        change(booking);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsSeatsTaken())
        {
            throw SeatsTaken();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new DomainException(ErrorCodes.Conflict, "The booking changed meanwhile. Load it again and retry.");
        }

        return booking;
    }

    private static DomainException SeatsTaken() =>
        new(ErrorCodes.SeatsTaken, "Not enough seats are free on that part of the route any more.");

    [LoggerMessage(Level = LogLevel.Information, Message = "Booking {BookingId} on trip {TripId}: {Status}, {Seats} seats")]
    private static partial void LogBooked(ILogger logger, Guid bookingId, Guid tripId, BookingStatus status, int seats);
}
