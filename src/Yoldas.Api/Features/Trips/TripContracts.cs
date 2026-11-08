using Yoldas.Core.Trips;

namespace Yoldas.Api.Features.Trips;

public sealed record PlaceResponse(string Name, string Province, string Label, double Latitude, double Longitude);

public sealed record VehicleRequest(string? Make, string? Model, string? Colour, string? Plate, int PassengerSeats);

/// <summary>Your car. The plate is shown in full to you, and to passengers with a confirmed seat.</summary>
public sealed record VehicleResponse(string Make, string Model, string Colour, string Plate, int PassengerSeats);

public sealed record StopRequest(string? Name, double Latitude, double Longitude);

/// <summary>
/// A trip to offer. The stops, in order, make the route; prices are for a seat over the whole route, in TL, and
/// passengers pay their share of it.
/// </summary>
public sealed record TripRequest(
    IReadOnlyList<StopRequest>? Stops, DateTimeOffset DepartureAt, int Seats, decimal PricePerSeat, bool InstantBook = false, string? Notes = null);

/// <summary>What can change before the trip. The route cannot: offer a new trip for a different one.</summary>
public sealed record TripChangeRequest(DateTimeOffset DepartureAt, int Seats, decimal PricePerSeat, bool InstantBook = false, string? Notes = null);

public sealed record DriverResponse(Guid Id, string Name, DateTimeOffset MemberSince);

public sealed record CarResponse(string Make, string Model, string Colour, string? Plate);

/// <summary>A stop, how far along the route it is, and when the car should pass it.</summary>
public sealed record StopResponse(int Index, string Name, double Latitude, double Longitude, double KmAlong, DateTimeOffset At);

/// <summary>Where a passenger joins or leaves: the point of the route closest to them, the nearest stop, and when.</summary>
public sealed record MatchPoint(double Latitude, double Longitude, double KmAlong, double KmFromYou, string NearStop, DateTimeOffset At);

/// <summary>A passenger's part of the trip and its price.</summary>
public sealed record MatchResponse(MatchPoint Pickup, MatchPoint Dropoff, double Km, decimal Price);

public sealed record TripSummary(
    Guid Id, string From, string To, DateTimeOffset DepartureAt, DateTimeOffset ArrivalAt, DriverResponse Driver, CarResponse Car, int Seats, int SeatsLeft,
    decimal PricePerSeat, bool InstantBook, TripStatus Status, double LengthKm, MatchResponse? Match);

public sealed record TripResponse(
    Guid Id, DriverResponse Driver, CarResponse Car, IReadOnlyList<StopResponse> Stops, double LengthKm, int DurationMinutes, DateTimeOffset DepartureAt,
    DateTimeOffset ArrivalAt, int Seats, int SeatsLeft, decimal PricePerSeat, decimal MaxPricePerSeat, bool InstantBook, string Notes, TripStatus Status,
    bool IsMine, MatchResponse? Match);
