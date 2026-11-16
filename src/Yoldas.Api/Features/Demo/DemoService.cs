using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Yoldas.Core.Bookings;
using Yoldas.Core.Geo;
using Yoldas.Core.Reviews;
using Yoldas.Core.Trips;
using Yoldas.Core.Users;
using Yoldas.Infrastructure.Persistence;

namespace Yoldas.Api.Features.Demo;

public sealed class DemoOptions
{
    public const string Section = "Demo";

    /// <summary>Off everywhere except the public demo: seeds people and trips, and drives one car along its route.</summary>
    public bool Enabled { get; set; }

    /// <summary>The password of every demo account. They are public on the sign-in page, so it protects nothing.</summary>
    public string Password { get; set; } = "yoldas-demo";

    /// <summary>How often the car on the road reports where it is.</summary>
    public TimeSpan Tick { get; set; } = TimeSpan.FromSeconds(4);

    /// <summary>How far it moves between reports: 100 m in 4 s is 90 km/h, so the times on screen stay honest.</summary>
    public int MetersPerTick { get; set; } = 100;
}

/// <summary>A demo account shown on the sign-in page.</summary>
public sealed record DemoAccount(string Role, string Name, string Email, string Password, string Description);

/// <summary>
/// The people, cars and trips of the public demo. Everything is created through the domain, the same as through the
/// API, only with the clock set where it needs to be: rides that took place last week, trips over the next days, and
/// one car on the road now. Running it again tops up what visitors used up and changes nothing else.
/// </summary>
public sealed partial class DemoService(YoldasDbContext db, TimeProvider clock, IOptions<DemoOptions> options, ILogger<DemoService> logger)
{
    public const string Domain = "demo.yoldas.app";

    private static readonly PasswordHasher<User> Hasher = new();

    internal static readonly Person Mehmet = new("Mehmet Yıldız", "mehmet", new Car("Renault", "Clio", "Beyaz", "55 ABC 123", 3));
    internal static readonly Person Ayse = new("Ayşe Kaya", "ayse", null);
    private static readonly Person Elif = new("Elif Şahin", "elif", new Car("Toyota", "Corolla", "Gri", "55 ES 410", 4));
    private static readonly Person Burak = new("Burak Aydın", "burak", new Car("Fiat", "Egea", "Kırmızı", "34 BRK 72", 3));
    private static readonly Person Zeynep = new("Zeynep Arslan", "zeynep", new Car("Volkswagen", "Golf", "Lacivert", "06 ZA 1905", 4));
    private static readonly Person Can = new("Can Demir", "can", null);
    private static readonly Person Selin = new("Selin Öztürk", "selin", null);
    private static readonly Person Emre = new("Emre Koç", "emre", null);
    private static readonly Person[] People = [Mehmet, Ayse, Elif, Burak, Zeynep, Can, Selin, Emre];

    internal static readonly StopInput Atakum = new("Atakum", 41.333, 36.271);
    internal static readonly StopInput Otogar = new("Samsun Otogar", 41.2601, 36.2847);
    internal static readonly StopInput Kavak = new("Kavak", 41.0797, 36.0425);
    internal static readonly StopInput Havza = new("Havza", 40.97, 35.6622);
    internal static readonly StopInput Merzifon = new("Merzifon", 40.8733, 35.4633);
    private static readonly StopInput Amasya = new("Amasya", 40.6499, 35.8353);
    private static readonly StopInput Tokat = new("Tokat", 40.3167, 36.55);
    private static readonly StopInput Corum = new("Çorum", 40.5506, 34.9556);
    private static readonly StopInput Sungurlu = new("Sungurlu", 40.1667, 34.3833);
    private static readonly StopInput Kirikkale = new("Kırıkkale", 39.8468, 33.5153);
    private static readonly StopInput Asti = new("AŞTİ", 39.918, 32.81);

    /// <summary>The route of the car on the road: Ayşe waits in Havza.</summary>
    internal static readonly StopInput[] LiveRoute = [Atakum, Otogar, Kavak, Havza, Merzifon];

    public IReadOnlyList<DemoAccount> Accounts =>
    [
        new("Passenger", Ayse.Name, Ayse.Email, options.Value.Password,
            "Waits in Havza for Mehmet's car, which is on the road now; has asked for a seat to Ankara tomorrow."),
        new("Driver", Mehmet.Name, Mehmet.Email, options.Value.Password,
            "Driving to Merzifon now; offers Samsun to Ankara tomorrow, with two passengers and a request to answer."),
    ];

    public async Task SeedAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var users = await EnsurePeopleAsync(now, ct);
        await EnsureHistoryAsync(users, now, ct);
        await EnsureUpcomingAsync(users, now, ct);
    }

    private async Task<Dictionary<Person, (User User, Vehicle? Car)>> EnsurePeopleAsync(DateTimeOffset now, CancellationToken ct)
    {
        var emails = People.Select(p => p.Email).ToList();
        var existing = await db.Users.Where(u => emails.Contains(u.Email)).ToDictionaryAsync(u => u.Email, ct);
        var result = new Dictionary<Person, (User, Vehicle?)>();
        foreach (var person in People)
        {
            if (!existing.TryGetValue(person.Email, out var user))
            {
                // Members for a few months, so profiles do not all say "since today".
                user = User.Register(person.Email, person.Name, now.AddDays(-120 + Array.IndexOf(People, person) * 9));
                user.SetPasswordHash(Hasher.HashPassword(user, options.Value.Password));
                db.Users.Add(user);
            }

            Vehicle? car = null;
            if (person.Car is { } spec)
            {
                car = await db.Vehicles.SingleOrDefaultAsync(v => v.OwnerId == user.Id, ct);
                if (car is null)
                {
                    car = Vehicle.Register(user.Id, spec.Make, spec.Model, spec.Colour, spec.Plate, spec.Seats, now);
                    db.Vehicles.Add(car);
                }
            }

            result[person] = (user, car);
        }

        await db.SaveChangesAsync(ct);
        return result;
    }

    /// <summary>Rides that took place last week, with reviews both ways, so drivers have stars and Ayşe has a past.</summary>
    private async Task EnsureHistoryAsync(Dictionary<Person, (User User, Vehicle? Car)> people, DateTimeOffset now, CancellationToken ct)
    {
        var mehmet = people[Mehmet].User.Id;
        if (await db.Trips.AnyAsync(t => t.DriverId == mehmet && t.Status == TripStatus.Finished, ct))
        {
            return;
        }

        (Person Driver, StopInput[] Route, int DaysAgo, long Price, (Person Who, int From, int To, int Stars, string Words)[] Riders)[] rides =
        [
            (Mehmet, [Atakum, Otogar, Kavak], 9, 60, [(Selin, 0, 2, 5, "Atakum'dan almak için yolunu bile uzattı."), (Emre, 1, 2, 5, "Dakik ve kibar.")]),
            (Mehmet, [Otogar, Havza, Corum, Asti], 6, 380, [(Ayse, 0, 3, 5, "Çok rahat bir yolculuktu, tam saatinde geldi."), (Can, 1, 2, 4, "Güvenli sürüş, sohbet de keyifliydi.")]),
            (Elif, [Otogar, Havza, Merzifon], 5, 120, [(Can, 0, 2, 5, "Araç tertemizdi."), (Selin, 1, 2, 4, "Müzik biraz yüksekti ama keyifliydi.")]),
            (Burak, [Otogar, Amasya, Tokat], 4, 180, [(Emre, 0, 2, 5, "Tokat'a kadar sorunsuz."), (Ayse, 0, 1, 5, "Amasya'da tam kapıya bıraktı.")]),
        ];

        foreach (var ride in rides)
        {
            var (driver, car) = people[ride.Driver];
            var leaves = Day(now, -ride.DaysAgo).AddHours(9);
            var offered = leaves.AddDays(-2);
            var trip = Trip.Offer(driver.Id, car!.Id, car.PassengerSeats, ride.Route, leaves, car.PassengerSeats, ride.Price * 100, true, null, offered);
            db.Trips.Add(trip);
            await db.SaveChangesAsync(ct);

            var bookings = new List<(Booking Booking, (Person Who, int From, int To, int Stars, string Words) Rider)>();
            foreach (var rider in ride.Riders)
            {
                var booking = Book(trip, people[rider.Who].User.Id, ride.Route[rider.From], ride.Route[rider.To], offered.AddHours(3), "");
                bookings.Add((booking, rider));
            }

            await db.SaveChangesAsync(ct);
            trip.Start(leaves.AddMinutes(-5));
            foreach (var (booking, _) in bookings)
            {
                booking.Board(driver.Id, booking.BoardingCode, trip, leaves.AddMinutes(2));
            }

            var arrived = trip.ArrivalAt.AddMinutes(10);
            trip.Finish(arrived);
            foreach (var (booking, rider) in bookings)
            {
                booking.TripFinished(arrived);
                AddReview(Review.Write(booking, people[rider.Who].User.Id, rider.Stars, rider.Words, arrived.AddHours(2)), people[ride.Driver].User);
                AddReview(Review.Write(booking, driver.Id, 5, "Zamanında hazırdı, tavsiye ederim.", arrived.AddHours(3)), people[rider.Who].User);
            }

            await db.SaveChangesAsync(ct);
        }

        LogHistory(logger, rides.Length);
    }

    /// <summary>Trips out of Samsun over the next two days. Topped up whenever fewer than four are still ahead.</summary>
    private async Task EnsureUpcomingAsync(Dictionary<Person, (User User, Vehicle? Car)> people, DateTimeOffset now, CancellationToken ct)
    {
        var drivers = new[] { Mehmet, Elif, Burak, Zeynep }.Select(p => people[p].User.Id).ToList();
        var ahead = await db.Trips.CountAsync(t => drivers.Contains(t.DriverId) && t.Status == TripStatus.Scheduled && t.DepartureAt > now.AddHours(2), ct);
        if (ahead >= 4)
        {
            return;
        }

        var tomorrow = Day(now, 1);
        Trip Offer(Person driver, StopInput[] route, DateTimeOffset at, long price, bool instant, string? notes)
        {
            var (user, car) = people[driver];
            var trip = Trip.Offer(user.Id, car!.Id, car.PassengerSeats, route, at, Math.Min(3, car.PassengerSeats), price * 100, instant, notes, now);
            db.Trips.Add(trip);
            return trip;
        }

        var main = Offer(Mehmet, [Otogar, Havza, Corum, Sungurlu, Kirikkale, Asti], tomorrow.AddHours(8.5), 380, false,
            "Bagaj geniş. Sigara içilmiyor; Çorum'da kısa bir mola var.");
        Offer(Elif, [Atakum, Havza, Merzifon, Corum, Asti], tomorrow.AddHours(7.25), 360, true, "Kadın yolculara öncelik.");
        Offer(Zeynep, [Otogar, Kavak, Havza, Corum], tomorrow.AddHours(10), 170, true, null);
        var burak = Offer(Burak, [Otogar, Havza, Corum, Kirikkale, Asti], tomorrow.AddHours(13.75), 350, false, null);
        Offer(Zeynep, [Otogar, Amasya, Tokat], tomorrow.AddDays(1).AddHours(9), 180, true, null);
        Offer(Elif, [Asti, Kirikkale, Corum, Havza, Otogar], tomorrow.AddDays(1).AddHours(16), 360, true, "Dönüş yolu; Havza'da çay molası.");
        await db.SaveChangesAsync(ct);

        var mehmet = people[Mehmet].User.Id;
        Book(main, people[Can].User.Id, Otogar, Havza, now, "").Accept(mehmet, now);
        Book(main, people[Selin].User.Id, Havza, Asti, now, "Ankara'da okula dönüyorum.").Accept(mehmet, now);
        Book(main, people[Emre].User.Id, Corum, Asti, now, "Kırıkkale'de de inebilirim.");
        Book(burak, people[Ayse].User.Id, Otogar, Asti, now, "İki valizim var, sığar mı?");
        await db.SaveChangesAsync(ct);
        LogUpcoming(logger, 6);
    }

    private void AddReview(Review review, User subject)
    {
        db.Reviews.Add(review);
        subject.AddRating(review.Of == ReviewOf.Driver, review.Stars);
    }

    internal Booking Book(Trip trip, Guid passengerId, StopInput from, StopInput to, DateTimeOffset now, string message)
    {
        var booking = Booking.Request(
            trip, passengerId, 1, trip.Route.Locate(GeoPoint.At(from.Latitude, from.Longitude)), trip.Route.Locate(GeoPoint.At(to.Latitude, to.Longitude)), message, now);
        db.Bookings.Add(booking);
        return booking;
    }

    /// <summary>Midnight of a day in Turkey (UTC+3), some days from now.</summary>
    internal static DateTimeOffset Day(DateTimeOffset now, int days)
    {
        var local = now.ToOffset(TimeSpan.FromHours(3));
        return new DateTimeOffset(local.Date.AddDays(days), TimeSpan.FromHours(3)).ToUniversalTime();
    }

    internal sealed record Car(string Make, string Model, string Colour, string Plate, int Seats);

    internal sealed record Person(string Name, string Handle, Car? Car)
    {
        public string Email => $"{Handle}@{Domain}";
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Demo: {Count} past rides with reviews created")]
    private static partial void LogHistory(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Demo: {Count} upcoming trips created")]
    private static partial void LogUpcoming(ILogger logger, int count);
}
