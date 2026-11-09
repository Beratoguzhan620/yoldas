using Yoldas.Core;
using Yoldas.Core.Geo;

namespace Yoldas.UnitTests;

public sealed class GeoTests
{
    [Fact]
    public void Points_are_longitude_first_like_PostGIS_and_rounded_to_a_tenth_of_a_metre()
    {
        var point = GeoPoint.At(41.28671234, 36.33004567);

        Assert.Equal((36.330046, 41.286712, 4326), (point.X, point.Y, point.SRID));
    }

    [Theory]
    [InlineData(91, 36)]
    [InlineData(41, 181)]
    [InlineData(double.NaN, 36)]
    public void Points_must_be_on_the_earth(double lat, double lng) =>
        Assert.Throws<DomainException>(() => GeoPoint.At(lat, lng));

    [Fact]
    public void Distances_are_great_circle_kilometres()
    {
        var centre = GeoPoint.At(41.2867, 36.33);

        Assert.InRange(GeoPoint.DistanceKm(centre, GeoPoint.At(41.3335, 36.2714)), 7.0, 7.3);
        Assert.InRange(GeoPoint.DistanceKm(centre, GeoPoint.At(39.9334, 32.8597)), 325, 335);
    }
}
