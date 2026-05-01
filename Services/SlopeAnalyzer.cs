using Sailyk.Utils;

namespace Sailyk.Services;

public static class SlopeAnalyzer
{
    public static SlopeAnalysisResult AnalyzeSlopes(
        WorkBlock block,
        TinInterpolator projectTin,
        TinInterpolator factTin,
        double step,
        double slopeAngleThresholdDegrees)
    {
        var contour = block.Contour;

        var pointsInsideBlock = 0;
        var slopePoints = 0;

        var sumSlopeAngle = 0.0;
        var sumDz = 0.0;

        var positiveVolume = 0.0;
        var negativeVolume = 0.0;

        var slopePlanArea = 0.0;
        var slopeSurfaceArea = 0.0;

        var cellArea = step * step;

        for (var x = contour.MinX + step / 2.0; x <= contour.MaxX; x += step)
        {
            for (var y = contour.MinY + step / 2.0; y <= contour.MaxY; y += step)
            {
                if (!GeometryUtils.IsPointInsidePolygon(x, y, contour.Points))
                    continue;

                pointsInsideBlock++;

                var projectZ = projectTin.GetZ(x, y);
                var factZ = factTin.GetZ(x, y);

                if (projectZ is null || factZ is null)
                    continue;

                var slope = CalculateSlope(projectTin, x, y, step);

                if (slope is null)
                    continue;

                if (slope.AngleDegrees < slopeAngleThresholdDegrees)
                    continue;

                var dz = factZ.Value - projectZ.Value;

                slopePoints++;
                sumSlopeAngle += slope.AngleDegrees;
                sumDz += dz;

                slopePlanArea += cellArea;

                // Приближённая площадь поверхности:
                // площадь в плане / cos(угол уклона)
                var angleRad = slope.AngleDegrees * Math.PI / 180.0;
                slopeSurfaceArea += cellArea / Math.Cos(angleRad);

                var volume = dz * cellArea;

                if (volume >= 0)
                    positiveVolume += volume;
                else
                    negativeVolume += volume;
            }
        }

        return new SlopeAnalysisResult
        {
            PointsInsideBlock = pointsInsideBlock,
            SlopePoints = slopePoints,
            SlopePlanArea = slopePlanArea,
            SlopeSurfaceArea = slopeSurfaceArea,
            AverageSlopeAngle = slopePoints > 0 ? sumSlopeAngle / slopePoints : 0,
            AverageDz = slopePoints > 0 ? sumDz / slopePoints : 0,
            PositiveVolume = positiveVolume,
            NegativeVolume = negativeVolume
        };
    }

    private static LocalSlope? CalculateSlope(
        TinInterpolator tin,
        double x,
        double y,
        double step)
    {
        var zLeft = tin.GetZ(x - step, y);
        var zRight = tin.GetZ(x + step, y);
        var zDown = tin.GetZ(x, y - step);
        var zUp = tin.GetZ(x, y + step);

        if (zLeft is null || zRight is null || zDown is null || zUp is null)
            return null;

        var dzDx = (zRight.Value - zLeft.Value) / (2.0 * step);
        var dzDy = (zUp.Value - zDown.Value) / (2.0 * step);

        var gradient = Math.Sqrt(dzDx * dzDx + dzDy * dzDy);
        var angleRad = Math.Atan(gradient);
        var angleDegrees = angleRad * 180.0 / Math.PI;

        return new LocalSlope(angleDegrees);
    }

    private sealed record LocalSlope(double AngleDegrees);
}

public sealed class SlopeAnalysisResult
{
    public required int PointsInsideBlock { get; init; }
    public required int SlopePoints { get; init; }
    public required double SlopePlanArea { get; init; }
    public required double SlopeSurfaceArea { get; init; }
    public required double AverageSlopeAngle { get; init; }
    public required double AverageDz { get; init; }
    public required double PositiveVolume { get; init; }
    public required double NegativeVolume { get; init; }

    public double TotalVolume => PositiveVolume + NegativeVolume;
}