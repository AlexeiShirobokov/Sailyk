using System.Globalization;
using System.Net;
using System.Text;

namespace Sailyk.Services;

public static class DebugMapWriter
{
    private static readonly CultureInfo RuCulture = CultureInfo.GetCultureInfo("ru-RU");

    public static void WriteNoLocalAreaPointsCsv(
        string filePath,
        IEnumerable<DebugGridCell> cells)
    {
        var lines = new List<string>
        {
            "X;Y;Геологический блок;Взрывной блок;Локальная площадь;Мощность, м;Объем, м3"
        };

        foreach (var cell in cells)
        {
            lines.Add(string.Join(";", new[]
            {
                F(cell.X, 3),
                F(cell.Y, 3),
                Csv(cell.GeologicalBlock),
                Csv(cell.BlastBlock),
                Csv(cell.LocalArea),
                F(cell.CutHeight, 3),
                F(cell.Volume, 3)
            }));
        }

        File.WriteAllLines(
            filePath,
            lines,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    public static void WriteNoLocalAreaSummaryCsv(
        string filePath,
        IEnumerable<DebugGridCell> cells)
    {
        var rows = cells
            .GroupBy(x => new
            {
                x.GeologicalBlock,
                x.BlastBlock
            })
            .Select(g => new
            {
                g.Key.GeologicalBlock,
                g.Key.BlastBlock,
                Points = g.Count(),
                Area = g.Sum(x => x.CellSize * x.CellSize),
                ExcavationVolume = g.Where(x => x.Volume >= 0).Sum(x => x.Volume),
                FillOrErrorVolume = g.Where(x => x.Volume < 0).Sum(x => x.Volume),
                NetVolume = g.Sum(x => x.Volume),
                AvgCut = g.Average(x => x.CutHeight)
            })
            .OrderBy(x => x.GeologicalBlock)
            .ThenBy(x => x.BlastBlock)
            .ToList();

        var lines = new List<string>
        {
            "Геологический блок;Взрывной блок;Точек;Площадь, м2;Объем вскрыши, м3;Отрицательный объем, м3;Баланс, м3;Средняя мощность, м"
        };

        foreach (var row in rows)
        {
            lines.Add(string.Join(";", new[]
            {
                Csv(row.GeologicalBlock),
                Csv(row.BlastBlock),
                row.Points.ToString(CultureInfo.InvariantCulture),
                F(row.Area, 0),
                F(row.ExcavationVolume, 0),
                F(row.FillOrErrorVolume, 0),
                F(row.NetVolume, 0),
                F(row.AvgCut, 3)
            }));
        }

        File.WriteAllLines(
            filePath,
            lines,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    public static void WriteNoLocalAreaMapHtml(
        string filePath,
        IReadOnlyList<WorkBlock> productionContours,
        IReadOnlyList<WorkBlock> geologicalBlocks,
        IReadOnlyList<WorkBlock> blastBlocks,
        IReadOnlyList<WorkBlock> localAreas,
        IReadOnlyList<DebugGridCell> noLocalCells)
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

        var summary = noLocalCells
            .GroupBy(x => new
            {
                x.GeologicalBlock,
                x.BlastBlock
            })
            .Select(g => new
            {
                g.Key.GeologicalBlock,
                g.Key.BlastBlock,
                Area = g.Sum(x => x.CellSize * x.CellSize),
                NetVolume = g.Sum(x => x.Volume),
                ExcavationVolume = g.Where(x => x.Volume >= 0).Sum(x => x.Volume),
                FillOrErrorVolume = g.Where(x => x.Volume < 0).Sum(x => x.Volume),
                Points = g.Count()
            })
            .OrderByDescending(x => x.Area)
            .ToList();

        double Tx(double x) => (x - minX) / worldWidth * canvasWidth;

        double Ty(double y) => canvasHeight - (y - minY) / worldHeight * canvasHeight;

        var sb = new StringBuilder();

        sb.AppendLine("""
<!doctype html>
<html lang="ru">
<head>
<meta charset="utf-8" />
<title>Карта зоны без локальной площади</title>
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
        sb.AppendLine("<h1>Карта зоны «Без локальной площади»</h1>");
        sb.AppendLine("<p>Оранжевым показаны ячейки сетки, которые попали внутрь зелёного производственного контура, но не попали ни в одну локальную площадь uhodka.</p>");
        sb.AppendLine($"<p><b>Количество ячеек:</b> {noLocalCells.Count:N0}</p>");
        sb.AppendLine($"<p><b>Площадь:</b> {noLocalCells.Sum(x => x.CellSize * x.CellSize):N0} м²</p>");
        sb.AppendLine($"<p><b>Объём вскрыши:</b> {noLocalCells.Where(x => x.Volume >= 0).Sum(x => x.Volume):N0} м³</p>");
        sb.AppendLine($"<p><b>Отрицательный объём:</b> {noLocalCells.Where(x => x.Volume < 0).Sum(x => x.Volume):N0} м³</p>");
        sb.AppendLine($"<p><b>Баланс:</b> {noLocalCells.Sum(x => x.Volume):N0} м³</p>");

        sb.AppendLine("""
<div class="legend">
    <span><i class="swatch" style="background:#ff8c00;opacity:.65"></i> Без локальной площади</span>
    <span><i class="swatch" style="background:transparent;border:3px solid #00aa00"></i> Производственный контур</span>
    <span><i class="swatch" style="background:transparent;border:2px solid red"></i> Геоблок</span>
    <span><i class="swatch" style="background:transparent;border:2px solid blue"></i> П-блок</span>
    <span><i class="swatch" style="background:#d9d9d9;border:1px solid #888"></i> Локальные площади</span>
</div>
</div>
""");

        sb.AppendLine($"<svg viewBox='0 0 {S(canvasWidth)} {S(canvasHeight)}'>");

        // Локальные площади
        sb.AppendLine("<g id='local-areas' fill='rgba(180,180,180,0.25)' stroke='#999' stroke-width='1'>");
        foreach (var area in localAreas)
        {
            sb.AppendLine(Polygon(area.Contour, Tx, Ty, area.Name));
        }
        sb.AppendLine("</g>");

        // Ячейки без локальной площади
        sb.AppendLine("<g id='no-local-cells' fill='rgba(255,140,0,0.60)' stroke='rgba(180,80,0,0.35)' stroke-width='0.4'>");
        foreach (var cell in noLocalCells)
        {
            var x1 = Tx(cell.X - cell.CellSize / 2.0);
            var x2 = Tx(cell.X + cell.CellSize / 2.0);
            var y1 = Ty(cell.Y + cell.CellSize / 2.0);
            var y2 = Ty(cell.Y - cell.CellSize / 2.0);

            var rx = Math.Min(x1, x2);
            var ry = Math.Min(y1, y2);
            var rw = Math.Abs(x2 - x1);
            var rh = Math.Abs(y2 - y1);

            sb.AppendLine(
                $"<rect x='{S(rx)}' y='{S(ry)}' width='{S(rw)}' height='{S(rh)}'>" +
                $"<title>{H(cell.GeologicalBlock)} | {H(cell.BlastBlock)} | h={S(cell.CutHeight)} | V={S(cell.Volume)}</title>" +
                $"</rect>");
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
        sb.AppendLine("<g id='blast' fill='none' stroke='blue' stroke-width='2.4'>");
        foreach (var block in blastBlocks)
        {
            sb.AppendLine(Polygon(block.Contour, Tx, Ty, block.Name));
        }
        sb.AppendLine("</g>");

        // Производственный контур
        sb.AppendLine("<g id='production' fill='none' stroke='#00aa00' stroke-width='3.5'>");
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
        sb.AppendLine("<h2>Где находится «Без локальной площади»</h2>");
        sb.AppendLine("<table>");
        sb.AppendLine("<tr><th>Геоблок</th><th>П-блок</th><th>Точек</th><th>Площадь, м²</th><th>Объём вскрыши, м³</th><th>Отрицательный объём, м³</th><th>Баланс, м³</th></tr>");

        foreach (var row in summary)
        {
            sb.AppendLine(
                "<tr>" +
                $"<td>{H(row.GeologicalBlock)}</td>" +
                $"<td>{H(row.BlastBlock)}</td>" +
                $"<td>{row.Points:N0}</td>" +
                $"<td>{row.Area:N0}</td>" +
                $"<td>{row.ExcavationVolume:N0}</td>" +
                $"<td>{row.FillOrErrorVolume:N0}</td>" +
                $"<td>{row.NetVolume:N0}</td>" +
                "</tr>");
        }

        sb.AppendLine("</table>");
        sb.AppendLine("<p class='small'>Оранжевые ячейки — это зона, которая входит в производственный контур, но не входит ни в один контур uhodka.</p>");
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

    private static string S(double value)
    {
        return value.ToString("F3", CultureInfo.InvariantCulture);
    }

    private static string F(double value, int digits)
    {
        return value.ToString($"F{digits}", RuCulture);
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