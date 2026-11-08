using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Yoldas.IntegrationTests.Support;
using static Yoldas.IntegrationTests.Support.TestApi;

namespace Yoldas.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class TripDayTests(ApiFactory factory)
{
    /// <summary>A trip leaving in 40 minutes that the driver can start in a quarter of an hour, with a passenger from Havza.</summary>
    private async Task<(Person Driver, Person Passenger, TripDto Trip, BookingDto Booking)> TodayAsync(bool instant = true)
    {
        var driver = await factory.RegisterAsync("Mehmet Usta");
        await driver.DriveAsync();
        var trip = await driver.OfferAsync(Trip(factory.Clock.GetUtcNow().AddMinutes(40), instantBook: instant));
        var passenger = await factory.RegisterAsync("Ayşe Kaya");
        var booking = await passenger.BookAsync(trip.Id, Havza, Ankara);
        factory.Clock.Advance(TimeSpan.FromMinutes(15));
        return (driver, passenger, trip, booking);
    }

    [Fact]
    public async Task A_trip_day_from_the_start_to_the_reviews()
    {
        var (driver, passenger, trip, booking) = await TodayAsync();
        using var _ = driver;
        using var __ = passenger;
        var (live, joined) = await LiveClient.FollowAsync(factory, passenger, trip.Id);
        await using var ___ = live;

        var early = await driver.PostAsync($"/api/bookings/{booking.Id}/board", new { code = booking.BoardingCode });
        var started = await (await driver.PostAsync($"/api/trips/{trip.Id}/start")).ReadAsync<LiveViewDto>();
        await live.WaitForAsync("TripChanged", v => v.Str("status") == "Started");
        var moved = await (await driver.Http.PostAsJsonAsync($"/api/trips/{trip.Id}/position", new { latitude = 41.115, longitude = 35.973 })).ReadAsync<LiveViewDto>();
        var heard = await live.WaitForAsync("TripChanged", v => v.TryGetProperty("position", out var p) && !p.IsNull());
        var view = await (await passenger.Http.GetAsync($"/api/trips/{trip.Id}/live")).ReadAsync<LiveViewDto>();
        var wrong = await driver.PostAsync($"/api/bookings/{booking.Id}/board", new { code = booking.BoardingCode == "0000" ? "1111" : "0000" });
        var boarded = await (await driver.PostAsync($"/api/bookings/{booking.Id}/board", new { code = booking.BoardingCode })).ReadAsync<BookingDto>();
        var finished = await (await driver.PostAsync($"/api/trips/{trip.Id}/finish")).ReadAsync<LiveViewDto>();
        await live.WaitForAsync("TripChanged", v => v.Str("status") == "Finished");
        var done = await (await passenger.Http.GetAsync($"/api/bookings/{booking.Id}")).ReadAsync<BookingDto>();
        var ofDriver = await passenger.PostAsync($"/api/bookings/{booking.Id}/review", new { stars = 5, comment = "Çok rahat bir yolculuktu." });
        var ofPassenger = await driver.PostAsync($"/api/bookings/{booking.Id}/review", new { stars = 4 });
        var twice = await passenger.PostAsync($"/api/bookings/{booking.Id}/review", new { stars = 1 });
        using var anonymous = factory.CreateClient();
        var driverProfile = await (await anonymous.GetAsync($"/api/users/{driver.Id}")).ReadAsync<ProfileDto>();
        var passengerProfile = await (await anonymous.GetAsync($"/api/users/{passenger.Id}")).ReadAsync<ProfileDto>();
        var tripNow = await (await anonymous.GetAsync($"/api/trips/{trip.Id}")).ReadAsync<TripDto>();

        Assert.Equal(("Scheduled", (LivePositionDto?)null), (joined.Status, joined.Position));
        Assert.Matches("^[0-9]{4}$", booking.BoardingCode);
        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);
        Assert.Equal("Started", started.Status);
        Assert.InRange(moved.Position!.KmAlong, 28, 33);
        Assert.Equal(moved.Position.KmAlong, heard.GetProperty("position").GetProperty("kmAlong").GetDouble());
        Assert.Null(view.Stops[0].At);
        Assert.True(view.Stops[1].At > factory.Clock.GetUtcNow());
        Assert.Equal((HttpStatusCode.UnprocessableEntity, "wrong_code"), (wrong.StatusCode, await wrong.ProblemCodeAsync()));
        Assert.Equal("Boarded", boarded.Status);
        Assert.Equal(("Finished", (LivePositionDto?)null), (finished.Status, finished.Position));
        Assert.Equal(("Completed", true), (done.Status, done.Reviews.CanReview));
        Assert.Equal((HttpStatusCode.Created, HttpStatusCode.Created), (ofDriver.StatusCode, ofPassenger.StatusCode));
        Assert.Equal((HttpStatusCode.Conflict, "already_reviewed"), (twice.StatusCode, await twice.ProblemCodeAsync()));
        Assert.Equal(((double?)5.0, 1, 1), (driverProfile.AsDriver.Rating, driverProfile.AsDriver.Reviews, driverProfile.AsDriver.TripsDriven));
        Assert.Equal("Çok rahat bir yolculuktu.", driverProfile.Reviews.Single().Comment);
        Assert.Equal(((double?)4.0, 1), (passengerProfile.AsPassenger.Rating, passengerProfile.AsPassenger.Rides));
        Assert.Equal(((double?)5.0, 1), (tripNow.Driver.Rating, tripNow.Driver.RatingCount));
    }

    [Fact]
    public async Task Only_the_driver_and_confirmed_passengers_follow_a_trip()
    {
        var (driver, passenger, trip, _) = await TodayAsync(instant: false);
        using var _ = driver;
        using var __ = passenger;
        using var stranger = await factory.RegisterAsync();

        var asking = await passenger.Http.GetAsync($"/api/trips/{trip.Id}/live");
        var strangerLive = await stranger.Http.GetAsync($"/api/trips/{trip.Id}/live");
        var strangerHub = LiveClient.Build(factory, "/hubs/trips", stranger.Token);
        await strangerHub.StartAsync();
        var refused = await Assert.ThrowsAsync<HubException>(() => strangerHub.InvokeAsync<LiveViewDto>("Follow", trip.Id));
        await strangerHub.DisposeAsync();
        var notStarted = await driver.Http.PostAsJsonAsync($"/api/trips/{trip.Id}/position", new { latitude = 41.1, longitude = 36.0 });
        var byPassenger = await passenger.PostAsync($"/api/trips/{trip.Id}/start");
        var driverView = await driver.Http.GetAsync($"/api/trips/{trip.Id}/live");

        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound), (asking.StatusCode, strangerLive.StatusCode));
        Assert.Contains("may follow", refused.Message);
        Assert.Equal(HttpStatusCode.Conflict, notStarted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, byPassenger.StatusCode);
        Assert.Equal(HttpStatusCode.OK, driverView.StatusCode);
    }

    [Fact]
    public async Task A_passenger_who_does_not_come_is_marked_once_the_car_has_passed()
    {
        var (driver, passenger, trip, booking) = await TodayAsync();
        using var _ = driver;
        using var __ = passenger;
        (await driver.PostAsync($"/api/trips/{trip.Id}/start")).EnsureSuccessStatusCode();

        var tooSoon = await driver.PostAsync($"/api/bookings/{booking.Id}/no-show");
        var notYet = await (await driver.Http.GetAsync($"/api/bookings/{booking.Id}")).ReadAsync<BookingDto>();
        (await driver.Http.PostAsJsonAsync($"/api/trips/{trip.Id}/position", new { latitude = 40.80, longitude = 35.40 })).EnsureSuccessStatusCode();
        var marked = await (await driver.PostAsync($"/api/bookings/{booking.Id}/no-show")).ReadAsync<BookingDto>();
        var profile = await (await driver.Http.GetAsync($"/api/users/{passenger.Id}")).ReadAsync<ProfileDto>();

        Assert.Equal(HttpStatusCode.Conflict, tooSoon.StatusCode);
        Assert.Equal((true, false), (notYet.CanBoard, notYet.CanMarkNoShow));
        Assert.Equal("NoShow", marked.Status);
        Assert.Equal(1, profile.AsPassenger.NoShows);
    }

    [Fact]
    public async Task The_clock_cancels_trips_that_never_started_as_the_drivers_and_finishes_forgotten_ones()
    {
        var ghost = await factory.RegisterAsync("Gelmeyen Sürücü");
        await ghost.DriveAsync();
        var never = await ghost.OfferAsync(Trip(factory.At(1, 8)));
        var forgotten = await ghost.OfferAsync(Trip(factory.At(1, 20)));
        using var passenger = await factory.RegisterAsync();
        var seat = await passenger.BookAsync(never.Id, Havza, Ankara);
        (await ghost.PostAsync($"/api/bookings/{seat.Id}/accept")).EnsureSuccessStatusCode();
        ghost.Dispose();

        // To 19:45 tomorrow: the morning trip should have started hours ago; the evening one is started.
        factory.Clock.SetUtcNow(factory.At(1, 19, 45));
        var driver = await factory.SignInAsync(ghost.Email);
        (await driver.PostAsync($"/api/trips/{forgotten.Id}/start")).EnsureSuccessStatusCode();
        await factory.SweepAsync();
        factory.Clock.SetUtcNow(factory.At(3, 12));
        await factory.SweepAsync();
        using var anonymous = factory.CreateClient();
        var profile = await (await anonymous.GetAsync($"/api/users/{ghost.Id}")).ReadAsync<ProfileDto>();

        Assert.Equal("Cancelled", await factory.ScalarAsync<string>("SELECT status FROM trips WHERE id = @p0", never.Id));
        Assert.Equal("Finished", await factory.ScalarAsync<string>("SELECT status FROM trips WHERE id = @p0", forgotten.Id));
        Assert.Equal(("Cancelled", "Driver", true), (
            await factory.ScalarAsync<string>("SELECT status FROM bookings WHERE id = @p0", seat.Id),
            await factory.ScalarAsync<string>("SELECT cancelled_by FROM bookings WHERE id = @p0", seat.Id),
            await factory.ScalarAsync<bool>("SELECT late_cancellation FROM bookings WHERE id = @p0", seat.Id)));
        Assert.Equal((1, 1), (profile.AsDriver.LateCancellations, profile.AsDriver.TripsDriven));
        driver.Dispose();
    }
}
