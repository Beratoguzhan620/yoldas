using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Yoldas.Api.Features.Demo;
using Yoldas.Core.Bookings;
using Yoldas.Core.Trips;
using Yoldas.Infrastructure.Persistence;
using Yoldas.IntegrationTests.Support;

namespace Yoldas.IntegrationTests;

/// <summary>Its own database: the demo's people and trips would show up in other tests' searches.</summary>
[CollectionDefinition(Name)]
public sealed class DemoCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "demo";
}

[Collection(DemoCollection.Name)]
public sealed class DemoTests(ApiFactory factory)
{
    private async Task SeedAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DemoService>().SeedAsync(CancellationToken.None);
    }

    private DemoWorker Worker() => ActivatorUtilities.CreateInstance<DemoWorker>(factory.Services);

    private async Task<T> InDbAsync<T>(Func<YoldasDbContext, Task<T>> query)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<YoldasDbContext>());
    }

    [Fact]
    public async Task The_demo_lists_no_accounts_unless_it_is_on()
    {
        using var anonymous = factory.CreateClient();
        var accounts = await anonymous.GetFromJsonAsync<List<object>>("/api/demo");
        Assert.Empty(accounts!);
    }

    [Fact]
    public async Task Seeding_creates_people_who_can_sign_in_with_a_past_and_trips_ahead_and_running_it_again_adds_nothing()
    {
        await SeedAsync();
        var counts = await InDbAsync(async db => (await db.Users.CountAsync(), await db.Trips.CountAsync(), await db.Bookings.CountAsync(), await db.Reviews.CountAsync()));
        await SeedAsync();
        Assert.Equal(counts, await InDbAsync(async db => (await db.Users.CountAsync(), await db.Trips.CountAsync(), await db.Bookings.CountAsync(), await db.Reviews.CountAsync())));

        using var ayse = await factory.SignInAsync(DemoService.Ayse.Email, "yoldas-demo");
        var rides = await ayse.Http.GetFromJsonAsync<List<BookingSummaryDto>>("/api/me/bookings?when=past", TestApi.Json);
        Assert.Contains(rides!, r => r.Status == "Completed");

        using var mehmet = await factory.SignInAsync(DemoService.Mehmet.Email, "yoldas-demo");
        var profile = await mehmet.Http.GetFromJsonAsync<ProfileDto>($"/api/users/{mehmet.Id}", TestApi.Json);
        Assert.Equal(4, profile!.AsDriver.Reviews);
        Assert.Equal(2, profile.AsDriver.TripsDriven);
        var trips = await mehmet.Http.GetFromJsonAsync<List<TripSummaryDto>>("/api/me/trips", TestApi.Json);
        var tomorrow = Assert.Single(trips!, t => t.Status == "Scheduled");
        var passengers = await mehmet.Http.GetFromJsonAsync<TripPassengersDto>($"/api/trips/{tomorrow.Id}/passengers", TestApi.Json);
        Assert.Equal(2, passengers!.Passengers.Count(p => p.Status == "Confirmed"));
        Assert.Single(passengers.Passengers, p => p.Status == "Requested");
    }

    [Fact]
    public async Task The_car_on_the_road_picks_Ayse_up_in_Havza_drops_her_in_Merzifon_and_starts_over()
    {
        await SeedAsync();
        var worker = Worker();
        await worker.DriveAsync(CancellationToken.None);

        var (trip, ayse) = await InDbAsync(async db =>
        {
            var t = await db.Trips.SingleAsync(x => x.Status == TripStatus.Started);
            var a = await db.Users.SingleAsync(u => u.Email == DemoService.Ayse.Email);
            return (t, await db.Bookings.SingleAsync(b => b.TripId == t.Id && b.PassengerId == a.Id));
        });
        Assert.Equal(BookingStatus.Confirmed, ayse.Status);
        Assert.True(trip.LiveMetersAlong < ayse.FromMeters);

        // She can follow it: the live view has the car, and times for the stops ahead.
        using var passenger = await factory.SignInAsync(DemoService.Ayse.Email, "yoldas-demo");
        var view = await passenger.Http.GetFromJsonAsync<LiveViewDto>($"/api/trips/{trip.Id}/live", TestApi.Json);
        Assert.NotNull(view!.Position);
        Assert.Contains(view.Stops, s => s.Name == "Havza" && s.At is not null);

        var laps = 0;
        int? last = trip.LiveMetersAlong;
        var boarded = false;
        for (var step = 0; step < 2_000 && laps == 0; step++)
        {
            factory.Clock.Advance(TimeSpan.FromSeconds(4));
            await worker.DriveAsync(CancellationToken.None);
            var now = await InDbAsync(db => db.Trips.Where(t => t.Id == trip.Id).Select(t => t.LiveMetersAlong).SingleOrDefaultAsync());
            var status = await InDbAsync(db => db.Bookings.Where(b => b.Id == ayse.Id).Select(b => (BookingStatus?)b.Status).SingleOrDefaultAsync());
            boarded |= status == BookingStatus.Boarded;
            if (now is null)
            {
                laps++;
            }
            else
            {
                Assert.True(now > last);
                last = now;
            }
        }

        Assert.True(boarded);
        Assert.Equal(1, laps);
        Assert.False(await InDbAsync(db => db.Bookings.AnyAsync(b => b.TripId == trip.Id)));

        await worker.DriveAsync(CancellationToken.None);
        Assert.Equal(1, await InDbAsync(db => db.Trips.CountAsync(t => t.Status == TripStatus.Started)));
    }
}
