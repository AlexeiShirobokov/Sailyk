using System.Globalization;
using Sailyk.Utils;

namespace Sailyk.Services;

public static class BlockVisualizer
{
    public static void ExportDzMapSvg(
        WorkBlock block,
        TinInterpolator projectTin,
        TinInterpolator factTin,
        double step,
        string outputPath)
    {
        var samples = new List<DzSample>();

        var contour = block.Contour;

        for (var x = contour.MinX + step / 2.0; x <= contour.MaxX; x += step)
        {
            for (var y = contour.MinY + step / 2.0; y <= contour.MaxY; y += step)
            {
                if (!GeometryUtils.IsPointInsidePolygon(x, y, contour.Points))
                    continue;

                var projectZ = projectTin.GetZ(x, y);
                var factZ = factTin.GetZ(x, y);

                if (projectZ is null || factZ is null)
                    continue;

                var dz = factZ.Value - projectZ.Value;

                samples.Add(new DzSample(x, y, dz));
            }
        }

        if (samples.Count == 0)
        {
            File.WriteAllText(
                outputPath,
                $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"800\" height=\"300\">" +
                $"<text x=\"20\" y=\"40\" font-size=\"20\">Нет данных для блока {block.Name}</text>" +
                $"</svg>"
            );

            return;
        }

        const int width = 1100;
        const int height = 900;
        const int margin = 70;

        var mapWidth = width - margin * 2;
        var mapHeight = height - margin * 2;

        var dx = contour.MaxX - contour.MinX;
        var dy = contour.MaxY - contour.MinY;

        var scale = Math.Min(mapWidth / dx, mapHeight / dy);

        double ToSvgX(double x) => margin + (x - contour.MinX) * scale;
        double ToSvgY(double y) => height - margin - (y - contour.MinY) * scale;

        var minDz = samples.Min(s => s.Dz);
        var maxDz = samples.Max(s => s.Dz);
        var avgDz = samples.Average(s => s.Dz);

        var positiveVolume = samples
            .Where(s => s.Dz > 0)
            .Sum(s => s.Dz * step * step);

        var negativeVolume = samples
            .Where(s => s.Dz < 0)
            .Sum(s => s.Dz * step * step);

        var balance = positiveVolume + negativeVolume;

        var svg = new List<string>();

        svg.Add($"""
<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" viewBox="0 0 {width} {height}">
<rect x="0" y="0" width="{width}" height="{height}" fill="white"/>
<text x="40" y="35" font-size="24" font-family="Arial" font-weight="bold">Блок {Escape(block.Name)} — карта dZ = Zфакт - Zпроект</text>
<text x="40" y="62" font-size="15" font-family="Arial">Красный: факт выше проекта / недоработка. Синий: факт ниже проекта / переработка. Шаг сетки: {Format(step)} м</text>
""");

        var cellSize = step * scale;

        foreach (var sample in samples)
        {
            var sx = ToSvgX(sample.X) - cellSize / 2.0;
            var sy = ToSvgY(sample.Y) - cellSize / 2.0;

            var color = GetColorForDz(sample.Dz);

            svg.Add(
                $"<rect x=\"{Format(sx)}\" y=\"{Format(sy)}\" width=\"{Format(cellSize)}\" height=\"{Format(cellSize)}\" fill=\"{color}\" stroke=\"none\"/>"
            );
        }

        var contourPoints = string.Join(
            " ",
            contour.Points.Select(p => $"{Format(ToSvgX(p.X))},{Format(ToSvgY(p.Y))}")
        );

        svg.Add($"""
<polygon points="{contourPoints}" fill="none" stroke="black" stroke-width="3"/>
""");

        var centerX = ToSvgX((contour.MinX + contour.MaxX) / 2.0);
        var centerY = ToSvgY((contour.MinY + contour.MaxY) / 2.0);

        svg.Add($"""
<text x="{Format(centerX)}" y="{Format(centerY)}" text-anchor="middle" font-size="28" font-family="Arial" font-weight="bold" fill="black">{Escape(block.Name)}</text>
""");

        var infoX = 40;
        var infoY = height - 170;

        svg.Add($"""
<rect x="{infoX}" y="{infoY}" width="520" height="125" fill="white" stroke="black" stroke-width="1"/>
<text x="{infoX + 15}" y="{infoY + 28}" font-size="16" font-family="Arial">Площадь контура: {Format(block.Contour.Area)} м²</text>
<text x="{infoX + 15}" y="{infoY + 52}" font-size="16" font-family="Arial">Среднее dZ: {Format(avgDz)} м</text>
<text x="{infoX + 15}" y="{infoY + 76}" font-size="16" font-family="Arial">Min dZ: {Format(minDz)} м | Max dZ: {Format(maxDz)} м</text>
<text x="{infoX + 15}" y="{infoY + 100}" font-size="16" font-family="Arial">Факт выше проекта: {Format(positiveVolume)} м³ | Факт ниже проекта: {Format(negativeVolume)} м³ | Баланс: {Format(balance)} м³</text>
""");

        var legendX = width - 310;
        var legendY = height - 170;

        svg.Add($"""
<rect x="{legendX}" y="{legendY}" width="260" height="125" fill="white" stroke="black" stroke-width="1"/>
<text x="{legendX + 15}" y="{legendY + 25}" font-size="16" font-family="Arial" font-weight="bold">Легенда dZ</text>
<rect x="{legendX + 15}" y="{legendY + 42}" width="30" height="18" fill="#08306B"/><text x="{legendX + 55}" y="{legendY + 57}" font-size="14" font-family="Arial">меньше -5 м</text>
<rect x="{legendX + 15}" y="{legendY + 65}" width="30" height="18" fill="#6BAED6"/><text x="{legendX + 55}" y="{legendY + 80}" font-size="14" font-family="Arial">-5 ... -1 м</text>
<rect x="{legendX + 15}" y="{legendY + 88}" width="30" height="18" fill="#EEEEEE"/><text x="{legendX + 55}" y="{legendY + 103}" font-size="14" font-family="Arial">около проекта</text>
<rect x="{legendX + 145}" y="{legendY + 42}" width="30" height="18" fill="#FDAE6B"/><text x="{legendX + 185}" y="{legendY + 57}" font-size="14" font-family="Arial">+1 ... +5 м</text>
<rect x="{legendX + 145}" y="{legendY + 65}" width="30" height="18" fill="#A50F15"/><text x="{legendX + 185}" y="{legendY + 80}" font-size="14" font-family="Arial">больше +5 м</text>
""");

        svg.Add("</svg>");

        File.WriteAllText(outputPath, string.Join(Environment.NewLine, svg));
    }

    private static string GetColorForDz(double dz)
    {
        if (dz <= -5.0)
            return "#08306B";

        if (dz <= -1.0)
            return "#6BAED6";

        if (dz < 1.0)
            return "#EEEEEE";

        if (dz < 5.0)
            return "#FDAE6B";

        return "#A50F15";
    }

    private static string Format(double value)
    {
        return value.ToString("F3", CultureInfo.InvariantCulture);
    }

    private static string Escape(string text)
    {
        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");
    }

    private sealed record DzSample(double X, double Y, double Dz);
}