using System.Globalization;
using System.Text.RegularExpressions;
using Sailyk.Utils;

namespace Sailyk.Services;

public static class DxfBlockReader
{
    public static List<WorkBlock> ReadBlocks(
        string dxfPath,
        string labelLayerName,
        string boundaryLayerName)
    {
        var pairs = ReadDxfPairs(dxfPath);

        var texts = new List<DxfText>();
        var polylines = new List<DxfPolyline>();

        for (var i = 0; i < pairs.Count; i++)
        {
            var pair = pairs[i];

            if (pair.Code != 0)
                continue;

            if (pair.Value == "TEXT")
            {
                var entityPairs = ReadEntityPairs(pairs, ref i);
                var text = ParseText(entityPairs, "TEXT");

                if (text is not null)
                    texts.Add(text);
            }
            else if (pair.Value == "MTEXT")
            {
                var entityPairs = ReadEntityPairs(pairs, ref i);
                var text = ParseText(entityPairs, "MTEXT");

                if (text is not null)
                    texts.Add(text);
            }
            else if (pair.Value == "LWPOLYLINE")
            {
                var entityPairs = ReadEntityPairs(pairs, ref i);
                var polyline = ParseLwPolyline(entityPairs);

                if (polyline is not null)
                    polylines.Add(polyline);
            }
        }

        var labels = texts
            .Where(t => IsLayer(t.Layer, labelLayerName))
            .Select(t => new BlockLabel(
                RawText: t,
                Name: ExtractBlockLabel(t.Value)
            ))
            .Where(x => x.Name is not null)
            .Select(x => new BlockLabel(x.RawText, x.Name!))
            .ToList();

        var closedContours = polylines
            .Where(p => IsLayer(p.Layer, boundaryLayerName))
            .Where(p => p.IsClosed)
            .ToList();

        Console.WriteLine();
        Console.WriteLine("=== ДИАГНОСТИКА DXF ===");
        Console.WriteLine($"Всего TEXT/MTEXT: {texts.Count}");
        Console.WriteLine($"Метки на слое {labelLayerName}: {labels.Count}");
        Console.WriteLine($"Всего LWPOLYLINE: {polylines.Count}");
        Console.WriteLine($"Замкнутые контуры на слое {boundaryLayerName}: {closedContours.Count}");
        Console.WriteLine();

        var result = new List<WorkBlock>();

        foreach (var label in labels)
        {
            var x = label.RawText.X;
            var y = label.RawText.Y;

            var containingContours = closedContours
                .Where(p => GeometryUtils.IsPointInsidePolygon(x, y, p.Points))
                .OrderBy(p => p.Area)
                .ToList();

            if (containingContours.Count == 0)
            {
                Console.WriteLine($"{label.Name}: контур НЕ найден");
                continue;
            }

            var contour = containingContours.First();

            Console.WriteLine($"{label.Name}: контур найден, площадь {contour.Area:F0} м²");

            result.Add(new WorkBlock(
                Name: label.Name,
                Label: label.RawText,
                Contour: contour
            ));
        }

        Console.WriteLine();

        return result
            .GroupBy(b => b.Name)
            .Select(g => g.First())
            .ToList();
    }

    private static List<DxfPair> ReadDxfPairs(string path)
    {
        var lines = File.ReadAllLines(path);
        var result = new List<DxfPair>();

        for (var i = 0; i < lines.Length - 1; i += 2)
        {
            var codeText = lines[i].Trim();
            var value = lines[i + 1].Trim();

            if (!int.TryParse(codeText, out var code))
                continue;

            result.Add(new DxfPair(code, value));
        }

        return result;
    }

    private static List<DxfPair> ReadEntityPairs(List<DxfPair> pairs, ref int index)
    {
        var entityPairs = new List<DxfPair>();

        index++;

        while (index < pairs.Count)
        {
            if (pairs[index].Code == 0)
            {
                index--;
                break;
            }

            entityPairs.Add(pairs[index]);
            index++;
        }

        return entityPairs;
    }

    private static DxfText? ParseText(List<DxfPair> pairs, string type)
    {
        var layer = GetFirstValue(pairs, 8) ?? "";
        var x = GetFirstDouble(pairs, 10);
        var y = GetFirstDouble(pairs, 20);

        var valueParts = pairs
            .Where(p => p.Code == 1 || p.Code == 3)
            .Select(p => p.Value)
            .ToList();

        var value = string.Join("", valueParts)
            .Replace("\\P", " ")
            .Trim();

        value = DecodeDxfUnicode(value);

        if (x is null || y is null || string.IsNullOrWhiteSpace(value))
            return null;

        return new DxfText(type, layer, value, x.Value, y.Value);
    }

    private static DxfPolyline? ParseLwPolyline(List<DxfPair> pairs)
    {
        var layer = GetFirstValue(pairs, 8) ?? "";
        var flags = GetFirstInt(pairs, 70) ?? 0;
        var isClosed = (flags & 1) == 1;

        var points = new List<DxfPoint>();
        double? currentX = null;

        foreach (var pair in pairs)
        {
            if (pair.Code == 10)
            {
                currentX = ParseDouble(pair.Value);
            }
            else if (pair.Code == 20 && currentX is not null)
            {
                var y = ParseDouble(pair.Value);

                if (y is not null)
                    points.Add(new DxfPoint(currentX.Value, y.Value));

                currentX = null;
            }
        }

        if (points.Count < 3)
            return null;

        return new DxfPolyline(layer, isClosed, points);
    }

    private static string? GetFirstValue(List<DxfPair> pairs, int code)
    {
        return pairs.FirstOrDefault(p => p.Code == code)?.Value;
    }

    private static double? GetFirstDouble(List<DxfPair> pairs, int code)
    {
        var value = GetFirstValue(pairs, code);
        return value is null ? null : ParseDouble(value);
    }

    private static int? GetFirstInt(List<DxfPair> pairs, int code)
    {
        var value = GetFirstValue(pairs, code);

        if (value is null)
            return null;

        return int.TryParse(value, out var result) ? result : null;
    }

    private static double? ParseDouble(string value)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
    }

    private static bool IsLayer(string actualLayer, string expectedLayer)
    {
        return string.Equals(
            actualLayer.Trim(),
            expectedLayer.Trim(),
            StringComparison.OrdinalIgnoreCase
        );
    }

    private static string? ExtractBlockLabel(string text)
    {
        var normalized = text
            .Replace("\\P", " ")
            .Replace("–", "-")
            .Replace("—", "-")
            .Replace("−", "-")
            .ToUpperInvariant();

        normalized = DecodeDxfUnicode(normalized);

        normalized = Regex.Replace(normalized, @"\\[A-Z][^;{}]*;", "");
        normalized = normalized.Replace("{", "").Replace("}", "");

        var match = Regex.Match(normalized, @"[CС]\s*1\s*-\s*\d+");

        if (!match.Success)
            return null;

        var label = match.Value
            .Replace(" ", "")
            .Replace("С", "C");

        return label;
    }

    private static string DecodeDxfUnicode(string text)
    {
        return Regex.Replace(
            text,
            @"\\U\+([0-9A-Fa-f]{4})",
            match =>
            {
                var hex = match.Groups[1].Value;
                var code = int.Parse(hex, NumberStyles.HexNumber);
                return char.ConvertFromUtf32(code);
            });
    }
}