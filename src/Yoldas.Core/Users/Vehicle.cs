using System.Text.RegularExpressions;

namespace Yoldas.Core.Users;

/// <summary>
/// The car a driver offers seats in. The plate is shown only to passengers with a confirmed seat, so they can find
/// the right car; everyone else sees the make, model and colour.
/// </summary>
public sealed partial class Vehicle
{
    public const int MaxPassengerSeats = 8;

    private Vehicle()
    {
    }

    public Guid Id { get; private set; }

    public Guid OwnerId { get; private set; }

    public string Make { get; private set; } = string.Empty;

    public string Model { get; private set; } = string.Empty;

    public string Colour { get; private set; } = string.Empty;

    /// <summary>A Turkish plate, upper case with single spaces: "55 ABC 123".</summary>
    public string Plate { get; private set; } = string.Empty;

    /// <summary>Seats for passengers, not counting the driver's.</summary>
    public int PassengerSeats { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Vehicle Register(Guid ownerId, string? make, string? model, string? colour, string? plate, int passengerSeats, DateTimeOffset now)
    {
        var vehicle = new Vehicle { Id = Guid.CreateVersion7(now), OwnerId = ownerId };
        vehicle.Change(make, model, colour, plate, passengerSeats, now);
        return vehicle;
    }

    public void Change(string? make, string? model, string? colour, string? plate, int passengerSeats, DateTimeOffset now)
    {
        Make = Text.Clean(make, 2, 30, "A make");
        Model = Text.Clean(model, 1, 30, "A model");
        Colour = Text.Clean(colour, 3, 20, "A colour");
        Plate = NormalizePlate(plate);
        PassengerSeats = passengerSeats is >= 1 and <= MaxPassengerSeats
            ? passengerSeats
            : throw new DomainException(ErrorCodes.ValidationFailed, $"A car takes 1 to {MaxPassengerSeats} passengers.");
        UpdatedAt = now;
    }

    /// <summary>
    /// Province code 01–81, one to three letters, two to four digits: "34ab1234", "55 ABC 123" and "06-k-4567" all
    /// become the form printed on the plate.
    /// </summary>
    public static string NormalizePlate(string? plate)
    {
        var compact = (plate ?? string.Empty).ToUpperInvariant().Replace(" ", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal);
        var match = PlatePattern().Match(compact);
        if (!match.Success || int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) is < 1 or > 81)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "A Turkish plate looks like 55 ABC 123.");
        }

        return $"{match.Groups[1].Value} {match.Groups[2].Value} {match.Groups[3].Value}";
    }

    /// <summary>"55 *** *23": enough to recognise your own car in a list, not enough to identify it.</summary>
    public static string MaskPlate(string plate) =>
        plate.Length < 4 ? plate : $"{plate[..2]} {new string('*', plate.Split(' ')[1].Length)} {new string('*', plate.Split(' ')[2].Length - 2)}{plate[^2..]}";

    [GeneratedRegex("^([0-9]{2})([A-PR-VYZ]{1,3})([0-9]{2,4})$")]
    private static partial Regex PlatePattern();
}
