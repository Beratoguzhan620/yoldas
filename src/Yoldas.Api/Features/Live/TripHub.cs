using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Yoldas.Api.Common;
using Yoldas.Api.Features.Trips;
using Yoldas.Core;
using Yoldas.Core.Bookings;
using Yoldas.Core.Trips;
using Yoldas.Infrastructure.Persistence;

namespace Yoldas.Api.Features.Live;

/// <summary>Where the car is, for the people in it or waiting for it.</summary>
public sealed record LivePosition(double Latitude, double Longitude, double KmAlong, double KmOffRoute, bool OffRoute, double KmPerHour, DateTimeOffset At);

/// <summary>When the car should reach a stop: from where it is once the trip is under way; null once it has passed.</summary>
public sealed record StopEta(int Index, string Name, double KmAlong, DateTimeOffset? At);

/// <summary>The trip as it happens. Every message carries the whole of it, so a client never merges pieces.</summary>
public sealed record LiveView(Guid TripId, TripStatus Status, LivePosition? Position, IReadOnlyList<StopEta> Stops);

public interface ITripClient
{
    Task TripChanged(LiveView view);
}

/// <summary>
/// The live feed of a trip. After connecting, a client calls <c>Follow(tripId)</c>: the driver, and passengers with
/// a confirmed seat, are let in and hear every change until the trip ends. Nobody else is told whether the trip exists.
/// </summary>
[Authorize]
public sealed class TripHub(YoldasDbContext db, TimeProvider clock) : Hub<ITripClient>
{
    public const string Path = "/hubs/trips";

    public static string Group(Guid tripId) => $"trip:{tripId}";

    public async Task<LiveView> Follow(Guid tripId)
    {
        var userId = Context.User!.UserId();
        var trip = await db.Trips.AsNoTracking().SingleOrDefaultAsync(t => t.Id == tripId, Context.ConnectionAborted);
        if (trip is null || !await LiveAccess.MayFollowAsync(db, trip, userId, Context.ConnectionAborted))
        {
            throw new HubException("There is no trip with this id that you may follow.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, Group(tripId), Context.ConnectionAborted);
        return LiveAccess.View(trip, clock.GetUtcNow());
    }
}

internal static class LiveAccess
{
    /// <summary>The driver, and passengers whose seat is confirmed or who are on board.</summary>
    public static async Task<bool> MayFollowAsync(YoldasDbContext db, Trip trip, Guid userId, CancellationToken ct) =>
        trip.DriverId == userId
        || await db.Bookings.AnyAsync(
            b => b.TripId == trip.Id && b.PassengerId == userId && (b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.Boarded), ct);

    public static LiveView View(Trip trip, DateTimeOffset now) => new(
        trip.Id, trip.Status,
        trip is { Status: TripStatus.Started, LivePosition: { } p, LiveMetersAlong: { } along, LiveAt: { } at }
            ? new LivePosition(p.Y, p.X, TripQueries.Km(along), TripQueries.Km(trip.LiveMetersOff ?? 0), (trip.LiveMetersOff ?? 0) > Trip.OffRouteMeters,
                trip.LiveKmPerHour ?? Trip.AverageKmPerHour, at)
            : null,
        [.. trip.Stops.OrderBy(s => s.Index).Select(s => new StopEta(s.Index, s.Name, TripQueries.Km(s.MetersAlong), trip.Reaches(s.MetersAlong, now)))]);
}

/// <summary>Tells everyone following a trip what it looks like now.</summary>
public sealed class LivePublisher(IHubContext<TripHub, ITripClient> hub, TimeProvider clock)
{
    public Task PublishAsync(Trip trip) => hub.Clients.Group(TripHub.Group(trip.Id)).TripChanged(LiveAccess.View(trip, clock.GetUtcNow()));
}
