using System;
using System.Collections.Generic;
using System.Linq;

namespace Sailyk;

public sealed record DxfPair(int Code, string Value);

public sealed record DxfPoint(double X, double Y);

public sealed record DxfText(
    string Type,
    string Layer,
    string Value,
    double X,
    double Y,
    int? ColorIndex);

public sealed record BlockLabel(
    DxfText RawText,
    string Name);

public sealed record WorkBlock(
    string Name,
    DxfText Label,
    DxfPolyline Contour);

public sealed class DxfPolyline
{
    public string Layer { get; }

    public bool IsClosed { get; }

    public int? ColorIndex { get; }

    public List<DxfPoint> Points { get; }

    public double MinX => Points.Min(p => p.X);

    public double MaxX => Points.Max(p => p.X);

    public double MinY => Points.Min(p => p.Y);

    public double MaxY => Points.Max(p => p.Y);

    public double Area => Math.Abs(GetSignedArea());

    public DxfPolyline(
        string layer,
        bool isClosed,
        List<DxfPoint> points,
        int? colorIndex = null)
    {
        Layer = layer;
        IsClosed = isClosed;
        Points = points;
        ColorIndex = colorIndex;
    }

    public DxfPoint GetCentroid()
    {
        var signedArea = GetSignedArea();

        if (Math.Abs(signedArea) < 0.000000001)
        {
            return new DxfPoint(
                Points.Average(p => p.X),
                Points.Average(p => p.Y));
        }

        var cx = 0.0;
        var cy = 0.0;

        for (var i = 0; i < Points.Count; i++)
        {
            var j = (i + 1) % Points.Count;

            var factor =
                Points[i].X * Points[j].Y -
                Points[j].X * Points[i].Y;

            cx += (Points[i].X + Points[j].X) * factor;
            cy += (Points[i].Y + Points[j].Y) * factor;
        }

        cx /= 6.0 * signedArea;
        cy /= 6.0 * signedArea;

        return new DxfPoint(cx, cy);
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

public sealed record VolumeBreakdownKey(
    string GeologicalBlock,
    string BlastBlock,
    string LocalArea);

public sealed class VolumeBreakdownResult
{
    public required VolumeBreakdownKey Key { get; init; }

    public int GridPoints { get; set; }

    public double Area { get; set; }

    public double SumCutHeight { get; set; }

    public double MinCutHeight { get; set; } = double.PositiveInfinity;

    public double MaxCutHeight { get; set; } = double.NegativeInfinity;

    public double AverageCutHeight => GridPoints > 0
        ? SumCutHeight / GridPoints
        : 0.0;

    public double ExcavationVolume { get; set; }

    public double FillOrErrorVolume { get; set; }

    public double NetVolume => ExcavationVolume + FillOrErrorVolume;
}

public sealed record VolumeSummaryRow(
    string GroupName,
    double Area,
    double ExcavationVolume,
    double FillOrErrorVolume,
    double NetVolume);

public sealed record DebugGridCell(
    double X,
    double Y,
    double CellSize,
    string GeologicalBlock,
    string BlastBlock,
    string LocalArea,
    double CutHeight,
    double Volume);
    
public sealed record SurfaceChangeCell(
    double X,
    double Y,
    double CellSize,
    double InitialZ,
    double FinalZ,
    double CutHeight,
    string GeologicalBlock,
    string BlastBlock,
    string LocalArea,
    string ProductionContour);    