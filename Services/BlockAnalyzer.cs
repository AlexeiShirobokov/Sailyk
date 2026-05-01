using Sailyk.Utils;

namespace Sailyk.Services;

public static class BlockAnalyzer
{
    public static BlockAnalysisResult AnalyzeBlock(
        WorkBlock block,
        TinInterpolator projectTin,
        TinInterpolator factTin,
        double step)
    {
        var contour = block.Contour;

        var pointsInsideContour = 0;
        var validPoints = 0;

        var sumDz = 0.0;
        var minDz = double.MaxValue;
        var maxDz = double.MinValue;

        var positiveVolume = 0.0;
        var negativeVolume = 0.0;

        var cellArea = step * step;

        for (var x = contour.MinX + step / 2.0; x <= contour.MaxX; x += step)
        {
            for (var y = contour.MinY + step / 2.0; y <= contour.MaxY; y += step)
            {
                if (!GeometryUtils.IsPointInsidePolygon(x, y, contour.Points))
                    continue;

                pointsInsideContour++;

                var projectZ = projectTin.GetZ(x, y);
                var factZ = factTin.GetZ(x, y);

                if (projectZ is null || factZ is null)
                    continue;

                var dz = factZ.Value - projectZ.Value;

                validPoints++;
                sumDz += dz;

                minDz = Math.Min(minDz, dz);
                maxDz = Math.Max(maxDz, dz);

                var volume = dz * cellArea;

                if (volume >= 0)
                    positiveVolume += volume;
                else
                    negativeVolume += volume;
            }
        }

        return new BlockAnalysisResult
        {
            PointsInsideContour = pointsInsideContour,
            ValidPoints = validPoints,
            CalculatedArea = validPoints * cellArea,
            AverageDz = validPoints > 0 ? sumDz / validPoints : 0,
            MinDz = validPoints > 0 ? minDz : 0,
            MaxDz = validPoints > 0 ? maxDz : 0,
            PositiveVolume = positiveVolume,
            NegativeVolume = negativeVolume
        };
    }
}