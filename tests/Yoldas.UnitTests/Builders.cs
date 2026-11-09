using Yoldas.Core.Trips;

namespace Yoldas.UnitTests;

internal static class Builders
{
    public static readonly DateTimeOffset Now = new(2026, 11, 7, 9, 0, 0, TimeSpan.Zero);

    public static readonly Guid Driver = Guid.NewGuid();

    public static readonly Guid Car = Guid.NewGuid();

    public static readonly StopInput Samsun = new("Samsun Otogar", 41.2601, 36.2847);
    public static readonly StopInput Havza = new("Havza", 40.9700, 35.6622);
    public static readonly StopInput Corum = new("Çorum", 40.5506, 34.9556);
    public static readonly StopInput Ankara = new("AŞTİ", 39.9180, 32.8100);

    /// <summary>Samsun to Ankara by Havza and Çorum, tomorrow at 08:00 in Turkey, 3 seats at 400 TL for the whole way.</summary>
    public static Trip SamsunToAnkara(int seats = 3, long price = 400_00, bool instant = false, StopInput[]? stops = null) =>
        Trip.Offer(Driver, Car, 4, stops ?? [Samsun, Havza, Corum, Ankara], new DateTimeOffset(2026, 11, 8, 8, 0, 0, TimeSpan.FromHours(3)), seats, price, instant, null, Now);
}
