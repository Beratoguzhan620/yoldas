using Yoldas.Core;
using Yoldas.Core.Bookings;
using Yoldas.Core.Geo;
using Yoldas.Core.Reviews;
using Yoldas.Core.Trips;
using static Yoldas.UnitTests.Builders;

namespace Yoldas.UnitTests;

public sealed class TripDayTests
{
    private static readonly Guid Passenger = Guid.NewGuid();

    // The trip leaves at 05:00 UTC on 8 November (08:00 in Turkey).
    private static readonly DateTimeOffset Departure = new(2026, 11, 8, 5, 0, 0, TimeSpan.Zero);

    private static Trip Started(out Booking booking, bool instant = true)
    {
        var trip = SamsunToAnkara(instant: instant);
        RoutePosition At(int i) => new(trip.Stops[i].Location.Y, trip.Stops[i].Location.X, trip.Stops[i].MetersAlong, 0);
        booking = Booking.Request(trip, Passenger, 1, At(1), At(3), null, Now);
        trip.Start(Departure);
        return trip;
    }

    [Fact]
    public void A_trip_starts_from_half_an_hour_before_its_departure_to_three_hours_after()
    {
        Assert.Throws<DomainException>(() => SamsunToAnkara().Start(Departure.AddMinutes(-31)));
        Assert.Throws<DomainException>(() => SamsunToAnkara().Start(Departure.AddHours(3).AddMinutes(1)));
        var trip = SamsunToAnkara();
        trip.Start(Departure.AddMinutes(-20));
        Assert.Equal(TripStatus.Started, trip.Status);
        Assert.Throws<DomainException>(() => trip.Cancel(Departure));
    }

    [Fact]
    public void Positions_land_on_the_route_never_go_back_and_learn_the_speed()
    {
        var trip = Started(out _);
        var havza = trip.Stops[1];

        // Half way to Havza after half an hour: about 61 km/h.
        trip.Report(41.115, 35.973, Departure.AddMinutes(30));
        var first = trip.LiveMetersAlong!.Value;
        trip.Report(40.99, 35.70, Departure.AddMinutes(60));
        var second = trip.LiveMetersAlong!.Value;
        trip.Report(41.11, 35.98, Departure.AddMinutes(61));

        Assert.InRange(first, 28_000, 33_000);
        Assert.InRange(second, havza.MetersAlong - 4_000, havza.MetersAlong);
        Assert.Equal(second, trip.LiveMetersAlong);
        Assert.InRange(trip.LiveKmPerHour!.Value, 55, 66);
        Assert.Throws<DomainException>(() => SamsunToAnkara().Report(41, 36, Departure));
    }

    [Fact]
    public void Times_ahead_follow_the_car_and_a_passed_stop_has_none()
    {
        var trip = SamsunToAnkara();
        var corum = trip.Stops[2].MetersAlong;

        Assert.Equal(trip.PassesAt(corum), trip.Reaches(corum, Now));
        trip.Start(Departure);
        trip.Report(40.99, 35.70, Departure.AddMinutes(90));

        var eta = trip.Reaches(corum, Departure.AddMinutes(90))!.Value;
        Assert.InRange((eta - Departure.AddMinutes(90)).TotalMinutes, 70, 85);
        Assert.Null(trip.Reaches(trip.Stops[0].MetersAlong, Departure.AddMinutes(90)));
    }

    [Fact]
    public void Far_from_the_route_the_car_is_off_it()
    {
        var trip = Started(out _);
        trip.Report(41.5, 35.0, Departure.AddMinutes(10));

        Assert.True(trip.LiveMetersOff > Trip.OffRouteMeters);
    }

    [Fact]
    public void Finishing_forgets_where_the_car_was()
    {
        var trip = Started(out _);
        trip.Report(41.115, 35.973, Departure.AddMinutes(30));
        trip.Finish(Departure.AddHours(5));

        Assert.Equal((TripStatus.Finished, (int?)null, (DateTimeOffset?)null), (trip.Status, trip.LiveMetersAlong, trip.LiveAt));
        Assert.Null(trip.LivePosition);
    }

    [Fact]
    public void The_driver_boards_a_passenger_with_the_code_on_their_screen()
    {
        var trip = SamsunToAnkara(instant: true);
        RoutePosition At(int i) => new(trip.Stops[i].Location.Y, trip.Stops[i].Location.X, trip.Stops[i].MetersAlong, 0);
        var booking = Booking.Request(trip, Passenger, 1, At(1), At(3), null, Now);

        Assert.Matches("^[0-9]{4}$", booking.BoardingCode);
        Assert.Throws<DomainException>(() => booking.Board(Driver, booking.BoardingCode, trip, Departure));
        trip.Start(Departure);
        Assert.Equal(ErrorCodes.Forbidden, Assert.Throws<DomainException>(() => booking.Board(Passenger, booking.BoardingCode, trip, Departure)).Code);
        Assert.Equal(ErrorCodes.WrongCode, Assert.Throws<DomainException>(() => booking.Board(Driver, Other(booking.BoardingCode!), trip, Departure)).Code);
        booking.Board(Driver, booking.BoardingCode, trip, Departure.AddMinutes(60));
        Assert.Equal((BookingStatus.Boarded, 0), (booking.Status, booking.WrongCodes));
    }

    [Fact]
    public void Five_wrong_codes_put_a_new_one_on_the_passengers_screen()
    {
        var trip = Started(out var booking);
        var code = booking.BoardingCode!;
        for (var i = 0; i < Booking.CodeTries; i++)
        {
            Assert.Throws<DomainException>(() => booking.Board(Driver, Other(code), trip, Departure));
        }

        Assert.NotEqual(code, booking.BoardingCode);
        Assert.Throws<DomainException>(() => booking.Board(Driver, code, trip, Departure));
    }

    [Fact]
    public void A_no_show_waits_until_the_car_has_passed_or_a_quarter_of_an_hour()
    {
        var early = Started(out var waiting);
        var passed = Started(out var gone);
        var due = waiting.PickupAt;

        Assert.Throws<DomainException>(() => waiting.NoShow(Driver, early, due.AddMinutes(10)));
        waiting.NoShow(Driver, early, due.AddMinutes(16));
        passed.Report(40.80, 35.40, due.AddMinutes(5));
        gone.NoShow(Driver, passed, due.AddMinutes(5));

        Assert.Equal((BookingStatus.NoShow, BookingStatus.NoShow), (waiting.Status, gone.Status));
    }

    [Fact]
    public void Finishing_completes_who_boarded_and_marks_who_did_not()
    {
        var trip = Started(out var boarded);
        var other = SamsunToAnkara(instant: true);
        RoutePosition At(int i) => new(other.Stops[i].Location.Y, other.Stops[i].Location.X, other.Stops[i].MetersAlong, 0);
        var missing = Booking.Request(other, Guid.NewGuid(), 1, At(0), At(1), null, Now);
        var asking = Booking.Request(SamsunToAnkara(), Guid.NewGuid(), 1, At(0), At(1), null, Now);
        boarded.Board(Driver, boarded.BoardingCode, trip, Departure);

        foreach (var b in new[] { boarded, missing, asking })
        {
            b.TripFinished(Departure.AddHours(5));
        }

        Assert.Equal([BookingStatus.Completed, BookingStatus.NoShow, BookingStatus.Expired], new[] { boarded.Status, missing.Status, asking.Status });
    }

    [Fact]
    public void Reviews_come_after_a_ride_that_happened_once_per_side()
    {
        var trip = Started(out var booking);
        Assert.Throws<DomainException>(() => Review.Write(booking, Passenger, 5, null, Departure));
        booking.Board(Driver, booking.BoardingCode, trip, Departure);
        booking.TripFinished(Departure.AddHours(5));

        var ofDriver = Review.Write(booking, Passenger, 5, " Çok rahat bir yolculuktu ", Departure.AddHours(6));
        var ofPassenger = Review.Write(booking, Driver, 4, null, Departure.AddHours(6));

        Assert.Equal((ReviewOf.Driver, Driver, "Çok rahat bir yolculuktu"), (ofDriver.Of, ofDriver.SubjectId, ofDriver.Comment));
        Assert.Equal((ReviewOf.Passenger, Passenger), (ofPassenger.Of, ofPassenger.SubjectId));
        Assert.Throws<DomainException>(() => Review.Write(booking, Passenger, 0, null, Departure.AddHours(6)));
        Assert.Throws<DomainException>(() => Review.Write(booking, Passenger, 5, null, Departure.AddDays(15)));
        Assert.Throws<DomainException>(() => Review.Write(booking, Guid.NewGuid(), 5, null, Departure.AddHours(6)));
    }

    private static string Other(string code) => code == "0000" ? "1111" : "0000";
}
