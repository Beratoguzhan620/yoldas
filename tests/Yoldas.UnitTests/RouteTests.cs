using Yoldas.Core;
using Yoldas.Core.Geo;

namespace Yoldas.UnitTests;

public sealed class RouteTests
{
    private static readonly Route Line = new([GeoPoint.At(41.0, 36.0), GeoPoint.At(41.0, 37.0), GeoPoint.At(40.0, 37.0)]);

    [Fact]
    public void A_route_measures_its_legs_and_where_each_stop_is()
    {
        Assert.InRange(Line.StopPositions[1], 83_500, 84_200);
        Assert.InRange(Line.LengthMeters - Line.StopPositions[1], 111_000, 111_400);
        Assert.Equal(0, Line.StopPositions[0]);
    }

    [Fact]
    public void A_point_beside_a_leg_lands_on_it_at_the_right_distance()
    {
        // Half way along the first leg, about 5.5 km north of it.
        var position = Line.Locate(GeoPoint.At(41.05, 36.5));

        Assert.InRange(position.MetersAlong, 41_500, 42_500);
        Assert.InRange(position.MetersOff, 5_400, 5_700);
        Assert.InRange(position.Latitude, 40.999, 41.001);
    }

    [Fact]
    public void Points_past_either_end_land_on_the_end()
    {
        Assert.Equal(0, Line.Locate(GeoPoint.At(41.0, 35.5)).MetersAlong);
        Assert.Equal(Line.LengthMeters, Line.Locate(GeoPoint.At(39.5, 37.0)).MetersAlong);
    }

    [Fact]
    public void Along_the_second_leg_the_first_counts()
    {
        var position = Line.Locate(GeoPoint.At(40.5, 37.02));

        Assert.InRange(position.MetersAlong - Line.StopPositions[1], 55_000, 56_200);
        Assert.InRange(position.MetersOff, 1_500, 1_800);
    }

    [Fact]
    public void Positions_turn_back_into_coordinates()
    {
        var (lat, lng) = Line.At(Line.StopPositions[1] / 2);

        Assert.Equal(41.0, lat, 3);
        Assert.Equal(36.5, lng, 2);
        Assert.Equal((40.0, 37.0), Line.At(int.MaxValue));
    }

    [Fact]
    public void A_route_has_two_points() => Assert.Throws<DomainException>(() => new Route([GeoPoint.At(41, 36)]));
}
