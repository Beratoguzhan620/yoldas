using Yoldas.Api.Features.Trips;
using Yoldas.Core.Bookings;
using Yoldas.Core.Trips;

namespace Yoldas.Api.Features.Bookings;

/// <summary>Seats on a trip from where you are to where you are going; the trip says where you join and leave it.</summary>
public sealed record BookingRequest(Guid TripId, double FromLat, double FromLng, double ToLat, double ToLng, int Seats = 1, string? Message = null);

public sealed record ReasonRequest(string? Reason);

public sealed record PersonResponse(Guid Id, string Name);

public sealed record BookingTripResponse(Guid Id, string From, string To, DateTimeOffset DepartureAt, TripStatus Status);

/// <summary>
/// A booking as its passenger or driver sees it. The plate is shown once the seat is confirmed, so the passenger can
/// find the car.
/// </summary>
public sealed record BookingResponse(
    Guid Id, BookingTripResponse Trip, PersonResponse Driver, PersonResponse Passenger, CarResponse Car, int Seats, MatchPoint Pickup, MatchPoint Dropoff,
    double Km, decimal PricePerSeat, decimal Total, BookingStatus Status, string Message, string Reason, CancelledBy? CancelledBy, bool LateCancellation,
    DateTimeOffset RequestedAt, DateTimeOffset RespondBy, DateTimeOffset? ConfirmedAt, bool IsDriver, bool CanAccept, bool CanCancel, string? BoardingCode,
    DateTimeOffset? BoardedAt, bool CanBoard, bool CanMarkNoShow, BookingReviews Reviews);

public sealed record BookingReview(int Stars, string Comment, DateTimeOffset At);

/// <summary>Whether you can still review this ride, and the reviews written so far by you and about you.</summary>
public sealed record BookingReviews(bool CanReview, BookingReview? Mine, BookingReview? Theirs);

public sealed record BookingSummary(
    Guid Id, BookingTripResponse Trip, PersonResponse Other, int Seats, string PickupNear, DateTimeOffset PickupAt, string DropoffNear, decimal Total,
    BookingStatus Status, bool IsDriver);

/// <summary>A passenger as the driver sees them on the trip: who, how many, from where to where.</summary>
public sealed record PassengerResponse(
    Guid BookingId, PersonResponse Passenger, double? PassengerRating, int Seats, string PickupNear, double PickupKm, DateTimeOffset PickupAt, string DropoffNear,
    double DropoffKm, decimal Total, BookingStatus Status, string Message, DateTimeOffset RespondBy);

/// <summary>Seats taken between two stops: how full the car is on each leg.</summary>
public sealed record LegLoad(string From, string To, int Taken, int Seats);

public sealed record TripPassengersResponse(IReadOnlyList<PassengerResponse> Passengers, IReadOnlyList<LegLoad> Legs);
