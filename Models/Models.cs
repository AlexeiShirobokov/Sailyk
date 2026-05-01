namespace Sailyk;

public sealed record DxfPair(int Code, string Value);

public sealed record DxfPoint(double X, double Y);

public sealed record DxfText(
    string Type,
    string Layer,
    string Value,
    double X,
    double Y
);

public sealed record BlockLabel(
    DxfText RawText,
    string Name
);

public sealed record WorkBlock(
    string Name,
    DxfText Label,
    DxfPolyline Contour
);

public sealed class DxfPolyline
{
    public string Layer { get; }
    public bool IsClosed { get; }
    public List<DxfPoint> Points { get; }

    public double MinX => Points.Min(p => p.X);
    public double MaxX => Points.Max(p => p.X);
    public double MinY => Points.Min(p => p.Y);
    public double MaxY => Points.Max(p => p.Y);
    public double Area => Math.Abs(GetSignedArea());

    public DxfPolyline(string layer, bool isClosed, List<DxfPoint> points)
    {
        Layer = layer;
        IsClosed = isClosed;
        Points = points;
    }

    private double GetSignedArea()
    {
        var area = 0.0;

        for (var i = 0; i < Points.Count; i++)
        {
            var j = (i + 1) % Points.Count;
            area += Points[i].X * Points[j].Y;
            area -= Points[j].X * Points[i].Y;
        }

        return area / 2.0;
    }
}

public sealed class TinPoint
{
    public required int Id { get; init; }
    public required double X { get; init; }
    public required double Y { get; init; }
    public required double Z { get; init; }
}

public sealed class TinTriangle
{
    public required int P1 { get; init; }
    public required int P2 { get; init; }
    public required int P3 { get; init; }
}

public sealed class TinSurface
{
    public required string Name { get; init; }
    public required List<TinPoint> Points { get; init; }
    public required List<TinTriangle> Triangles { get; init; }

    public Dictionary<int, TinPoint> PointsById => Points.ToDictionary(p => p.Id);

    public double MinX => Points.Min(p => p.X);
    public double MaxX => Points.Max(p => p.X);
    public double MinY => Points.Min(p => p.Y);
    public double MaxY => Points.Max(p => p.Y);
    public double MinZ => Points.Min(p => p.Z);
    public double MaxZ => Points.Max(p => p.Z);
}

public sealed class BlockAnalysisResult
{
    public required int PointsInsideContour { get; init; }
    public required int ValidPoints { get; init; }
    public required double CalculatedArea { get; init; }
    public required double AverageDz { get; init; }
    public required double MinDz { get; init; }
    public required double MaxDz { get; init; }
    public required double PositiveVolume { get; init; }
    public required double NegativeVolume { get; init; }

    public double TotalVolume => PositiveVolume + NegativeVolume;
}