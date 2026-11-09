using Yoldas.Core;
using Yoldas.Core.Trips;
using static Yoldas.UnitTests.Builders;

namespace Yoldas.UnitTests;

public sealed class TripTests
{
    [Fact]
    public void Offering_a_trip_measures_the_route_and_times_each_stop()
    {
        var trip = SamsunToAnkara();

        Assert.Equal(["Samsun Otogar", "Havza", "Çorum", "AŞTİ"], trip.Stops.Select(s => s.Name));
        Assert.InRange(trip.LengthMeters, 320_000, 345_000);
        Assert.Equal(trip.LengthMeters, trip.Stops[^1].MetersAlong);
        Assert.Equal(4, trip.Path.NumPoints);
        // 65 km/h on straight legs: a bit over five hours.
        Assert.InRange(trip.Duration.TotalMinutes, 295, 320);
        Assert.Equal(trip.DepartureAt + trip.Duration, trip.ArrivalAt);
        Assert.True(trip.PassesAt(trip.Stops[1].MetersAlong) > trip.DepartureAt);
    }

    [Fact]
    public void Passengers_pay_their_share_of_the_route_to_the_lira()
    {
        var trip = SamsunToAnkara(price: 400_00);
        var half = trip.LengthMeters / 2;

        Assert.Equal(400_00, trip.PriceFor(0, trip.LengthMeters));
        Assert.Equal(200_00, trip.PriceFor(0, half));
        Assert.Equal(200_00, trip.PriceFor(half, trip.LengthMeters));
        Assert.Equal(10_00, trip.PriceFor(0, 1_000));
    }

    [Fact]
    public void Seats_cost_at_most_three_lira_a_kilometre()
    {
        var trip = SamsunToAnkara();

        Assert.Equal(Trip.MaxPriceFor(trip.LengthMeters), (long)Math.Floor(trip.LengthMeters * 0.3 / 100) * 100);
        var ex = Assert.Throws<DomainException>(() => SamsunToAnkara(price: Trip.MaxPriceFor(trip.LengthMeters) + 100));
        Assert.Contains("not to make a profit", ex.Message);
        Assert.Throws<DomainException>(() => SamsunToAnkara(price: 9_00));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void A_trip_offers_as_many_seats_as_the_car_has(int seats) =>
        Assert.Throws<DomainException>(() => SamsunToAnkara(seats: seats));

    [Fact]
    public void Stops_are_checked()
    {
        Assert.Throws<DomainException>(() => SamsunToAnkara(stops: [Samsun]));
        Assert.Throws<DomainException>(() => SamsunToAnkara(stops: [Samsun, Samsun with { Name = "Yakında", Latitude = 41.2620 }]));
        Assert.Throws<DomainException>(() => SamsunToAnkara(stops: [Samsun, Havza with { Name = " " }]));
        Assert.Throws<DomainException>(() => SamsunToAnkara(stops: [.. Enumerable.Range(0, 9).Select(i => new StopInput($"Durak {i}", 41 - (i * 0.1), 36))]));
    }

    [Fact]
    public void A_trip_leaves_between_half_an_hour_and_ninety_days_from_now()
    {
        var trip = SamsunToAnkara();

        Assert.Throws<DomainException>(() => trip.Change(4, Now.AddMinutes(20), 3, 400_00, false, null, Now));
        Assert.Throws<DomainException>(() => trip.Change(4, Now.AddDays(91), 3, 400_00, false, null, Now));
        trip.Change(4, Now.AddHours(2), 2, 350_00, true, " Bagaj küçük ", Now);
        Assert.Equal((2, 350_00L, true, "Bagaj küçük"), (trip.Seats, trip.PricePerSeatMinor, trip.InstantBook, trip.Notes));
    }

    [Fact]
    public void A_cancelled_trip_stays_cancelled()
    {
        var trip = SamsunToAnkara();
        trip.Cancel(Now);

        Assert.Equal(TripStatus.Cancelled, trip.Status);
        Assert.Throws<DomainException>(() => trip.Cancel(Now));
        Assert.Throws<DomainException>(() => trip.Change(4, Now.AddHours(2), 2, 350_00, true, null, Now));
    }
}
