namespace Sailyk.Services;

public sealed class TinInterpolator
{
    private readonly TinSurface _surface;
    private readonly Dictionary<int, TinPoint> _pointsById;
    private readonly Dictionary<(int CellX, int CellY), List<TriangleData>> _grid = new();

    private const double CellSize = 50.0;

    public TinInterpolator(TinSurface surface)
    {
        _surface = surface;
        _pointsById = surface.PointsById;

        BuildGridIndex();
    }

    public double? GetZ(double x, double y)
    {
        var cellX = GetCell(x);
        var cellY = GetCell(y);

        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                var key = (cellX + dx, cellY + dy);

                if (!_grid.TryGetValue(key, out var triangles))
                    continue;

                foreach (var triangle in triangles)
                {
                    if (TryInterpolateZ(x, y, triangle.A, triangle.B, triangle.C, out var z))
                        return z;
                }
            }
        }

        return null;
    }

    private void BuildGridIndex()
    {
        foreach (var triangle in _surface.Triangles)
        {
            if (!_pointsById.TryGetValue(triangle.P1, out var a))
                continue;

            if (!_pointsById.TryGetValue(triangle.P2, out var b))
                continue;

            if (!_pointsById.TryGetValue(triangle.P3, out var c))
                continue;

            var triangleData = new TriangleData(a, b, c);

            var minCellX = GetCell(Math.Min(a.X, Math.Min(b.X, c.X)));
            var maxCellX = GetCell(Math.Max(a.X, Math.Max(b.X, c.X)));
            var minCellY = GetCell(Math.Min(a.Y, Math.Min(b.Y, c.Y)));
            var maxCellY = GetCell(Math.Max(a.Y, Math.Max(b.Y, c.Y)));

            for (var ix = minCellX; ix <= maxCellX; ix++)
            {
                for (var iy = minCellY; iy <= maxCellY; iy++)
                {
                    var key = (ix, iy);

                    if (!_grid.TryGetValue(key, out var list))
                    {
                        list = new List<TriangleData>();
                        _grid[key] = list;
                    }

                    list.Add(triangleData);
                }
            }
        }
    }

    private static int GetCell(double value)
    {
        return (int)Math.Floor(value / CellSize);
    }

    private static bool TryInterpolateZ(
        double x,
        double y,
        TinPoint a,
        TinPoint b,
        TinPoint c,
        out double z)
    {
        z = 0;

        var denominator =
            (b.Y - c.Y) * (a.X - c.X) +
            (c.X - b.X) * (a.Y - c.Y);

        if (Math.Abs(denominator) < 0.000000001)
            return false;

        var w1 =
            ((b.Y - c.Y) * (x - c.X) +
             (c.X - b.X) * (y - c.Y)) / denominator;

        var w2 =
            ((c.Y - a.Y) * (x - c.X) +
             (a.X - c.X) * (y - c.Y)) / denominator;

        var w3 = 1.0 - w1 - w2;

        const double tolerance = -0.0000001;

        if (w1 < tolerance || w2 < tolerance || w3 < tolerance)
            return false;

        z = w1 * a.Z + w2 * b.Z + w3 * c.Z;
        return true;
    }

    private sealed record TriangleData(TinPoint A, TinPoint B, TinPoint C);
}