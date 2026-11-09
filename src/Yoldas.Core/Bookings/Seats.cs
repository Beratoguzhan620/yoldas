namespace Yoldas.Core.Bookings;

/// <summary>A part of a route some seats are taken on, from one position to another in metres, the end not included.</summary>
public readonly record struct SeatSpan(int FromMeters, int ToMeters, int Seats);

/// <summary>
/// How many seats are taken along a route. A seat is free again where its passenger gets out, so one seat carries a
/// passenger from Samsun to Havza and another from Havza to Ankara. The database checks the same rule when a seat is
/// confirmed; this is for showing what is left.
/// </summary>
public static class Seats
{
    /// <summary>The most seats taken at any point from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public static int MaxTaken(IEnumerable<SeatSpan> taken, int from, int to)
    {
        var spans = taken.Where(s => s.FromMeters < to && from < s.ToMeters).ToList();
        // The load only rises where someone gets in, so it is enough to look there and at the start.
        var points = spans.Select(s => s.FromMeters).Where(p => p > from).Append(from);
        return points.Select(p => spans.Where(s => s.FromMeters <= p && p < s.ToMeters).Sum(s => s.Seats)).DefaultIfEmpty(0).Max();
    }

    public static int Free(int capacity, IEnumerable<SeatSpan> taken, int from, int to) => Math.Max(0, capacity - MaxTaken(taken, from, to));
}
