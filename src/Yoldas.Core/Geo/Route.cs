using NetTopologySuite.Geometries;

namespace Yoldas.Core.Geo;

/// <summary>Where a point meets a route: the closest point on it, how far along the route that is, and how far off.</summary>
public readonly record struct RoutePosition(double Latitude, double Longitude, int MetersAlong, int MetersOff);

/// <summary>
/// A driver's route: straight legs between the stops they chose. Positions along it are whole metres from the first
/// stop, which is how bookings say which part of the trip they use. Distances are great-circle; within a leg a point is
/// projected on a flat local map, which is accurate to metres at the lengths involved.
/// </summary>
public sealed class Route
{
    private readonly Point[] _points;
    private readonly int[] _cumulative;

    public Route(IReadOnlyList<Point> points)
    {
        if (points.Count < 2)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "A route has at least two stops.");
        }

        _points = [.. points];
        _cumulative = new int[points.Count];
        double total = 0;
        for (var i = 1; i < points.Count; i++)
        {
            total += GeoPoint.DistanceKm(points[i - 1], points[i]) * 1000;
            _cumulative[i] = (int)Math.Round(total);
        }
    }

    public int LengthMeters => _cumulative[^1];

    /// <summary>How far along the route each stop is, in metres.</summary>
    public IReadOnlyList<int> StopPositions => _cumulative;

    public LineString ToLineString() => GeoPoint.Factory.CreateLineString(_points.Select(p => p.Coordinate).ToArray());

    /// <summary>The point on the route closest to <paramref name="point"/>; ties go to the earlier leg.</summary>
    public RoutePosition Locate(Point point)
    {
        RoutePosition best = default;
        var bestOff = double.MaxValue;
        for (var i = 1; i < _points.Length; i++)
        {
            var (a, b) = (_points[i - 1], _points[i]);
            // A flat map centred on the leg: x east and y north, in metres.
            var cos = Math.Cos((a.Y + b.Y) / 2 * Math.PI / 180);
            const double MetersPerDegree = 111_195.08;
            double X(Point p) => (p.X - a.X) * cos * MetersPerDegree;
            double Y(Point p) => (p.Y - a.Y) * MetersPerDegree;
            var (bx, by, px, py) = (X(b), Y(b), X(point), Y(point));
            var lengthSquared = (bx * bx) + (by * by);
            var t = lengthSquared == 0 ? 0 : Math.Clamp(((px * bx) + (py * by)) / lengthSquared, 0, 1);
            var closest = GeoPoint.At(a.Y + ((b.Y - a.Y) * t), a.X + ((b.X - a.X) * t));
            var off = GeoPoint.DistanceKm(closest, point) * 1000;
            if (off < bestOff - 0.5)
            {
                bestOff = off;
                var along = _cumulative[i - 1] + ((_cumulative[i] - _cumulative[i - 1]) * t);
                best = new RoutePosition(closest.Y, closest.X, (int)Math.Round(along), (int)Math.Round(off));
            }
        }

        return best;
    }

    /// <summary>The coordinates at a position along the route.</summary>
    public (double Latitude, double Longitude) At(int metersAlong)
    {
        var position = Math.Clamp(metersAlong, 0, LengthMeters);
        for (var i = 1; i < _points.Length; i++)
        {
            if (position <= _cumulative[i] || i == _points.Length - 1)
            {
                var span = _cumulative[i] - _cumulative[i - 1];
                var t = span == 0 ? 0 : (double)(position - _cumulative[i - 1]) / span;
                return (Math.Round(_points[i - 1].Y + ((_points[i].Y - _points[i - 1].Y) * t), 6), Math.Round(_points[i - 1].X + ((_points[i].X - _points[i - 1].X) * t), 6));
            }
        }

        return (_points[^1].Y, _points[^1].X);
    }
}
