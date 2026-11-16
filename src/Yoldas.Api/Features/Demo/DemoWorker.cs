using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Yoldas.Api.Features.Live;
using Yoldas.Core.Bookings;
using Yoldas.Core.Trips;
using Yoldas.Infrastructure.Persistence;

namespace Yoldas.Api.Features.Demo;

/// <summary>
/// Keeps the public demo alive: seeds it when the app starts and tops it up every half hour, and drives Mehmet's car
/// from Atakum towards Merzifon, reporting its position the way the driver's phone would. Ayşe waits in Havza; the
/// car picks her up there, drops her in Merzifon, and the trip starts over. Positions go through the same domain
/// method and the same SignalR publisher as a real driver's, so what visitors see is what passengers would see.
/// </summary>
public sealed partial class DemoWorker(IServiceScopeFactory scopes, LivePublisher live, TimeProvider clock, IOptions<DemoOptions> options, ILogger<DemoWorker> logger)
    : BackgroundService
{
    private const string LiveNotes = "Canlı demo: araç rota boyunca kendi ilerliyor. Ayşe Havza'da biniyor.";

    /// <summary>Where each lap starts, in metres along the route: a few minutes before Havza.</summary>
    private const int StartAt = 52_000;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextSeed = DateTimeOffset.MinValue;
        using var timer = new PeriodicTimer(options.Value.Tick);
        do
        {
            try
            {
                if (clock.GetUtcNow() >= nextSeed)
                {
                    await using var scope = scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<DemoService>().SeedAsync(stoppingToken);
                    nextSeed = clock.GetUtcNow().AddMinutes(30);
                }

                await DriveAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(logger, ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>One step of the car on the road: starts a lap when there is none, then moves it along.</summary>
    public async Task DriveAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<YoldasDbContext>();
        var demo = scope.ServiceProvider.GetRequiredService<DemoService>();
        var now = clock.GetUtcNow();
        var mehmet = await db.Users.SingleOrDefaultAsync(u => u.Email == DemoService.Mehmet.Email, ct);
        var ayse = await db.Users.SingleOrDefaultAsync(u => u.Email == DemoService.Ayse.Email, ct);
        if (mehmet is null || ayse is null)
        {
            return;
        }

        var trip = await db.Trips.Where(t => t.DriverId == mehmet.Id && t.Status == TripStatus.Started && t.Notes == LiveNotes).OrderBy(t => t.Id).FirstOrDefaultAsync(ct);
        if (trip is null)
        {
            trip = await StartLapAsync(db, demo, mehmet.Id, ayse.Id, now, ct);
        }

        var next = Math.Min(trip.LengthMeters, (trip.LiveMetersAlong ?? StartAt) + options.Value.MetersPerTick);
        var waiting = await db.Bookings.SingleOrDefaultAsync(b => b.TripId == trip.Id && b.PassengerId == ayse.Id, ct);
        if (waiting is { Status: BookingStatus.Confirmed } && next >= waiting.FromMeters - 200)
        {
            // The driver types in the code on Ayşe's screen as she gets in.
            waiting.Board(mehmet.Id, waiting.BoardingCode, trip, now);
        }

        if (next >= trip.LengthMeters - 300)
        {
            // Arrived: clear this lap away so the next one starts clean and nothing piles up in the demo database.
            trip.Finish(now);
            await live.PublishAsync(trip);
            await db.Bookings.Where(b => b.TripId == trip.Id).ExecuteDeleteAsync(ct);
            await db.Trips.Where(t => t.Id == trip.Id).ExecuteDeleteAsync(ct);
            LogLap(logger);
            return;
        }

        var (latitude, longitude) = trip.Route.At(next);
        trip.Report(latitude, longitude, now);
        await db.SaveChangesAsync(ct);
        await live.PublishAsync(trip);
    }

    private async Task<Trip> StartLapAsync(YoldasDbContext db, DemoService demo, Guid mehmet, Guid ayse, DateTimeOffset now, CancellationToken ct)
    {
        var car = await db.Vehicles.SingleAsync(v => v.OwnerId == mehmet, ct);
        // Left as long ago as it takes to get here at the timetable's speed, so the times on screen agree with the car.
        var leaves = now - Trip.DurationFor(StartAt);
        var offered = leaves.AddHours(-3);
        var trip = Trip.Offer(mehmet, car.Id, car.PassengerSeats, DemoService.LiveRoute, leaves, car.PassengerSeats, 150 * 100, true, LiveNotes, offered);
        db.Trips.Add(trip);
        await db.SaveChangesAsync(ct);

        var emre = await db.Users.SingleAsync(u => u.Email == $"emre@{DemoService.Domain}", ct);
        demo.Book(trip, ayse, DemoService.Havza, DemoService.Merzifon, offered.AddMinutes(20), "Havza otogarının önündeyim.");
        var early = demo.Book(trip, emre.Id, DemoService.Otogar, DemoService.Havza, offered.AddMinutes(40), "");
        await db.SaveChangesAsync(ct);

        trip.Start(now);
        early.Board(mehmet, early.BoardingCode, trip, now);
        var (latitude, longitude) = trip.Route.At(StartAt);
        trip.Report(latitude, longitude, now);
        await db.SaveChangesAsync(ct);
        return trip;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Demo: the car reached Merzifon; starting a new lap")]
    private static partial void LogLap(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Demo: a step failed; trying again at the next tick")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}

/// <summary>The demo accounts, for the sign-in page. Empty unless the demo is on.</summary>
internal static class DemoEndpoints
{
    public static IEndpointRouteBuilder MapDemoEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/demo", (IOptions<DemoOptions> options, IServiceProvider services) =>
                options.Value.Enabled ? services.GetRequiredService<DemoService>().Accounts : (IReadOnlyList<DemoAccount>)[])
            .WithTags("Demo").AllowAnonymous()
            .WithSummary("Demo accounts to sign in with, when this is the public demo");
        return app;
    }
}
