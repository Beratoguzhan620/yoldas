using System.Net;
using System.Net.Http.Json;
using Yoldas.IntegrationTests.Support;
using static Yoldas.IntegrationTests.Support.TestApi;

namespace Yoldas.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class TripTests(ApiFactory factory)
{
    [Fact]
    public async Task A_driver_adds_a_car_before_offering_seats()
    {
        using var driver = await factory.RegisterAsync("Mehmet Usta");

        var noCar = await driver.Http.PostAsJsonAsync("/api/trips", Trip(factory.At(1, 8)));
        var badPlate = await driver.Http.PutAsJsonAsync("/api/me/vehicle", new { make = "Renault", model = "Clio", colour = "Beyaz", plate = "ABC", passengerSeats = 4 });
        var none = await driver.Http.GetAsync("/api/me/vehicle");
        await driver.DriveAsync();
        var car = await (await driver.Http.GetAsync("/api/me/vehicle")).ReadAsync<VehicleDto>();
        var trip = await driver.OfferAsync(Trip(factory.At(1, 8)));
        var me = await (await driver.Http.GetAsync("/api/me")).ReadAsync<MeDto>();

        Assert.Equal((HttpStatusCode.Conflict, "no_vehicle"), (noCar.StatusCode, await noCar.ProblemCodeAsync()));
        Assert.Equal(HttpStatusCode.BadRequest, badPlate.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, none.StatusCode);
        Assert.Equal(("55 ABC 123", 4), (car.Plate, car.PassengerSeats));
        Assert.Equal("Scheduled", trip.Status);
        Assert.Equal((true, 1), (me.HasVehicle, me.UpcomingTrips));
    }

    [Fact]
    public async Task An_offered_trip_shows_its_stops_times_and_price_limits_and_the_plate_only_to_its_driver()
    {
        using var driver = await factory.RegisterAsync("Mehmet Usta");
        await driver.DriveAsync();
        using var anonymous = factory.CreateClient();

        var trip = await driver.OfferAsync(Trip(factory.At(1, 8), notes: "Bagajda bir valizlik yer var."));
        var seen = await (await anonymous.GetAsync($"/api/trips/{trip.Id}")).ReadAsync<TripDto>();

        Assert.Equal(["Samsun Otogar", "Havza", "Çorum", "AŞTİ"], trip.Stops.Select(s => s.Name));
        Assert.InRange(trip.LengthKm, 320, 345);
        Assert.InRange(trip.DurationMinutes, 295, 320);
        Assert.Equal(factory.At(1, 8), trip.DepartureAt);
        Assert.Equal(trip.DepartureAt.AddMinutes(trip.DurationMinutes), trip.ArrivalAt);
        Assert.True(trip.Stops.Zip(trip.Stops.Skip(1)).All(p => p.First.At < p.Second.At && p.First.KmAlong < p.Second.KmAlong));
        Assert.Equal(Math.Floor((decimal)trip.LengthKm * 3m), trip.MaxPricePerSeat, 0);
        Assert.Equal((true, "55 ABC 123", 3, 3), (trip.IsMine, trip.Car.Plate, trip.Seats, trip.SeatsLeft));
        Assert.Equal((false, (string?)null, "Bagajda bir valizlik yer var."), (seen.IsMine, seen.Car.Plate, seen.Notes));
    }

    [Fact]
    public async Task Offers_are_checked()
    {
        using var driver = await factory.RegisterAsync();
        await driver.DriveAsync(seats: 2);

        var tooDear = await driver.Http.PostAsJsonAsync("/api/trips", Trip(factory.At(1, 8), seats: 2, price: 1500m));
        var tooManySeats = await driver.Http.PostAsJsonAsync("/api/trips", Trip(factory.At(1, 8), seats: 3));
        var tooSoon = await driver.Http.PostAsJsonAsync("/api/trips", Trip(factory.Clock.GetUtcNow().AddMinutes(10), seats: 2));
        var oneStop = await driver.Http.PostAsJsonAsync("/api/trips", Trip(factory.At(1, 8), stops: [SamsunToAnkara[0]], seats: 2));
        var noStops = await driver.Http.PostAsJsonAsync("/api/trips", new { departureAt = factory.At(1, 8), seats = 2, pricePerSeat = 100 });

        Assert.All([tooDear, tooManySeats, tooSoon, oneStop, noStops], r => Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode));
        Assert.Contains("not to make a profit", await tooDear.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Search_finds_trips_passing_near_both_places_in_order_and_prices_the_part_you_ride()
    {
        using var driver = await factory.RegisterAsync();
        await driver.DriveAsync();
        var day = 3;
        var trip = await driver.OfferAsync(Trip(factory.At(day, 8)));
        var date = $"&date={factory.Day(day):yyyy-MM-dd}";
        using var anonymous = factory.CreateClient();

        async Task<TripSummaryDto?> Find((double, double) from, (double, double) to, string extra = "")
        {
            var found = await (await anonymous.GetAsync(Search(from, to, date + extra))).ReadAsync<List<TripSummaryDto>>();
            return found.SingleOrDefault(t => t.Id == trip.Id);
        }

        var whole = await Find(Samsun, Ankara);
        var middle = await Find(Havza, Corum);
        var back = await Find(Ankara, Samsun);
        var izmir = await Find(Samsun, (38.4237, 27.1428));
        var otherDay = await (await anonymous.GetAsync(Search(Samsun, Ankara, $"&date={factory.Day(day + 1):yyyy-MM-dd}"))).ReadAsync<List<TripSummaryDto>>();
        var tooManySeats = await Find(Samsun, Ankara, "&seats=4");
        var detail = await (await anonymous.GetAsync(FormattableString.Invariant($"/api/trips/{trip.Id}?fromLat={Havza.Lat}&fromLng={Havza.Lng}&toLat={Corum.Lat}&toLng={Corum.Lng}"))).ReadAsync<TripDto>();

        Assert.NotNull(whole);
        Assert.Equal(("Samsun Otogar", "AŞTİ"), (whole.Match!.Pickup.NearStop, whole.Match.Dropoff.NearStop));
        // Kızılay meets the route a few kilometres before the bus station, so the ride is a little short of the whole way.
        Assert.InRange(whole.Match.Price, 390m, 400m);
        Assert.InRange(whole.Match.Pickup.KmFromYou, 2, 6);
        Assert.NotNull(middle);
        Assert.Equal(("Havza", "Çorum"), (middle.Match!.Pickup.NearStop, middle.Match.Dropoff.NearStop));
        // Havza to Çorum is about 76 of 332 km: 23 percent of 400 TL.
        Assert.InRange(middle.Match.Price, 88m, 95m);
        Assert.Equal(0, middle.Match.Pickup.KmFromYou, 1);
        Assert.True(middle.Match.Pickup.At > trip.DepartureAt && middle.Match.Dropoff.At < trip.ArrivalAt);
        Assert.Null(back);
        Assert.Null(izmir);
        Assert.DoesNotContain(otherDay, t => t.Id == trip.Id);
        Assert.Null(tooManySeats);
        Assert.Equal(middle.Match.Price, detail.Match!.Price);
    }

    [Fact]
    public async Task Results_sort_by_departure_price_or_how_close_they_pass()
    {
        using var driver = await factory.RegisterAsync();
        await driver.DriveAsync();
        var day = 5;
        var early = await driver.OfferAsync(Trip(factory.At(day, 7), price: 450m));
        var cheap = await driver.OfferAsync(Trip(factory.At(day, 9), price: 300m));
        var near = await driver.OfferAsync(Trip(factory.At(day, 11), price: 350m, stops:
        [
            new { name = "Samsun Merkez", latitude = 41.2867, longitude = 36.3300 },
            new { name = "Kızılay", latitude = 39.9208, longitude = 32.8541 },
        ]));
        using var anonymous = factory.CreateClient();
        var mine = new[] { early.Id, cheap.Id, near.Id };

        async Task<List<Guid>> Order(string sort) =>
            [.. (await (await anonymous.GetAsync(Search(Samsun, Ankara, $"&date={factory.Day(day):yyyy-MM-dd}&sort={sort}"))).ReadAsync<List<TripSummaryDto>>())
                .Select(t => t.Id).Where(mine.Contains)];

        Assert.Equal([early.Id, cheap.Id, near.Id], await Order("Departure"));
        Assert.Equal([cheap.Id, near.Id, early.Id], await Order("Price"));
        Assert.Equal(near.Id, (await Order("Closest"))[0]);
    }

    [Fact]
    public async Task Drivers_change_and_cancel_their_own_trips_and_cancelled_trips_leave_search()
    {
        using var driver = await factory.RegisterAsync();
        using var other = await factory.RegisterAsync();
        await driver.DriveAsync();
        var day = 7;
        var trip = await driver.OfferAsync(Trip(factory.At(day, 8)));

        var changed = await (await driver.Http.PutAsJsonAsync($"/api/trips/{trip.Id}", new { departureAt = factory.At(day, 10), seats = 2, pricePerSeat = 380m, instantBook = true }))
            .ReadAsync<TripDto>();
        var stranger = await other.Http.PutAsJsonAsync($"/api/trips/{trip.Id}", new { departureAt = factory.At(day, 10), seats = 2, pricePerSeat = 1m });
        var strangerCancel = await other.PostAsync($"/api/trips/{trip.Id}/cancel");
        var cancelled = await (await driver.PostAsync($"/api/trips/{trip.Id}/cancel")).ReadAsync<TripDto>();
        var again = await driver.PostAsync($"/api/trips/{trip.Id}/cancel");
        using var anonymous = factory.CreateClient();
        var found = await (await anonymous.GetAsync(Search(Samsun, Ankara, $"&date={factory.Day(day):yyyy-MM-dd}"))).ReadAsync<List<TripSummaryDto>>();
        var upcoming = await (await driver.Http.GetAsync("/api/me/trips")).ReadAsync<List<TripSummaryDto>>();
        var past = await (await driver.Http.GetAsync("/api/me/trips?when=past")).ReadAsync<List<TripSummaryDto>>();

        Assert.Equal((factory.At(day, 10), 2, 380m, true), (changed.DepartureAt, changed.Seats, changed.PricePerSeat, changed.InstantBook));
        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound), (stranger.StatusCode, strangerCancel.StatusCode));
        Assert.Equal("Cancelled", cancelled.Status);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.DoesNotContain(found, t => t.Id == trip.Id);
        Assert.DoesNotContain(upcoming, t => t.Id == trip.Id);
        Assert.Contains(past, t => t.Id == trip.Id);
    }

    [Fact]
    public async Task Places_are_found_with_or_without_Turkish_letters_and_search_needs_both_ends()
    {
        using var anonymous = factory.CreateClient();

        var places = await (await anonymous.GetAsync("/api/places?q=canak")).ReadAsync<List<PlaceDto>>();
        var towns = await (await anonymous.GetAsync("/api/places?q=bafra")).ReadAsync<List<PlaceDto>>();
        var noEnd = await anonymous.GetAsync("/api/trips?fromLat=41.28&fromLng=36.33");

        Assert.Equal("Çanakkale", places[0].Name);
        Assert.Equal("Bafra, Samsun", towns[0].Label);
        Assert.Equal(HttpStatusCode.BadRequest, noEnd.StatusCode);
    }
}
