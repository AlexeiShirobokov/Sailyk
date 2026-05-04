using System.Globalization;
using System.Net;
using System.Text;
using Sailyk.Utils;

namespace Sailyk.Services;

public static class SurfaceChangeVisualizer
{
    public static List<SurfaceChangeCell> CollectCells(
        IReadOnlyList<WorkBlock> geologicalBlocks,
        IReadOnlyList<WorkBlock> blastBlocks,
        IReadOnlyList<WorkBlock> localAreas,
        IReadOnlyList<WorkBlock> productionContours,
        TinInterpolator initialTin,
        TinInterpolator finalTin,
        double step)
    {
        if (step <= 0)
            throw new ArgumentOutOfRangeException(nameof(step), "Шаг сетки должен быть больше нуля.");

        if (productionContours.Count == 0)
            throw new InvalidOperationException("Не найден производственный контур.");

        var cells = new List<SurfaceChangeCell>();

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

                var cutHeight = initialZ.Value - finalZ.Value;

                cells.Add(new SurfaceChangeCell(
                    X: x,
                    Y: y,
                    CellSize: step,
                    InitialZ: initialZ.Value,
                    FinalZ: finalZ.Value,
                    CutHeight: cutHeight,
                    GeologicalBlock: geological?.Name ?? "Без геоблока",
                    BlastBlock: blast?.Name ?? "Без взрывного блока",
                    LocalArea: local?.Name ?? "Без локальной площади",
                    ProductionContour: production.Name));
            }
        }

        return cells;
    }

    public static void WriteCellsCsv(
        string filePath,
        IReadOnlyList<SurfaceChangeCell> cells)
    {
        var lines = new List<string>
        {
            "X;Y;Z первоначальная;Z вторичная;ΔZ (снятие=+);Геологический блок;Взрывной блок;Локальная площадь;Производственный контур"
        };

        foreach (var cell in cells)
        {
            lines.Add(string.Join(";", new[]
            {
                F(cell.X, 3),
                F(cell.Y, 3),
                F(cell.InitialZ, 3),
                F(cell.FinalZ, 3),
                F(cell.CutHeight, 3),
                Csv(cell.GeologicalBlock),
                Csv(cell.BlastBlock),
                Csv(cell.LocalArea),
                Csv(cell.ProductionContour)
            }));
        }

        File.WriteAllLines(
            filePath,
            lines,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    public static void WriteCategorySummaryCsv(
        string filePath,
        IReadOnlyList<SurfaceChangeCell> cells,
        double nearZeroThresholdAbs,
        double strongChangeThresholdAbs)
    {
        var rows = cells
            .GroupBy(c => GetCategoryName(c.CutHeight, nearZeroThresholdAbs, strongChangeThresholdAbs))
            .Select(g => new
            {
                Category = g.Key,
                Cells = g.Count(),
                Area = g.Sum(x => x.CellSize * x.CellSize),
                AverageDz = g.Average(x => x.CutHeight),
                MinDz = g.Min(x => x.CutHeight),
                MaxDz = g.Max(x => x.CutHeight)
            })
            .OrderBy(x => GetCategoryOrder(x.Category))
            .ToList();

        var lines = new List<string>
        {
            "Категория;Количество ячеек;Площадь, м2;Среднее ΔZ, м;Мин ΔZ, м;Макс ΔZ, м"
        };

        foreach (var row in rows)
        {
            lines.Add(string.Join(";", new[]
            {
                Csv(row.Category),
                row.Cells.ToString(CultureInfo.InvariantCulture),
                F(row.Area, 0),
                F(row.AverageDz, 3),
                F(row.MinDz, 3),
                F(row.MaxDz, 3)
            }));
        }

        File.WriteAllLines(
            filePath,
            lines,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    public static void WriteGlobalChangeMapHtml(
        string filePath,
        IReadOnlyList<WorkBlock> productionContours,
        IReadOnlyList<WorkBlock> geologicalBlocks,
        IReadOnlyList<WorkBlock> blastBlocks,
        IReadOnlyList<WorkBlock> localAreas,
        IReadOnlyList<SurfaceChangeCell> cells,
        double nearZeroThresholdAbs,
        double strongChangeThresholdAbs)
    {
        var title = "Карта изменения мощности по всей рабочей области";
        var description =
            "Цвет показывает разницу между первоначальной и вторичной поверхностью. " +
            "Положительное ΔZ = поверхность стала ниже (снятие / вскрыша). " +
            "Отрицательное ΔZ = вторичная поверхность выше первоначальной.";

        WriteMapHtml(
            filePath,
            title,
            description,
            productionContours,
            geologicalBlocks,
            blastBlocks,
            localAreas,
            cells,
            c => GetCategoryColor(c.CutHeight, nearZeroThresholdAbs, strongChangeThresholdAbs),
            c => GetCategoryName(c.CutHeight, nearZeroThresholdAbs, strongChangeThresholdAbs),
            BuildLegendForGlobalMap());
    }

    public static void WriteWorkedAreaMapHtml(
        string filePath,
        IReadOnlyList<WorkBlock> productionContours,
        IReadOnlyList<WorkBlock> geologicalBlocks,
        IReadOnlyList<WorkBlock> blastBlocks,
        IReadOnlyList<WorkBlock> localAreas,
        IReadOnlyList<SurfaceChangeCell> cells,
        double workThresholdAbs)
    {
        var title = "Карта: где вообще работали";
        var description =
            $"Рабочей зоной считается ячейка, где |ΔZ| ≥ {workThresholdAbs:F1} м. " +
            "Красный = поверхность стала ниже, синий = поверхность стала выше, серый = почти без изменений.";

        WriteMapHtml(
            filePath,
            title,
            description,
            productionContours,
            geologicalBlocks,
            blastBlocks,
            localAreas,
            cells,
            c => GetWorkedAreaColor(c.CutHeight, workThresholdAbs),
            c => GetWorkedAreaName(c.CutHeight, workThresholdAbs),
            BuildLegendForWorkedAreaMap(workThresholdAbs));
    }

    private static void WriteMapHtml(
        string filePath,
        string title,
        string description,
        IReadOnlyList<WorkBlock> productionContours,
        IReadOnlyList<WorkBlock> geologicalBlocks,
        IReadOnlyList<WorkBlock> blastBlocks,
        IReadOnlyList<WorkBlock> localAreas,
        IReadOnlyList<SurfaceChangeCell> cells,
        Func<SurfaceChangeCell, string> colorSelector,
        Func<SurfaceChangeCell, string> categorySelector,
        string legendHtml)
    {
        var minX = productionContours.Min(x => x.Contour.MinX);
        var maxX = productionContours.Max(x => x.Contour.MaxX);
        var minY = productionContours.Min(x => x.Contour.MinY);
        var maxY = productionContours.Max(x => x.Contour.MaxY);

        const double canvasWidth = 1600.0;

        var worldWidth = maxX - minX;
        var worldHeight = maxY - minY;

        if (worldWidth <= 0)
            worldWidth = 1;

        if (worldHeight <= 0)
            worldHeight = 1;

        var canvasHeight = canvasWidth * worldHeight / worldWidth;
        if (canvasHeight < 900)
            canvasHeight = 900;

        double Tx(double x) => (x - minX) / worldWidth * canvasWidth;
        double Ty(double y) => canvasHeight - (y - minY) / worldHeight * canvasHeight;

        var summary = cells
            .GroupBy(categorySelector)
            .Select(g => new
            {
                Category = g.Key,
                Cells = g.Count(),
                Area = g.Sum(x => x.CellSize * x.CellSize),
                AvgDz = g.Average(x => x.CutHeight)
            })
            .OrderBy(x => x.Category)
            .ToList();

        var sb = new StringBuilder();

        sb.AppendLine("""
<!doctype html>
<html lang="ru">
<head>
<meta charset="utf-8" />
<title>Визуализация изменения поверхности</title>
<style>
body {
    font-family: Arial, sans-serif;
    margin: 0;
    background: #f4f4f4;
    color: #222;
}
.wrap {
    padding: 16px;
}
h1 {
    margin: 0 0 10px 0;
    font-size: 24px;
}
h2 {
    margin: 20px 0 10px 0;
    font-size: 18px;
}
p {
    margin: 6px 0;
}
.panel {
    background: #fff;
    padding: 12px;
    border-radius: 10px;
    box-shadow: 0 1px 6px rgba(0,0,0,.08);
    margin-bottom: 16px;
}
.legend {
    display: flex;
    gap: 18px;
    flex-wrap: wrap;
    margin: 12px 0 4px 0;
}
.legend span {
    display: inline-flex;
    align-items: center;
    gap: 8px;
    font-size: 14px;
}
.swatch {
    width: 18px;
    height: 18px;
    display: inline-block;
    border: 1px solid #333;
}
svg {
    width: 100%;
    height: auto;
    background: white;
    border: 1px solid #bbb;
}
table {
    border-collapse: collapse;
    width: 100%;
    background: #fff;
}
th, td {
    border: 1px solid #ccc;
    padding: 6px 8px;
    text-align: left;
    font-size: 13px;
}
th {
    background: #f0f0f0;
}
.small {
    font-size: 12px;
    color: #555;
}
</style>
</head>
<body>
<div class="wrap">
""");

        sb.AppendLine("<div class='panel'>");
        sb.AppendLine($"<h1>{H(title)}</h1>");
        sb.AppendLine($"<p>{H(description)}</p>");
        sb.AppendLine($"<p><b>Количество расчётных ячеек:</b> {cells.Count:N0}</p>");
        sb.AppendLine($"<p><b>Площадь расчётной области:</b> {cells.Sum(x => x.CellSize * x.CellSize):N0} м²</p>");
        sb.AppendLine(legendHtml);
        sb.AppendLine("</div>");

        sb.AppendLine($"<svg viewBox='0 0 {S(canvasWidth)} {S(canvasHeight)}'>");

        // Цветные ячейки
        sb.AppendLine("<g id='cells' stroke='rgba(80,80,80,0.12)' stroke-width='0.3'>");
        foreach (var cell in cells)
        {
            var x1 = Tx(cell.X - cell.CellSize / 2.0);
            var x2 = Tx(cell.X + cell.CellSize / 2.0);
            var y1 = Ty(cell.Y + cell.CellSize / 2.0);
            var y2 = Ty(cell.Y - cell.CellSize / 2.0);

            var rx = Math.Min(x1, x2);
            var ry = Math.Min(y1, y2);
            var rw = Math.Abs(x2 - x1);
            var rh = Math.Abs(y2 - y1);

            var fill = colorSelector(cell);
            var category = categorySelector(cell);

            sb.AppendLine(
                $"<rect x='{S(rx)}' y='{S(ry)}' width='{S(rw)}' height='{S(rh)}' fill='{fill}'>" +
                $"<title>{H(category)} | ΔZ={cell.CutHeight:F2} м | Геоблок={H(cell.GeologicalBlock)} | П-блок={H(cell.BlastBlock)} | Локальная={H(cell.LocalArea)}</title>" +
                $"</rect>");
        }
        sb.AppendLine("</g>");

        // Локальные площади
        sb.AppendLine("<g id='local-areas' fill='none' stroke='#888' stroke-width='1.0' stroke-dasharray='4,3'>");
        foreach (var area in localAreas)
        {
            sb.AppendLine(Polygon(area.Contour, Tx, Ty, area.Name));
        }
        sb.AppendLine("</g>");

        // Геоблоки
        sb.AppendLine("<g id='geo' fill='none' stroke='red' stroke-width='2.2'>");
        foreach (var block in geologicalBlocks)
        {
            sb.AppendLine(Polygon(block.Contour, Tx, Ty, block.Name));
        }
        sb.AppendLine("</g>");

        // П-блоки
        sb.AppendLine("<g id='blast' fill='none' stroke='blue' stroke-width='2.3'>");
        foreach (var block in blastBlocks)
        {
            sb.AppendLine(Polygon(block.Contour, Tx, Ty, block.Name));
        }
        sb.AppendLine("</g>");

        // Производственный контур
        sb.AppendLine("<g id='production' fill='none' stroke='#00aa00' stroke-width='3.2'>");
        foreach (var block in productionContours)
        {
            sb.AppendLine(Polygon(block.Contour, Tx, Ty, block.Name));
        }
        sb.AppendLine("</g>");

        // Подписи геоблоков
        sb.AppendLine("<g font-size='22' font-family='Arial' fill='black'>");
        foreach (var block in geologicalBlocks)
        {
            var c = block.Contour.GetCentroid();
            sb.AppendLine($"<text x='{S(Tx(c.X))}' y='{S(Ty(c.Y))}'>{H(block.Name)}</text>");
        }
        sb.AppendLine("</g>");

        // Подписи П-блоков
        sb.AppendLine("<g font-size='22' font-family='Arial' fill='blue'>");
        foreach (var block in blastBlocks)
        {
            var c = block.Contour.GetCentroid();
            sb.AppendLine($"<text x='{S(Tx(c.X))}' y='{S(Ty(c.Y))}'>{H(block.Name)}</text>");
        }
        sb.AppendLine("</g>");

        sb.AppendLine("</svg>");

        sb.AppendLine("<div class='panel' style='margin-top:16px;'>");
        sb.AppendLine("<h2>Сводка по категориям</h2>");
        sb.AppendLine("<table>");
        sb.AppendLine("<tr><th>Категория</th><th>Ячеек</th><th>Площадь, м²</th><th>Среднее ΔZ, м</th></tr>");

        foreach (var row in summary)
        {
            sb.AppendLine(
                "<tr>" +
                $"<td>{H(row.Category)}</td>" +
                $"<td>{row.Cells:N0}</td>" +
                $"<td>{row.Area:N0}</td>" +
                $"<td>{row.AvgDz:F3}</td>" +
                "</tr>");
        }

        sb.AppendLine("</table>");
        sb.AppendLine("<p class='small'>Подсказка: наведи курсор на ячейку карты, чтобы увидеть её ΔZ и принадлежность к контурам.</p>");
        sb.AppendLine("</div>");

        sb.AppendLine("</div>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        File.WriteAllText(
            filePath,
            sb.ToString(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        static string Polygon(
            DxfPolyline contour,
            Func<double, double> tx,
            Func<double, double> ty,
            string? title)
        {
            var points = string.Join(
                " ",
                contour.Points.Select(p =>
                    $"{tx(p.X).ToString(CultureInfo.InvariantCulture)},{ty(p.Y).ToString(CultureInfo.InvariantCulture)}"));

            var titleTag = string.IsNullOrWhiteSpace(title)
                ? ""
                : $"<title>{H(title)}</title>";

            return $"<polygon points='{points}'>{titleTag}</polygon>";
        }
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

    private static string GetCategoryName(
        double dz,
        double nearZeroThresholdAbs,
        double strongChangeThresholdAbs)
    {
        if (dz <= -strongChangeThresholdAbs)
            return $"меньше -{strongChangeThresholdAbs:F0} м";

        if (dz < -nearZeroThresholdAbs)
            return $"-{strongChangeThresholdAbs:F0} ... -{nearZeroThresholdAbs:F0} м";

        if (Math.Abs(dz) <= nearZeroThresholdAbs)
            return "около проекта";

        if (dz < strongChangeThresholdAbs)
            return $"+{nearZeroThresholdAbs:F0} ... +{strongChangeThresholdAbs:F0} м";

        return $"больше +{strongChangeThresholdAbs:F0} м";
    }

    private static string GetCategoryColor(
        double dz,
        double nearZeroThresholdAbs,
        double strongChangeThresholdAbs)
    {
        if (dz <= -strongChangeThresholdAbs)
            return "#0B3C7F"; // темно-синий

        if (dz < -nearZeroThresholdAbs)
            return "#74ADD1"; // светло-синий

        if (Math.Abs(dz) <= nearZeroThresholdAbs)
            return "#E5E5E5"; // серый

        if (dz < strongChangeThresholdAbs)
            return "#F2AD63"; // оранжевый

        return "#B30000"; // красный
    }

    private static string GetWorkedAreaName(double dz, double workThresholdAbs)
    {
        if (dz <= -workThresholdAbs)
            return $"вторичная поверхность выше на {workThresholdAbs:F1} м и более";

        if (dz >= workThresholdAbs)
            return $"снятие / вскрыша {workThresholdAbs:F1} м и более";

        return "почти без изменений";
    }

    private static string GetWorkedAreaColor(double dz, double workThresholdAbs)
    {
        if (dz <= -workThresholdAbs)
            return "#5AA0D6"; // синий

        if (dz >= workThresholdAbs)
            return "#CC3D3D"; // красный

        return "#E8E8E8"; // серый
    }

    private static string BuildLegendForGlobalMap()
    {
        return """
<div class="legend">
    <span><i class="swatch" style="background:#0B3C7F"></i> меньше -5 м</span>
    <span><i class="swatch" style="background:#74ADD1"></i> -5 ... -1 м</span>
    <span><i class="swatch" style="background:#E5E5E5"></i> около проекта</span>
    <span><i class="swatch" style="background:#F2AD63"></i> +1 ... +5 м</span>
    <span><i class="swatch" style="background:#B30000"></i> больше +5 м</span>
</div>
""";
    }

    private static string BuildLegendForWorkedAreaMap(double workThresholdAbs)
    {
        return $"""
<div class="legend">
    <span><i class="swatch" style="background:#5AA0D6"></i> вторичная выше на {workThresholdAbs:F1} м и более</span>
    <span><i class="swatch" style="background:#E8E8E8"></i> почти без изменений</span>
    <span><i class="swatch" style="background:#CC3D3D"></i> снятие / вскрыша {workThresholdAbs:F1} м и более</span>
</div>
""";
    }

    private static int GetCategoryOrder(string category)
    {
        return category switch
        {
            var x when x.StartsWith("меньше -") => 1,
            var x when x.StartsWith("-") => 2,
            "около проекта" => 3,
            var x when x.StartsWith("+") => 4,
            var x when x.StartsWith("больше +") => 5,
            _ => 99
        };
    }

    private static string S(double value)
    {
        return value.ToString("F3", CultureInfo.InvariantCulture);
    }

    private static string F(double value, int digits)
    {
        return value.ToString($"F{digits}", CultureInfo.GetCultureInfo("ru-RU"));
    }

    private static string H(string value)
    {
        return WebUtility.HtmlEncode(value);
    }

    private static string Csv(string? value)
    {
        value ??= "";

        var escaped = value.Replace("\"", "\"\"");

        if (
            escaped.Contains(';') ||
            escaped.Contains('"') ||
            escaped.Contains('\n') ||
            escaped.Contains('\r'))
        {
            return $"\"{escaped}\"";
        }

        return escaped;
    }
}