namespace Yoldas.IntegrationTests.Support;

public sealed record UserDto(Guid Id, string Email, string DisplayName);

public sealed record SessionDto(string AccessToken, DateTimeOffset ExpiresAt, UserDto User);

public sealed record MeDto(UserDto User, DateTimeOffset MemberSince, bool HasVehicle, int UpcomingTrips);

public sealed record PlaceDto(string Name, string Province, string Label, double Latitude, double Longitude);

public sealed record VehicleDto(string Make, string Model, string Colour, string Plate, int PassengerSeats);

public sealed record DriverDto(Guid Id, string Name, DateTimeOffset MemberSince);

public sealed record CarDto(string Make, string Model, string Colour, string? Plate);

public sealed record StopDto(int Index, string Name, double Latitude, double Longitude, double KmAlong, DateTimeOffset At);

public sealed record MatchPointDto(double Latitude, double Longitude, double KmAlong, double KmFromYou, string NearStop, DateTimeOffset At);

public sealed record MatchDto(MatchPointDto Pickup, MatchPointDto Dropoff, double Km, decimal Price);

public sealed record TripSummaryDto(
    Guid Id, string From, string To, DateTimeOffset DepartureAt, DateTimeOffset ArrivalAt, DriverDto Driver, CarDto Car, int Seats, int SeatsLeft,
    decimal PricePerSeat, bool InstantBook, string Status, double LengthKm, MatchDto? Match);

public sealed record TripDto(
    Guid Id, DriverDto Driver, CarDto Car, List<StopDto> Stops, double LengthKm, int DurationMinutes, DateTimeOffset DepartureAt, DateTimeOffset ArrivalAt,
    int Seats, int SeatsLeft, decimal PricePerSeat, decimal MaxPricePerSeat, bool InstantBook, string Notes, string Status, bool IsMine, MatchDto? Match);
