using System.Globalization;
using Sailyk.Utils;

namespace Sailyk.Services;

public static class SlopeVisualizer
{
    public static void ExportSlopeMapSvg(
        WorkBlock block,
        TinInterpolator projectTin,
        double step,
        double slopeAngleThresholdDegrees,
        string outputPath)
    {
        var contour = block.Contour;
        var samples = new List<SlopeSample>();

        for (var x = contour.MinX + step / 2.0; x <= contour.MaxX; x += step)
        {
            for (var y = contour.MinY + step / 2.0; y <= contour.MaxY; y += step)
            {
                if (!GeometryUtils.IsPointInsidePolygon(x, y, contour.Points))
                    continue;

                var slope = CalculateSlope(projectTin, x, y, step);

                if (slope is null)
                    continue;

                samples.Add(new SlopeSample(x, y, slope.Value));
            }
        }

        if (samples.Count == 0)
        {
            File.WriteAllText(
                outputPath,
                $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"900\" height=\"300\">" +
                $"<text x=\"20\" y=\"40\" font-size=\"20\">Нет данных по уклонам для блока {block.Name}</text>" +
                $"</svg>"
            );

            return;
        }

        const int width = 1200;
        const int height = 900;
        const int margin = 70;

        var mapWidth = width - margin * 2;
        var mapHeight = height - margin * 2;

        var dx = contour.MaxX - contour.MinX;
        var dy = contour.MaxY - contour.MinY;

        var scale = Math.Min(mapWidth / dx, mapHeight / dy);

        double ToSvgX(double x) => margin + (x - contour.MinX) * scale;
        double ToSvgY(double y) => height - margin - (y - contour.MinY) * scale;

        var slopeSamples = samples
            .Where(s => s.AngleDegrees >= slopeAngleThresholdDegrees)
            .ToList();

        var slopePlanArea = slopeSamples.Count * step * step;

        var avgSlope = slopeSamples.Count > 0
            ? slopeSamples.Average(s => s.AngleDegrees)
            : 0;

        var maxSlope = samples.Max(s => s.AngleDegrees);

        var svg = new List<string>();

        svg.Add($"""
<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" viewBox="0 0 {width} {height}">
<rect x="0" y="0" width="{width}" height="{height}" fill="white"/>
<text x="40" y="35" font-size="24" font-family="Arial" font-weight="bold">Блок {Escape(block.Name)} — карта откосов</text>
<text x="40" y="62" font-size="15" font-family="Arial">Откос = уклон проектной поверхности больше {Format(slopeAngleThresholdDegrees)}°. Шаг сетки: {Format(step)} м</text>
""");

        var cellSize = step * scale;

        foreach (var sample in samples)
        {
            var sx = ToSvgX(sample.X) - cellSize / 2.0;
            var sy = ToSvgY(sample.Y) - cellSize / 2.0;

            var color = GetSlopeColor(sample.AngleDegrees, slopeAngleThresholdDegrees);

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
<text x="{Format(centerX)}" y="{Format(centerY)}" text-anchor="middle" font-size="30" font-family="Arial" font-weight="bold" fill="black">{Escape(block.Name)}</text>
""");

        var infoX = 40;
        var infoY = height - 175;

        svg.Add($"""
<rect x="{infoX}" y="{infoY}" width="560" height="130" fill="white" stroke="black" stroke-width="1"/>
<text x="{infoX + 15}" y="{infoY + 28}" font-size="16" font-family="Arial">Площадь блока в плане: {Format(block.Contour.Area)} м²</text>
<text x="{infoX + 15}" y="{infoY + 52}" font-size="16" font-family="Arial">Площадь откосов в плане: {Format(slopePlanArea)} м²</text>
<text x="{infoX + 15}" y="{infoY + 76}" font-size="16" font-family="Arial">Доля откосов: {Format(slopePlanArea / block.Contour.Area * 100.0)}%</text>
<text x="{infoX + 15}" y="{infoY + 100}" font-size="16" font-family="Arial">Средний уклон откосов: {Format(avgSlope)}° | Максимальный уклон: {Format(maxSlope)}°</text>
""");

        var legendX = width - 350;
        var legendY = height - 175;

        svg.Add($"""
<rect x="{legendX}" y="{legendY}" width="300" height="130" fill="white" stroke="black" stroke-width="1"/>
<text x="{legendX + 15}" y="{legendY + 25}" font-size="16" font-family="Arial" font-weight="bold">Легенда уклона</text>

<rect x="{legendX + 15}" y="{legendY + 42}" width="30" height="18" fill="#E6E6E6"/>
<text x="{legendX + 55}" y="{legendY + 57}" font-size="14" font-family="Arial">не откос</text>

<rect x="{legendX + 15}" y="{legendY + 65}" width="30" height="18" fill="#FEE391"/>
<text x="{legendX + 55}" y="{legendY + 80}" font-size="14" font-family="Arial">слабый откос</text>

<rect x="{legendX + 15}" y="{legendY + 88}" width="30" height="18" fill="#FE9929"/>
<text x="{legendX + 55}" y="{legendY + 103}" font-size="14" font-family="Arial">средний откос</text>

<rect x="{legendX + 170}" y="{legendY + 65}" width="30" height="18" fill="#CC4C02"/>
<text x="{legendX + 210}" y="{legendY + 80}" font-size="14" font-family="Arial">крутой откос</text>

<rect x="{legendX + 170}" y="{legendY + 88}" width="30" height="18" fill="#7F2704"/>
<text x="{legendX + 210}" y="{legendY + 103}" font-size="14" font-family="Arial">очень крутой</text>
""");

        svg.Add("</svg>");

        File.WriteAllText(outputPath, string.Join(Environment.NewLine, svg));
    }

    private static double? CalculateSlope(
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

        return angleDegrees;
    }

    private static string GetSlopeColor(double angleDegrees, double threshold)
    {
        if (angleDegrees < threshold)
            return "#E6E6E6";

        if (angleDegrees < threshold + 5.0)
            return "#FEE391";

        if (angleDegrees < threshold + 10.0)
            return "#FE9929";

        if (angleDegrees < threshold + 20.0)
            return "#CC4C02";

        return "#7F2704";
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

    private sealed record SlopeSample(double X, double Y, double AngleDegrees);
}