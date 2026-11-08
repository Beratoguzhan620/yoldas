using NetTopologySuite.Geometries;
using Yoldas.Core.Geo;

namespace Yoldas.Core.Trips;

public enum TripStatus
{
    Scheduled,
    Started,
    Finished,
    Cancelled,
}

/// <summary>A place the driver passes, named as they would say it: "Samsun Otogar", "Havza çıkışı".</summary>
public sealed class TripStop
{
    private TripStop()
    {
    }

    public Guid TripId { get; private set; }

    public int Index { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public Point Location { get; private set; } = null!;

    public int MetersAlong { get; private set; }

    internal static TripStop Create(Guid tripId, int index, string name, Point location, int metersAlong) =>
        new() { TripId = tripId, Index = index, Name = name, Location = location, MetersAlong = metersAlong };
}

public sealed record StopInput(string? Name, double Latitude, double Longitude);

/// <summary>
/// A drive with seats to share. The route is the straight legs between the driver's stops; passengers join and leave
/// anywhere along it, and pay their share of the price for the part they ride.
/// </summary>
public sealed class Trip
{
    public const int MinStops = 2;
    public const int MaxStops = 8;
    public const int MinLegMeters = 1_000;
    public const int MinLengthMeters = 5_000;
    public const int MaxLengthMeters = 2_000_000;
    public const int NotesMax = 500;

    /// <summary>The average speed for times along the route: legs are straight, roads are not.</summary>
    public const double AverageKmPerHour = 65;

    /// <summary>Sharing costs, not earning: at most 3 TL a kilometre of route for a seat, and never less than 10 TL.</summary>
    public const long MaxMinorPerKm = 300;

    public const long MinPriceMinor = 10_00;

    public static readonly TimeSpan EarliestDeparture = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan LatestDeparture = TimeSpan.FromDays(90);

    private readonly List<TripStop> _stops = [];
    private Route? _route;

    private Trip()
    {
    }

    public Guid Id { get; private set; }

    public Guid DriverId { get; private set; }

    public Guid VehicleId { get; private set; }

    public IReadOnlyList<TripStop> Stops => _stops;

    /// <summary>The legs as one line, indexed in PostGIS so trips passing near a place are found quickly.</summary>
    public LineString Path { get; private set; } = null!;

    public int LengthMeters { get; private set; }

    public DateTimeOffset DepartureAt { get; private set; }

    public int Seats { get; private set; }

    /// <summary>The price of a seat for the whole route, in kuruş.</summary>
    public long PricePerSeatMinor { get; private set; }

    public bool InstantBook { get; private set; }

    public string Notes { get; private set; } = string.Empty;

    public TripStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public Route Route => _route ??= new Route([.. _stops.OrderBy(s => s.Index).Select(s => s.Location)]);

    public TimeSpan Duration => DurationFor(LengthMeters);

    public DateTimeOffset ArrivalAt => DepartureAt + Duration;

    public static TimeSpan DurationFor(int meters) => TimeSpan.FromMinutes(Math.Round(meters / 1000.0 / AverageKmPerHour * 60));

    /// <summary>When the car passes a point of the route, by the average speed.</summary>
    public DateTimeOffset PassesAt(int metersAlong) => DepartureAt + DurationFor(metersAlong);

    public static Trip Offer(
        Guid driverId, Guid vehicleId, int vehicleSeats, IReadOnlyList<StopInput> stops, DateTimeOffset departureAt, int seats, long pricePerSeatMinor,
        bool instantBook, string? notes, DateTimeOffset now)
    {
        if (stops.Count is < MinStops or > MaxStops)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, $"A trip has {MinStops} to {MaxStops} stops, the start and the end included.");
        }

        var points = stops.Select(s => GeoPoint.At(s.Latitude, s.Longitude)).ToList();
        var route = new Route(points);
        for (var i = 1; i < route.StopPositions.Count; i++)
        {
            if (route.StopPositions[i] - route.StopPositions[i - 1] < MinLegMeters)
            {
                throw new DomainException(ErrorCodes.ValidationFailed, "Stops are at least a kilometre apart.");
            }
        }

        if (route.LengthMeters is < MinLengthMeters or > MaxLengthMeters)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "A trip is 5 to 2,000 km long.");
        }

        var names = stops.Select((s, i) => Text.Clean(s.Name, 2, 60, $"Stop {i + 1}'s name")).ToList();
        var trip = new Trip
        {
            Id = Guid.CreateVersion7(now), DriverId = driverId, VehicleId = vehicleId, Status = TripStatus.Scheduled, CreatedAt = now,
            Path = route.ToLineString(), LengthMeters = route.LengthMeters, _route = route,
        };
        for (var i = 0; i < points.Count; i++)
        {
            trip._stops.Add(TripStop.Create(trip.Id, i, names[i], points[i], route.StopPositions[i]));
        }

        trip.Change(vehicleSeats, departureAt, seats, pricePerSeatMinor, instantBook, notes, now);
        return trip;
    }

    /// <summary>
    /// Changes what can change before the trip. The route cannot: passengers' seats are on parts of it, so a different
    /// route is a different trip.
    /// </summary>
    public void Change(
        int vehicleSeats, DateTimeOffset departureAt, int seats, long pricePerSeatMinor, bool instantBook, string? notes, DateTimeOffset now, bool hasPassengers = false)
    {
        RequireScheduled("Only a trip that has not started can be changed.");
        if (hasPassengers && (departureAt.ToUniversalTime() != DepartureAt || pricePerSeatMinor != PricePerSeatMinor))
        {
            throw new DomainException(ErrorCodes.InvalidState,
                "People have asked for seats at this time and price. Cancel the trip and offer a new one to change them.");
        }

        if (departureAt < now + EarliestDeparture || departureAt > now + LatestDeparture)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "A trip leaves at least 30 minutes from now and within 90 days.");
        }

        if (seats < 1 || seats > vehicleSeats)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, $"Offer 1 to {vehicleSeats} seats; that is what the car takes.");
        }

        var max = MaxPriceFor(LengthMeters);
        if (pricePerSeatMinor < MinPriceMinor || pricePerSeatMinor > max)
        {
            throw new DomainException(ErrorCodes.ValidationFailed,
                $"A seat for the whole route costs {MinPriceMinor / 100} to {max / 100} TL: enough to share the fuel, not to make a profit.");
        }

        // Kept in UTC like every other time; shown in Turkey's time by whoever displays it.
        DepartureAt = departureAt.ToUniversalTime();
        Seats = seats;
        PricePerSeatMinor = pricePerSeatMinor;
        InstantBook = instantBook;
        Notes = Text.CleanParagraphs(notes, NotesMax, "Notes");
    }

    /// <summary>The highest price a seat for the whole route may have: 3 TL a kilometre, in whole lira.</summary>
    public static long MaxPriceFor(int lengthMeters) => Math.Max(MinPriceMinor, (long)Math.Floor(lengthMeters / 1000.0 * MaxMinorPerKm / 100) * 100);

    /// <summary>
    /// A seat from one point to another: the share of the route ridden, of the full price, to the nearest lira and at
    /// least 10 TL. Half the route costs half.
    /// </summary>
    public long PriceFor(int fromMeters, int toMeters)
    {
        var share = (double)(toMeters - fromMeters) / LengthMeters;
        var lira = Math.Round(PricePerSeatMinor * share / 100, MidpointRounding.AwayFromZero);
        return Math.Clamp((long)lira * 100, Math.Min(MinPriceMinor, PricePerSeatMinor), PricePerSeatMinor);
    }

    public void Cancel(DateTimeOffset now)
    {
        RequireScheduled("Only a trip that has not started can be cancelled.");
        Status = TripStatus.Cancelled;
        CancelledAt = now;
    }

    private void RequireScheduled(string message)
    {
        if (Status != TripStatus.Scheduled)
        {
            throw new DomainException(ErrorCodes.InvalidState, message);
        }
    }
}
