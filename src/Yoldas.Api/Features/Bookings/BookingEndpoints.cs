using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Yoldas.Api.Common;
using Yoldas.Core.Bookings;
using Yoldas.Infrastructure.Persistence;

namespace Yoldas.Api.Features.Bookings;

internal static class BookingEndpoints
{
    public static IEndpointRouteBuilder MapBookingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/bookings").WithTags("Bookings").RequireAuthorization();
        group.MapPost("", Book).WithSummary("Ask for seats on a trip, from where you are to where you are going")
            .WithDescription($"""
                You join the route where it passes closest to your start and leave where it passes closest to your end;
                the price is your share of the route. On an instant trip the seats are yours at once; otherwise the driver
                answers within {Booking.AnswerWithin.TotalHours:0} hours, and at least half an hour before the car reaches you. Seats are
                counted per part of the route, so a seat someone leaves in Havza is free from Havza on.
                """);
        group.MapGet("/{bookingId:guid}", Get).WithSummary("A booking, for its passenger or driver");
        group.MapPost("/{bookingId:guid}/accept", Accept).WithSummary("Accept a request (driver)");
        group.MapPost("/{bookingId:guid}/decline", Decline).WithSummary("Decline a request (driver)");
        group.MapPost("/{bookingId:guid}/cancel", Cancel).WithSummary("Call the seats off, before the pickup")
            .WithDescription($"Calling off a confirmed seat less than {Booking.LateWithin.TotalHours:0} hours before the pickup counts as late, for whoever does it.");

        app.MapGet("/api/me/bookings", Mine).WithTags("Bookings").RequireAuthorization().WithSummary("Seats you have booked: upcoming, or past with when=past");
        app.MapGet("/api/trips/{tripId:guid}/passengers", Passengers).WithTags("Trips").RequireAuthorization()
            .WithSummary("Who is coming on your trip, and how full the car is on each leg");
        return app;
    }

    private static async Task<Created<BookingResponse>> Book(
        BookingRequest request, ClaimsPrincipal user, BookingService bookings, YoldasDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var booking = await bookings.RequestAsync(user.UserId(), request, ct);
        return TypedResults.Created($"/api/bookings/{booking.Id}", await BookingQueries.DetailAsync(db, booking.Id, user.UserId(), clock.GetUtcNow(), ct));
    }

    private static async Task<Ok<BookingResponse>> Get(Guid bookingId, ClaimsPrincipal user, YoldasDbContext db, TimeProvider clock, CancellationToken ct) =>
        TypedResults.Ok(await BookingQueries.DetailAsync(db, bookingId, user.UserId(), clock.GetUtcNow(), ct));

    private static async Task<Ok<BookingResponse>> Accept(Guid bookingId, ClaimsPrincipal user, BookingService bookings, YoldasDbContext db, TimeProvider clock, CancellationToken ct)
    {
        await bookings.AcceptAsync(bookingId, user.UserId(), ct);
        return TypedResults.Ok(await BookingQueries.DetailAsync(db, bookingId, user.UserId(), clock.GetUtcNow(), ct));
    }

    private static async Task<Ok<BookingResponse>> Decline(
        Guid bookingId, ReasonRequest? request, ClaimsPrincipal user, BookingService bookings, YoldasDbContext db, TimeProvider clock, CancellationToken ct)
    {
        await bookings.DeclineAsync(bookingId, user.UserId(), request?.Reason, ct);
        return TypedResults.Ok(await BookingQueries.DetailAsync(db, bookingId, user.UserId(), clock.GetUtcNow(), ct));
    }

    private static async Task<Ok<BookingResponse>> Cancel(
        Guid bookingId, ReasonRequest? request, ClaimsPrincipal user, BookingService bookings, YoldasDbContext db, TimeProvider clock, CancellationToken ct)
    {
        await bookings.CancelAsync(bookingId, user.UserId(), request?.Reason, ct);
        return TypedResults.Ok(await BookingQueries.DetailAsync(db, bookingId, user.UserId(), clock.GetUtcNow(), ct));
    }

    private static async Task<Ok<IReadOnlyList<BookingSummary>>> Mine(string? when, ClaimsPrincipal user, YoldasDbContext db, TimeProvider clock, CancellationToken ct) =>
        TypedResults.Ok(await BookingQueries.MineAsync(db, user.UserId(), string.Equals(when, "past", StringComparison.OrdinalIgnoreCase), clock.GetUtcNow(), ct));

    private static async Task<Ok<TripPassengersResponse>> Passengers(Guid tripId, ClaimsPrincipal user, YoldasDbContext db, CancellationToken ct) =>
        TypedResults.Ok(await BookingQueries.PassengersAsync(db, tripId, user.UserId(), ct));
}
