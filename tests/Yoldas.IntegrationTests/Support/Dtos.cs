namespace Yoldas.IntegrationTests.Support;

public sealed record UserDto(Guid Id, string Email, string DisplayName);

public sealed record SessionDto(string AccessToken, DateTimeOffset ExpiresAt, UserDto User);

public sealed record MeDto(UserDto User, DateTimeOffset MemberSince, bool HasVehicle, int UpcomingTrips);

public sealed record PlaceDto(string Name, string Province, string Label, double Latitude, double Longitude);

public sealed record VehicleDto(string Make, string Model, string Colour, string Plate, int PassengerSeats);

public sealed record DriverDto(Guid Id, string Name, DateTimeOffset MemberSince, double? Rating, int RatingCount);

public sealed record CarDto(string Make, string Model, string Colour, string? Plate);

public sealed record StopDto(int Index, string Name, double Latitude, double Longitude, double KmAlong, DateTimeOffset At);

public sealed record MatchPointDto(double Latitude, double Longitude, double KmAlong, double KmFromYou, string NearStop, DateTimeOffset At);

public sealed record MatchDto(MatchPointDto Pickup, MatchPointDto Dropoff, double Km, decimal Price, int SeatsFree);

public sealed record TripSummaryDto(
    Guid Id, string From, string To, DateTimeOffset DepartureAt, DateTimeOffset ArrivalAt, DriverDto Driver, CarDto Car, int Seats, int SeatsLeft,
    decimal PricePerSeat, bool InstantBook, string Status, double LengthKm, MatchDto? Match);

public sealed record TripDto(
    Guid Id, DriverDto Driver, CarDto Car, List<StopDto> Stops, double LengthKm, int DurationMinutes, DateTimeOffset DepartureAt, DateTimeOffset ArrivalAt,
    int Seats, int SeatsLeft, decimal PricePerSeat, decimal MaxPricePerSeat, bool InstantBook, string Notes, string Status, bool IsMine, MatchDto? Match,
    Guid? MyBookingId);

public sealed record PersonDto(Guid Id, string Name);

public sealed record BookingTripDto(Guid Id, string From, string To, DateTimeOffset DepartureAt, string Status);

public sealed record BookingDto(
    Guid Id, BookingTripDto Trip, PersonDto Driver, PersonDto Passenger, CarDto Car, int Seats, MatchPointDto Pickup, MatchPointDto Dropoff, double Km,
    decimal PricePerSeat, decimal Total, string Status, string Message, string Reason, string? CancelledBy, bool LateCancellation, DateTimeOffset RequestedAt,
    DateTimeOffset RespondBy, DateTimeOffset? ConfirmedAt, bool IsDriver, bool CanAccept, bool CanCancel, string? BoardingCode, DateTimeOffset? BoardedAt,
    bool CanBoard, bool CanMarkNoShow, BookingReviewsDto Reviews);

public sealed record BookingReviewDto(int Stars, string Comment, DateTimeOffset At);

public sealed record BookingReviewsDto(bool CanReview, BookingReviewDto? Mine, BookingReviewDto? Theirs);

public sealed record BookingSummaryDto(
    Guid Id, BookingTripDto Trip, PersonDto Other, int Seats, string PickupNear, DateTimeOffset PickupAt, string DropoffNear, decimal Total, string Status, bool IsDriver);

public sealed record PassengerDto(
    Guid BookingId, PersonDto Passenger, double? PassengerRating, int Seats, string PickupNear, double PickupKm, DateTimeOffset PickupAt, string DropoffNear, double DropoffKm,
    decimal Total, string Status, string Message, DateTimeOffset RespondBy);

public sealed record LegLoadDto(string From, string To, int Taken, int Seats);

public sealed record TripPassengersDto(List<PassengerDto> Passengers, List<LegLoadDto> Legs);

public sealed record LivePositionDto(double Latitude, double Longitude, double KmAlong, double KmOffRoute, bool OffRoute, double KmPerHour, DateTimeOffset At);

public sealed record StopEtaDto(int Index, string Name, double KmAlong, DateTimeOffset? At);

public sealed record LiveViewDto(Guid TripId, string Status, LivePositionDto? Position, List<StopEtaDto> Stops);

public sealed record ReviewDto(Guid Id, string AuthorName, string Of, int Stars, string Comment, DateTimeOffset At);

public sealed record AsDriverDto(double? Rating, int Reviews, int TripsDriven, int LateCancellations);

public sealed record AsPassengerDto(double? Rating, int Reviews, int Rides, int LateCancellations, int NoShows);

public sealed record ProfileDto(Guid Id, string Name, DateTimeOffset MemberSince, AsDriverDto AsDriver, AsPassengerDto AsPassenger, List<ReviewDto> Reviews);
