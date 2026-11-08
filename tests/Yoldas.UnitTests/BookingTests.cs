using Yoldas.Core;
using Yoldas.Core.Bookings;
using Yoldas.Core.Geo;
using static Yoldas.UnitTests.Builders;

namespace Yoldas.UnitTests;

public sealed class SeatTests
{
    [Fact]
    public void A_seat_is_free_again_where_its_passenger_gets_out()
    {
        SeatSpan[] taken = [new(0, 100, 1), new(100, 300, 1), new(50, 150, 2)];

        Assert.Equal(3, Seats.MaxTaken(taken, 0, 300));
        Assert.Equal(1, Seats.MaxTaken(taken, 0, 50));
        Assert.Equal(1, Seats.MaxTaken(taken, 150, 300));
        Assert.Equal(3, Seats.MaxTaken(taken, 120, 130));
        Assert.Equal(0, Seats.MaxTaken(taken, 300, 400));
        Assert.Equal(1, Seats.Free(4, taken, 0, 300));
        Assert.Equal(0, Seats.Free(2, taken, 0, 300));
    }

    [Fact]
    public void Nothing_taken_means_every_seat_is_free() => Assert.Equal(3, Seats.Free(3, [], 0, 1000));
}

public sealed class BookingTests
{
    private static readonly Guid Passenger = Guid.NewGuid();

    private static Booking Book(int seats = 1, bool instant = false, DateTimeOffset? now = null, int fromStop = 1, int toStop = 2)
    {
        var trip = SamsunToAnkara(instant: instant);
        var at = (int i) => new RoutePosition(trip.Stops[i].Location.Y, trip.Stops[i].Location.X, trip.Stops[i].MetersAlong, 0);
        return Booking.Request(trip, Passenger, seats, at(fromStop), at(toStop), " Bir valiz ", now ?? Now);
    }

    [Fact]
    public void A_request_takes_its_part_of_the_route_and_its_share_of_the_price()
    {
        var trip = SamsunToAnkara();
        var booking = Book(seats: 2);

        Assert.Equal((BookingStatus.Requested, "Havza", "Çorum", "Bir valiz"), (booking.Status, booking.PickupNear, booking.DropoffNear, booking.Message));
        Assert.Equal((trip.Stops[1].MetersAlong, trip.Stops[2].MetersAlong), (booking.FromMeters, booking.ToMeters));
        Assert.Equal(trip.PriceFor(booking.FromMeters, booking.ToMeters), booking.PricePerSeatMinor);
        Assert.Equal(booking.PricePerSeatMinor * 2, booking.TotalMinor);
        Assert.Equal(trip.PassesAt(booking.FromMeters), booking.PickupAt);
        Assert.Equal(Now + Booking.AnswerWithin, booking.RespondBy);
        Assert.False(booking.Holds);
    }

    [Fact]
    public void Instant_trips_confirm_at_once()
    {
        var booking = Book(instant: true);

        Assert.Equal((BookingStatus.Confirmed, (DateTimeOffset?)Now), (booking.Status, booking.ConfirmedAt));
        Assert.True(booking.Holds);
    }

    [Fact]
    public void Close_to_the_pickup_the_driver_must_answer_half_an_hour_before()
    {
        // The trip leaves at 05:00 UTC tomorrow; asked at 20:00 today, the twelve hours would run past the pickup.
        var lateEvening = new DateTimeOffset(2026, 11, 7, 20, 0, 0, TimeSpan.Zero);
        var booking = Book(now: lateEvening, fromStop: 0);

        Assert.Equal(booking.PickupAt - Booking.BookAhead, booking.RespondBy);
        Assert.Throws<DomainException>(() => Book(now: booking.PickupAt - TimeSpan.FromMinutes(20), fromStop: 0));
    }

    [Fact]
    public void Requests_are_checked()
    {
        var trip = SamsunToAnkara();
        var stop = (int i) => new RoutePosition(trip.Stops[i].Location.Y, trip.Stops[i].Location.X, trip.Stops[i].MetersAlong, 0);

        Assert.Equal(ErrorCodes.ValidationFailed, Assert.Throws<DomainException>(() => Booking.Request(trip, Passenger, 1, stop(2), stop(1), null, Now)).Code);
        Assert.Equal(ErrorCodes.ValidationFailed, Assert.Throws<DomainException>(() => Booking.Request(trip, Passenger, 4, stop(0), stop(3), null, Now)).Code);
        Assert.Equal(ErrorCodes.InvalidState, Assert.Throws<DomainException>(() => Booking.Request(trip, Driver, 1, stop(0), stop(3), null, Now)).Code);
        trip.Cancel(Now);
        Assert.Equal(ErrorCodes.InvalidState, Assert.Throws<DomainException>(() => Booking.Request(trip, Passenger, 1, stop(0), stop(3), null, Now)).Code);
    }

    [Fact]
    public void Only_the_driver_answers_and_only_in_time()
    {
        var accepted = Book();
        var late = Book();
        var declined = Book();

        Assert.Equal(ErrorCodes.Forbidden, Assert.Throws<DomainException>(() => accepted.Accept(Passenger, Now)).Code);
        Assert.Equal(ErrorCodes.NotFound, Assert.Throws<DomainException>(() => accepted.Accept(Guid.NewGuid(), Now)).Code);
        accepted.Accept(Driver, Now.AddHours(1));
        Assert.Throws<DomainException>(() => late.Accept(Driver, late.RespondBy));
        declined.Decline(Driver, "Bagaj dolu", Now);
        late.Expire(Now.AddHours(13));

        Assert.Equal(BookingStatus.Confirmed, accepted.Status);
        Assert.Equal((BookingStatus.Declined, "Bagaj dolu"), (declined.Status, declined.Reason));
        Assert.Equal(BookingStatus.Expired, late.Status);
        Assert.Throws<DomainException>(() => declined.Accept(Driver, Now));
    }

    [Fact]
    public void Calling_off_a_confirmed_seat_within_a_day_is_late_for_whoever_does_it()
    {
        var early = Book(instant: true);
        var byPassenger = Book(instant: true);
        var byDriver = Book(instant: true);
        var waiting = Book();
        var lateHour = byPassenger.PickupAt - TimeSpan.FromHours(3);

        early.Cancel(Passenger, null, byPassenger.PickupAt - TimeSpan.FromHours(30));
        byPassenger.Cancel(Passenger, "Planım değişti", lateHour);
        byDriver.Cancel(Driver, null, lateHour);
        waiting.Cancel(Passenger, null, lateHour);

        Assert.Equal((CancelledBy.Passenger, false), (early.CancelledBy!.Value, early.LateCancellation));
        Assert.Equal((CancelledBy.Passenger, true), (byPassenger.CancelledBy!.Value, byPassenger.LateCancellation));
        Assert.Equal((CancelledBy.Driver, true), (byDriver.CancelledBy!.Value, byDriver.LateCancellation));
        Assert.False(waiting.LateCancellation);
        Assert.Throws<DomainException>(() => Book().Cancel(Passenger, null, early.PickupAt));
        Assert.Throws<DomainException>(() => early.Cancel(Passenger, null, Now));
    }
}
