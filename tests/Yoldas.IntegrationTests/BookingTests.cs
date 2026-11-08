using System.Net;
using System.Net.Http.Json;
using Yoldas.IntegrationTests.Support;
using static Yoldas.IntegrationTests.Support.TestApi;

namespace Yoldas.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class BookingTests(ApiFactory factory)
{
    private async Task<(Person Driver, TripDto Trip)> OfferAsync(int seats = 3, bool instant = false, int day = 2, DateTimeOffset? departure = null)
    {
        var driver = await factory.RegisterAsync("Mehmet Usta");
        await driver.DriveAsync();
        return (driver, await driver.OfferAsync(Trip(departure ?? factory.At(day, 8), seats: seats, instantBook: instant)));
    }

    [Fact]
    public async Task A_request_waits_for_the_driver_and_the_plate_shows_once_it_is_accepted()
    {
        var (driver, trip) = await OfferAsync();
        using var _ = driver;
        using var passenger = await factory.RegisterAsync("Ayşe Kaya");
        using var stranger = await factory.RegisterAsync();

        var asked = await passenger.BookAsync(trip.Id, Havza, Ankara, seats: 2, message: "İki kişiyiz, bir valiz.");
        var queue = await (await driver.Http.GetAsync($"/api/trips/{trip.Id}/passengers")).ReadAsync<TripPassengersDto>();
        var byPassenger = await passenger.PostAsync($"/api/bookings/{asked.Id}/accept");
        var byStranger = await stranger.Http.GetAsync($"/api/bookings/{asked.Id}");
        var accepted = await (await driver.PostAsync($"/api/bookings/{asked.Id}/accept")).ReadAsync<BookingDto>();
        var seen = await (await passenger.Http.GetAsync($"/api/bookings/{asked.Id}")).ReadAsync<BookingDto>();
        var tripSeen = await (await passenger.Http.GetAsync($"/api/trips/{trip.Id}")).ReadAsync<TripDto>();
        var mine = await (await passenger.Http.GetAsync("/api/me/bookings")).ReadAsync<List<BookingSummaryDto>>();

        Assert.Equal(("Requested", "Havza", "AŞTİ", (string?)null), (asked.Status, asked.Pickup.NearStop, asked.Dropoff.NearStop, asked.Car.Plate));
        Assert.Equal(asked.PricePerSeat * 2, asked.Total);
        Assert.InRange(asked.PricePerSeat, 315m, 330m);
        Assert.Equal(("Ayşe Kaya", 2, "İki kişiyiz, bir valiz."), (queue.Passengers.Single().Passenger.Name, queue.Passengers.Single().Seats, queue.Passengers.Single().Message));
        Assert.Equal((HttpStatusCode.Forbidden, HttpStatusCode.NotFound), (byPassenger.StatusCode, byStranger.StatusCode));
        Assert.Equal("Confirmed", accepted.Status);
        Assert.Equal("55 ABC 123", seen.Car.Plate);
        Assert.Equal((asked.Id, "55 ABC 123", 1), (tripSeen.MyBookingId, tripSeen.Car.Plate, tripSeen.SeatsLeft));
        Assert.Contains(mine, b => b.Id == asked.Id && b.Status == "Confirmed");
    }

    [Fact]
    public async Task A_seat_left_in_Havza_carries_someone_else_from_Havza_on()
    {
        var (driver, trip) = await OfferAsync(seats: 2, instant: true);
        using var _ = driver;
        using var a = await factory.RegisterAsync();
        using var b = await factory.RegisterAsync();
        using var c = await factory.RegisterAsync();

        var first = await a.BookAsync(trip.Id, Samsun, Havza, seats: 2);
        var second = await b.BookAsync(trip.Id, Havza, Ankara, seats: 2);
        var across = await c.RequestAsync(trip.Id, Samsun, Corum);
        var legs = (await (await driver.Http.GetAsync($"/api/trips/{trip.Id}/passengers")).ReadAsync<TripPassengersDto>()).Legs;
        using var anonymous = factory.CreateClient();
        var date = $"&date={factory.Day(2):yyyy-MM-dd}";
        var full = await (await anonymous.GetAsync(Search(Samsun, Havza, date))).ReadAsync<List<TripSummaryDto>>();

        Assert.Equal(("Confirmed", "Confirmed"), (first.Status, second.Status));
        Assert.Equal((HttpStatusCode.Conflict, "seats_taken"), (across.StatusCode, await across.ProblemCodeAsync()));
        Assert.Equal([2, 2, 2], legs.Select(l => l.Taken));
        Assert.DoesNotContain(full, t => t.Id == trip.Id);
    }

    [Fact]
    public async Task Twenty_people_taking_the_last_seat_at_once_leaves_exactly_one_in_it()
    {
        var (driver, trip) = await OfferAsync(seats: 1, instant: true);
        using var _ = driver;
        var people = new List<Person>();
        for (var i = 0; i < 20; i++)
        {
            people.Add(await factory.RegisterAsync($"Yolcu {i}"));
        }

        // Every stretch crosses the leg from Havza to Çorum, so any two of them share a seat somewhere.
        (double, double)[] starts = [Samsun, Havza, (41.10, 36.0)];
        (double, double)[] ends = [Corum, Ankara, (40.3, 34.2)];
        var responses = await Task.WhenAll(people.Select((p, i) => p.RequestAsync(trip.Id, starts[i % 3], ends[i / 3 % 3])));
        people.ForEach(p => p.Dispose());

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.Created), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        Assert.Equal(1L, await factory.ScalarAsync<long>("SELECT count(*) FROM bookings WHERE trip_id = @p0 AND status = 'Confirmed'", trip.Id));
    }

    [Fact]
    public async Task The_database_refuses_seats_beyond_the_car_whoever_writes_them()
    {
        var (driver, trip) = await OfferAsync(seats: 2, instant: true);
        using var _ = driver;
        using var a = await factory.RegisterAsync();
        await a.BookAsync(trip.Id, Samsun, Ankara, seats: 2);

        var insert = await Assert.ThrowsAnyAsync<Npgsql.PostgresException>(() => factory.ScalarAsync<object>(
            "INSERT INTO bookings (id, trip_id, driver_id, passenger_id, seats, from_meters, to_meters, pickup, dropoff, pickup_near, dropoff_near, pickup_at, dropoff_at, " +
            "price_per_seat_minor, message, status, reason, late_cancellation, requested_at, respond_by) " +
            "SELECT gen_random_uuid(), id, driver_id, driver_id, 1, 1000, 5000, ST_StartPoint(path::geometry)::geography, ST_StartPoint(path::geometry)::geography, 'x', 'y', " +
            "departure_at, departure_at, 1000, '', 'Confirmed', '', false, now(), now() FROM trips WHERE id = @p0", trip.Id));
        var shrink = await Assert.ThrowsAnyAsync<Npgsql.PostgresException>(() => factory.ScalarAsync<object>("UPDATE trips SET seats = 1 WHERE id = @p0", trip.Id));

        Assert.Equal("ck_bookings_seats_free", insert.ConstraintName);
        Assert.Equal("ck_trips_seats_cover_bookings", shrink.ConstraintName);
    }

    [Fact]
    public async Task Accepting_when_the_seats_have_gone_is_refused_and_the_request_waits()
    {
        var (driver, trip) = await OfferAsync(seats: 1);
        using var _ = driver;
        using var a = await factory.RegisterAsync();
        using var b = await factory.RegisterAsync();
        var first = await a.BookAsync(trip.Id, Samsun, Ankara);
        var second = await b.BookAsync(trip.Id, Havza, Corum);

        (await driver.PostAsync($"/api/bookings/{first.Id}/accept")).EnsureSuccessStatusCode();
        var refused = await driver.PostAsync($"/api/bookings/{second.Id}/accept");
        var fewer = await driver.Http.PutAsJsonAsync($"/api/trips/{trip.Id}", new { departureAt = trip.DepartureAt, seats = 1, pricePerSeat = 400m, notes = "Klima var." });
        var later = await driver.Http.PutAsJsonAsync($"/api/trips/{trip.Id}", new { departureAt = trip.DepartureAt.AddHours(1), seats = 1, pricePerSeat = 400m });
        var still = await (await b.Http.GetAsync($"/api/bookings/{second.Id}")).ReadAsync<BookingDto>();

        Assert.Equal((HttpStatusCode.Conflict, "seats_taken"), (refused.StatusCode, await refused.ProblemCodeAsync()));
        Assert.Equal("Requested", still.Status);
        Assert.Equal(HttpStatusCode.OK, fewer.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, later.StatusCode);
    }

    [Fact]
    public async Task Calling_off_late_is_recorded_and_cancelling_the_trip_cancels_every_seat()
    {
        var (driver, trip) = await OfferAsync(instant: true, departure: factory.Clock.GetUtcNow().AddHours(3));
        using var _ = driver;
        using var early = await factory.RegisterAsync();
        using var late = await factory.RegisterAsync();
        using var waiting = await factory.RegisterAsync();
        var lateSeat = await late.BookAsync(trip.Id, Havza, Ankara);
        var keptSeat = await early.BookAsync(trip.Id, Samsun, Ankara);

        // The car reaches Havza in about four hours, so calling off now is late.
        var cancelled = await (await late.PostAsync($"/api/bookings/{lateSeat.Id}/cancel", new { reason = "Planım değişti" })).ReadAsync<BookingDto>();
        var again = await late.BookAsync(trip.Id, Havza, Ankara);
        var twice = await late.RequestAsync(trip.Id, Havza, Corum);
        var own = await driver.RequestAsync(trip.Id, Havza, Corum);
        (await driver.PostAsync($"/api/trips/{trip.Id}/cancel")).EnsureSuccessStatusCode();
        var afterTrip = await (await early.Http.GetAsync($"/api/bookings/{keptSeat.Id}")).ReadAsync<BookingDto>();
        var none = await waiting.RequestAsync(trip.Id, Havza, Corum);

        Assert.Equal(("Cancelled", "Passenger", true, "Planım değişti"), (cancelled.Status, cancelled.CancelledBy, cancelled.LateCancellation, cancelled.Reason));
        Assert.Equal("Confirmed", again.Status);
        Assert.Equal(HttpStatusCode.Conflict, twice.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, own.StatusCode);
        Assert.Equal(("Cancelled", "Driver", "The driver cancelled the trip."), (afterTrip.Status, afterTrip.CancelledBy, afterTrip.Reason));
        Assert.Equal(HttpStatusCode.Conflict, none.StatusCode);
    }

    [Fact]
    public async Task Requests_nobody_answers_lapse()
    {
        var (driver, trip) = await OfferAsync(day: 3);
        using var _ = driver;
        using var passenger = await factory.RegisterAsync();
        var asked = await passenger.BookAsync(trip.Id, Samsun, Corum);

        factory.Clock.Advance(TimeSpan.FromHours(13));
        var pass = await factory.SweepAsync();

        Assert.True(pass.Expired >= 1);
        Assert.Equal("Expired", await factory.ScalarAsync<string>("SELECT status FROM bookings WHERE id = @p0", asked.Id));
    }

    [Fact]
    public async Task Bookings_are_checked()
    {
        var (driver, trip) = await OfferAsync();
        using var _ = driver;
        using var passenger = await factory.RegisterAsync();

        var backwards = await passenger.RequestAsync(trip.Id, Ankara, Samsun);
        var far = await passenger.RequestAsync(trip.Id, (38.4237, 27.1428), Ankara);
        var tooMany = await passenger.RequestAsync(trip.Id, Samsun, Ankara, seats: 4);
        var unknown = await passenger.RequestAsync(Guid.NewGuid(), Samsun, Ankara);

        Assert.Equal((HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.BadRequest), (backwards.StatusCode, far.StatusCode, tooMany.StatusCode));
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }
}
