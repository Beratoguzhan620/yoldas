using Yoldas.Core;
using Yoldas.Core.Places;
using Yoldas.Core.Users;

namespace Yoldas.UnitTests;

public sealed class PlaceAndCarTests
{
    [Fact]
    public void All_81_provinces_are_there_once()
    {
        var provinces = Places.All.Where(p => p.Name == p.Province).Select(p => p.Name).ToList();

        Assert.Equal(81, provinces.Count);
        Assert.Equal(81, provinces.Distinct().Count());
        Assert.All(Places.All, p => Assert.True(p.Latitude is > 35.5 and < 42.2 && p.Longitude is > 25.6 and < 44.9, p.Name));
    }

    [Theory]
    [InlineData("canakkale", "Çanakkale")]
    [InlineData("ÇANAKKALE", "Çanakkale")]
    [InlineData("istanbul", "İstanbul")]
    [InlineData("igdir", "Iğdır")]
    [InlineData("sams", "Samsun")]
    [InlineData("carsamba", "Çarşamba")]
    public void Places_match_however_they_are_typed(string query, string expected) =>
        Assert.Equal(expected, Places.Find(query)[0].Name);

    [Fact]
    public void A_province_comes_before_its_towns_and_towns_say_where_they_are()
    {
        var found = Places.Find("samsun");

        Assert.Equal("Samsun", found[0].Label);
        Assert.Contains(found, p => p.Label == "Bafra, Samsun");
        Assert.Empty(Places.Find("  "));
    }

    [Theory]
    [InlineData("55abc123", "55 ABC 123")]
    [InlineData(" 34 k 4567 ", "34 K 4567")]
    [InlineData("06-ab-12", "06 AB 12")]
    public void Plates_are_written_as_on_the_car(string typed, string plate) => Assert.Equal(plate, Vehicle.NormalizePlate(typed));

    [Theory]
    [InlineData("00 ABC 123")]
    [InlineData("82 ABC 123")]
    [InlineData("55 ABCD 123")]
    [InlineData("55 ABÇ 123")]
    [InlineData("55 ABC 1")]
    public void Plates_that_cannot_exist_are_refused(string plate) => Assert.Throws<DomainException>(() => Vehicle.NormalizePlate(plate));

    [Fact]
    public void A_car_takes_one_to_eight_passengers()
    {
        Assert.Throws<DomainException>(() => Vehicle.Register(Guid.NewGuid(), "Renault", "Clio", "Beyaz", "55 ABC 123", 0, Builders.Now));
        var car = Vehicle.Register(Guid.NewGuid(), " Renault ", "Clio", "Beyaz", "55abc123", 4, Builders.Now);
        Assert.Equal(("Renault", "55 ABC 123", 4), (car.Make, car.Plate, car.PassengerSeats));
    }
}
