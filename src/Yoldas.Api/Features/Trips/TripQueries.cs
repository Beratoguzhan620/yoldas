using Microsoft.EntityFrameworkCore;
using Yoldas.Api.Common;
using Yoldas.Core;
using Yoldas.Core.Geo;
using Yoldas.Core.Trips;
using Yoldas.Core.Users;
using Yoldas.Infrastructure.Persistence;

namespace Yoldas.Api.Features.Trips;

public enum TripSort
{
    Departure,
    Price,
    Closest,
}

/// <summary>Finding trips that pass near two places in the right order, and describing them.</summary>
internal static class TripQueries
{
    public const double DefaultRadiusKm = 15;
    public const double MaxRadiusKm = 50;

    /// <summary>A passenger rides at least this far; shorter is a walk.</summary>
    public const int MinRideMeters = 2_000;

    private const int Candidates = 300;

    public static double Km(int meters) => Math.Round(meters / 1000.0, 1);

    /// <summary>
    /// Where a passenger going from one place to another would join and leave this trip, and what they would pay;
    /// null if the trip goes the other way or would carry them less than two kilometres.
    /// </summary>
    public static MatchResponse? Match(Trip trip, (double Latitude, double Longitude) from, (double Latitude, double Longitude) to)
    {
        var pickup = trip.Route.Locate(GeoPoint.At(from.Latitude, from.Longitude));
        var dropoff = trip.Route.Locate(GeoPoint.At(to.Latitude, to.Longitude));
        if (dropoff.MetersAlong - pickup.MetersAlong < MinRideMeters)
        {
            return null;
        }

        return new MatchResponse(
            Point(trip, pickup), Point(trip, dropoff), Km(dropoff.MetersAlong - pickup.MetersAlong),
            Money.FromMinor(trip.PriceFor(pickup.MetersAlong, dropoff.MetersAlong)));
    }

    private static MatchPoint Point(Trip trip, RoutePosition position) => new(
        position.Latitude, position.Longitude, Km(position.MetersAlong), Km(position.MetersOff),
        trip.Stops.OrderBy(s => Math.Abs(s.MetersAlong - position.MetersAlong)).First().Name, trip.PassesAt(position.MetersAlong));

    public static DriverResponse Driver(User user) => new(user.Id, user.DisplayName, user.CreatedAt);

    public static CarResponse Car(Vehicle vehicle, bool showPlate) => new(vehicle.Make, vehicle.Model, vehicle.Colour, showPlate ? vehicle.Plate : null);

    public static TripSummary Summary(Trip trip, User driver, Vehicle vehicle, int seatsLeft, MatchResponse? match) => new(
        trip.Id, trip.Stops[0].Name, trip.Stops[^1].Name, trip.DepartureAt, trip.ArrivalAt, Driver(driver), Car(vehicle, showPlate: false), trip.Seats,
        seatsLeft, Money.FromMinor(trip.PricePerSeatMinor), trip.InstantBook, trip.Status, Km(trip.LengthMeters), match);

    public static async Task<IReadOnlyList<TripSummary>> SearchAsync(
        YoldasDbContext db, (double Latitude, double Longitude) from, (double Latitude, double Longitude) to, DateOnly? date, int seats, double radiusKm,
        TripSort sort, DateTimeOffset now, CancellationToken ct)
    {
        var fromPoint = GeoPoint.At(from.Latitude, from.Longitude);
        var toPoint = GeoPoint.At(to.Latitude, to.Longitude);
        var meters = Math.Clamp(radiusKm, 1, MaxRadiusKm) * 1000;
        var query = db.Trips.AsNoTracking().Where(t => t.Status == TripStatus.Scheduled && t.DepartureAt > now);
        if (date is { } day)
        {
            var (start, end) = (Clock.StartOf(day).ToUniversalTime(), Clock.StartOf(day.AddDays(1)).ToUniversalTime());
            query = query.Where(t => t.DepartureAt >= start && t.DepartureAt < end);
        }

        // PostGIS narrows it down with the index on the route; the order along the route is checked here.
        var trips = await query
            .Where(t => t.Path.IsWithinDistance(fromPoint, meters) && t.Path.IsWithinDistance(toPoint, meters))
            .OrderBy(t => t.DepartureAt)
            .Take(Candidates)
            .ToListAsync(ct);

        var matches = trips.Select(t => (Trip: t, Match: Match(t, from, to))).Where(m => m.Match is not null).ToList();
        var left = await SeatsLeftAsync(db, matches.Select(m => m.Trip.Id).ToList(), ct);
        var people = await PeopleAsync(db, matches.Select(m => m.Trip).ToList(), ct);
        var found = matches
            .Select(m => (m.Trip, m.Match, Left: left.GetValueOrDefault(m.Trip.Id, m.Trip.Seats)))
            .Where(m => m.Left >= seats)
            .Select(m => Summary(m.Trip, people.Drivers[m.Trip.DriverId], people.Vehicles[m.Trip.VehicleId], m.Left, m.Match));

        return sort switch
        {
            TripSort.Price => [.. found.OrderBy(s => s.Match!.Price).ThenBy(s => s.DepartureAt)],
            TripSort.Closest => [.. found.OrderBy(s => s.Match!.Pickup.KmFromYou + s.Match.Dropoff.KmFromYou).ThenBy(s => s.DepartureAt)],
            _ => [.. found.OrderBy(s => s.DepartureAt)],
        };
    }

    /// <summary>Seats nobody has taken anywhere on the route. Bookings fill this in.</summary>
    public static Task<Dictionary<Guid, int>> SeatsLeftAsync(YoldasDbContext db, IReadOnlyList<Guid> tripIds, CancellationToken ct) =>
        Task.FromResult(new Dictionary<Guid, int>());

    public static async Task<(Dictionary<Guid, User> Drivers, Dictionary<Guid, Vehicle> Vehicles)> PeopleAsync(
        YoldasDbContext db, IReadOnlyList<Trip> trips, CancellationToken ct)
    {
        var driverIds = trips.Select(t => t.DriverId).Distinct().ToList();
        var vehicleIds = trips.Select(t => t.VehicleId).Distinct().ToList();
        var drivers = await db.Users.AsNoTracking().Where(u => driverIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, ct);
        var vehicles = await db.Vehicles.AsNoTracking().Where(v => vehicleIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, ct);
        return (drivers, vehicles);
    }

    public static async Task<TripResponse> DetailAsync(
        YoldasDbContext db, Guid tripId, Guid? viewerId, (double, double)? from, (double, double)? to, DateTimeOffset now, CancellationToken ct)
    {
        var trip = await db.Trips.AsNoTracking().SingleOrDefaultAsync(t => t.Id == tripId, ct)
            ?? throw new DomainException(ErrorCodes.NotFound, "There is no trip with this id.");
        var mine = trip.DriverId == viewerId;
        if (trip.Status == TripStatus.Cancelled && !mine && trip.CancelledAt < now - TimeSpan.FromDays(7))
        {
            throw new DomainException(ErrorCodes.NotFound, "There is no trip with this id.");
        }

        var driver = await db.Users.AsNoTracking().SingleAsync(u => u.Id == trip.DriverId, ct);
        var vehicle = await db.Vehicles.AsNoTracking().SingleAsync(v => v.Id == trip.VehicleId, ct);
        var left = (await SeatsLeftAsync(db, [trip.Id], ct)).GetValueOrDefault(trip.Id, trip.Seats);
        var match = from is { } f && to is { } t ? Match(trip, f, t) : null;
        return new TripResponse(
            trip.Id, Driver(driver), Car(vehicle, showPlate: mine), Stops(trip), Km(trip.LengthMeters), (int)trip.Duration.TotalMinutes, trip.DepartureAt,
            trip.ArrivalAt, trip.Seats, left, Money.FromMinor(trip.PricePerSeatMinor), Money.FromMinor(Trip.MaxPriceFor(trip.LengthMeters)), trip.InstantBook,
            trip.Notes, trip.Status, mine, match);
    }

    public static IReadOnlyList<StopResponse> Stops(Trip trip) =>
        [.. trip.Stops.OrderBy(s => s.Index).Select(s => new StopResponse(s.Index, s.Name, s.Location.Y, s.Location.X, Km(s.MetersAlong), trip.PassesAt(s.MetersAlong)))];
}
