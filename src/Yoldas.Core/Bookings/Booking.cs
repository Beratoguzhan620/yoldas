using NetTopologySuite.Geometries;
using Yoldas.Core.Geo;
using Yoldas.Core.Trips;

namespace Yoldas.Core.Bookings;

public enum BookingStatus
{
    Requested,
    Confirmed,
    Declined,
    Expired,
    Cancelled,
    Boarded,
    NoShow,
    Completed,
}

/// <summary>Who called a seat off, which counts against them if it was late.</summary>
public enum CancelledBy
{
    Passenger,
    Driver,
}

/// <summary>
/// Seats on part of a trip: where the passenger gets in and out, as positions along the route, and the price agreed
/// for them. Money changes hands in the car, so the price here is what the driver may ask, no more.
/// </summary>
public sealed class Booking
{
    public const int MessageMax = 300;
    public const int ReasonMax = 300;

    /// <summary>Asking needs the car to reach you at least this far ahead; instant seats too.</summary>
    public static readonly TimeSpan BookAhead = TimeSpan.FromMinutes(30);

    /// <summary>A driver answers within 12 hours, and at least half an hour before the pickup.</summary>
    public static readonly TimeSpan AnswerWithin = TimeSpan.FromHours(12);

    /// <summary>Calling off a confirmed seat closer than this to the pickup counts as late.</summary>
    public static readonly TimeSpan LateWithin = TimeSpan.FromHours(24);

    private Booking()
    {
    }

    public Guid Id { get; private set; }

    public Guid TripId { get; private set; }

    public Guid DriverId { get; private set; }

    public Guid PassengerId { get; private set; }

    public int Seats { get; private set; }

    public int FromMeters { get; private set; }

    public int ToMeters { get; private set; }

    public Point Pickup { get; private set; } = null!;

    public Point Dropoff { get; private set; } = null!;

    public string PickupNear { get; private set; } = string.Empty;

    public string DropoffNear { get; private set; } = string.Empty;

    /// <summary>When the car should reach the pickup, by the trip's times when the seat was booked.</summary>
    public DateTimeOffset PickupAt { get; private set; }

    public DateTimeOffset DropoffAt { get; private set; }

    /// <summary>For one seat, in kuruş.</summary>
    public long PricePerSeatMinor { get; private set; }

    public long TotalMinor => PricePerSeatMinor * Seats;

    public string Message { get; private set; } = string.Empty;

    public BookingStatus Status { get; private set; }

    public string Reason { get; private set; } = string.Empty;

    public CancelledBy? CancelledBy { get; private set; }

    public bool LateCancellation { get; private set; }

    public DateTimeOffset RequestedAt { get; private set; }

    public DateTimeOffset RespondBy { get; private set; }

    public DateTimeOffset? ConfirmedAt { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    public bool Holds => Status is BookingStatus.Confirmed or BookingStatus.Boarded;

    public bool IsOpen => Status is BookingStatus.Requested or BookingStatus.Confirmed;

    public static Booking Request(Trip trip, Guid passengerId, int seats, RoutePosition pickup, RoutePosition dropoff, string? message, DateTimeOffset now)
    {
        if (trip.Status != TripStatus.Scheduled)
        {
            throw new DomainException(ErrorCodes.InvalidState, "This trip is not taking passengers.");
        }

        if (trip.DriverId == passengerId)
        {
            throw new DomainException(ErrorCodes.InvalidState, "You cannot book a seat in your own car.");
        }

        if (seats < 1 || seats > trip.Seats)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, $"Book 1 to {trip.Seats} seats.");
        }

        if (dropoff.MetersAlong - pickup.MetersAlong < 2_000)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "This trip does not go from there to there: it goes the other way, or not far enough.");
        }

        var pickupAt = trip.PassesAt(pickup.MetersAlong);
        if (pickupAt < now + BookAhead)
        {
            throw new DomainException(ErrorCodes.InvalidState, "The car reaches you too soon to book; seats close half an hour before.");
        }

        var instant = trip.InstantBook;
        var respondBy = Min(now + AnswerWithin, pickupAt - BookAhead);
        return new Booking
        {
            Id = Guid.CreateVersion7(now), TripId = trip.Id, DriverId = trip.DriverId, PassengerId = passengerId, Seats = seats,
            FromMeters = pickup.MetersAlong, ToMeters = dropoff.MetersAlong,
            Pickup = GeoPoint.At(pickup.Latitude, pickup.Longitude), Dropoff = GeoPoint.At(dropoff.Latitude, dropoff.Longitude),
            PickupNear = Nearest(trip, pickup.MetersAlong), DropoffNear = Nearest(trip, dropoff.MetersAlong),
            PickupAt = pickupAt, DropoffAt = trip.PassesAt(dropoff.MetersAlong), PricePerSeatMinor = trip.PriceFor(pickup.MetersAlong, dropoff.MetersAlong),
            Message = Text.CleanParagraphs(message, MessageMax, "A message"),
            Status = instant ? BookingStatus.Confirmed : BookingStatus.Requested, RequestedAt = now, RespondBy = respondBy,
            ConfirmedAt = instant ? now : null,
        };
    }

    public void Accept(Guid by, DateTimeOffset now)
    {
        RequireDriver(by, "Only the driver accepts a request.");
        Expect(BookingStatus.Requested, "Only a request waiting for an answer can be accepted.");
        if (now >= RespondBy)
        {
            throw new DomainException(ErrorCodes.InvalidState, "The time to answer this request has passed.");
        }

        Status = BookingStatus.Confirmed;
        ConfirmedAt = now;
    }

    public void Decline(Guid by, string? reason, DateTimeOffset now)
    {
        RequireDriver(by, "Only the driver declines a request.");
        Expect(BookingStatus.Requested, "Only a request waiting for an answer can be declined.");
        Close(BookingStatus.Declined, Text.CleanParagraphs(reason, ReasonMax, "A reason"), now);
    }

    public void Expire(DateTimeOffset now)
    {
        Expect(BookingStatus.Requested, "Only a request waiting for an answer can lapse.");
        Close(BookingStatus.Expired, "The driver did not answer in time.", now);
    }

    /// <summary>
    /// Either side calls the seat off before the pickup. A confirmed seat called off within a day of the pickup is
    /// late, and shows on the profile of whoever did it.
    /// </summary>
    public void Cancel(Guid by, string? reason, DateTimeOffset now)
    {
        if (by != PassengerId && by != DriverId)
        {
            throw new DomainException(ErrorCodes.NotFound, "There is no booking with this id.");
        }

        if (!IsOpen)
        {
            throw new DomainException(ErrorCodes.InvalidState, "Only a booking that is waiting or confirmed can be cancelled.");
        }

        if (now >= PickupAt)
        {
            throw new DomainException(ErrorCodes.InvalidState, "The pickup time has passed; the driver marks whether you came.");
        }

        CancelledBy = by == DriverId ? Bookings.CancelledBy.Driver : Bookings.CancelledBy.Passenger;
        LateCancellation = Status == BookingStatus.Confirmed && PickupAt - now < LateWithin;
        Close(BookingStatus.Cancelled, Text.CleanParagraphs(reason, ReasonMax, "A reason"), now);
    }

    /// <summary>The driver called the whole trip off; it counts as the driver's.</summary>
    public void TripCancelled(DateTimeOffset now)
    {
        if (!IsOpen)
        {
            return;
        }

        CancelledBy = Bookings.CancelledBy.Driver;
        LateCancellation = Status == BookingStatus.Confirmed && PickupAt - now < LateWithin;
        Close(BookingStatus.Cancelled, "The driver cancelled the trip.", now);
    }

    public SeatSpan Span => new(FromMeters, ToMeters, Seats);

    private void Close(BookingStatus status, string reason, DateTimeOffset now)
    {
        Status = status;
        Reason = reason;
        ClosedAt = now;
    }

    private void Expect(BookingStatus status, string message)
    {
        if (Status != status)
        {
            throw new DomainException(ErrorCodes.InvalidState, message);
        }
    }

    private void RequireDriver(Guid by, string message)
    {
        if (by != DriverId)
        {
            throw new DomainException(by == PassengerId ? ErrorCodes.Forbidden : ErrorCodes.NotFound, by == PassengerId ? message : "There is no booking with this id.");
        }
    }

    private static string Nearest(Trip trip, int meters) => trip.Stops.OrderBy(s => Math.Abs(s.MetersAlong - meters)).First().Name;

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;
}
