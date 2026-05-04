using Sailyk.Utils;

namespace Sailyk.Services;

public static class VolumeBreakdownAnalyzer
{
    public static List<VolumeBreakdownResult> Analyze(
        IReadOnlyList<WorkBlock> geologicalBlocks,
        IReadOnlyList<WorkBlock> blastBlocks,
        IReadOnlyList<WorkBlock> localAreas,
        IReadOnlyList<WorkBlock> productionContours,
        TinInterpolator initialTin,
        TinInterpolator finalTin,
        double step,
        Action<DebugGridCell>? noLocalAreaCellCollector = null)
    {
        if (step <= 0)
            throw new ArgumentOutOfRangeException(nameof(step), "Шаг сетки должен быть больше нуля.");

        if (productionContours.Count == 0)
            throw new InvalidOperationException("Не найден производственный контур.");

        var result = new Dictionary<VolumeBreakdownKey, VolumeBreakdownResult>();
        var cellArea = step * step;

        var minX = productionContours.Min(b => b.Contour.MinX);
        var maxX = productionContours.Max(b => b.Contour.MaxX);
        var minY = productionContours.Min(b => b.Contour.MinY);
        var maxY = productionContours.Max(b => b.Contour.MaxY);

        for (var x = minX + step / 2.0; x <= maxX; x += step)
        {
            for (var y = minY + step / 2.0; y <= maxY; y += step)
            {
                var production = FindContainingBlock(x, y, productionContours);

                if (production is null)
                    continue;

                var initialZ = initialTin.GetZ(x, y);
                var finalZ = finalTin.GetZ(x, y);

                if (initialZ is null || finalZ is null)
                    continue;

                var geological = FindContainingBlock(x, y, geologicalBlocks);
                var blast = FindContainingBlock(x, y, blastBlocks);
                var local = FindContainingBlock(x, y, localAreas);

                var geologicalName = geological?.Name ?? "Без геоблока";
                var blastName = blast?.Name ?? "Без взрывного блока";
                var localName = local?.Name ?? "Без локальной площади";

                var key = new VolumeBreakdownKey(
                    GeologicalBlock: geologicalName,
                    BlastBlock: blastName,
                    LocalArea: localName);

                if (!result.TryGetValue(key, out var row))
                {
                    row = new VolumeBreakdownResult
                    {
                        Key = key
                    };

                    result[key] = row;
                }

                var cutHeight = initialZ.Value - finalZ.Value;
                var volume = cutHeight * cellArea;

                row.GridPoints++;
                row.Area += cellArea;
                row.SumCutHeight += cutHeight;
                row.MinCutHeight = Math.Min(row.MinCutHeight, cutHeight);
                row.MaxCutHeight = Math.Max(row.MaxCutHeight, cutHeight);

                if (volume >= 0)
                    row.ExcavationVolume += volume;
                else
                    row.FillOrErrorVolume += volume;

                if (local is null)
                {
                    noLocalAreaCellCollector?.Invoke(new DebugGridCell(
                        X: x,
                        Y: y,
                        CellSize: step,
                        GeologicalBlock: geologicalName,
                        BlastBlock: blastName,
                        LocalArea: localName,
                        CutHeight: cutHeight,
                        Volume: volume));
                }
            }
        }

        return result
            .Values
            .OrderBy(r => r.Key.GeologicalBlock)
            .ThenBy(r => r.Key.BlastBlock)
            .ThenBy(r => r.Key.LocalArea)
            .ToList();
    }

    private static WorkBlock? FindContainingBlock(
        double x,
        double y,
        IReadOnlyList<WorkBlock> blocks)
    {
        return blocks
            .Where(b => IsPointInBoundingBox(x, y, b.Contour))
            .Where(b => GeometryUtils.IsPointInsidePolygon(x, y, b.Contour.Points))
            .OrderBy(b => b.Contour.Area)
            .FirstOrDefault();
    }

    private static bool IsPointInBoundingBox(
        double x,
        double y,
        DxfPolyline polygon)
    {
        return
            x >= polygon.MinX &&
            x <= polygon.MaxX &&
            y >= polygon.MinY &&
            y <= polygon.MaxY;
    }
}