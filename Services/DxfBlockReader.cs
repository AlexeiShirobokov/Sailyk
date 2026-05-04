using System.Globalization;
using System.Text.RegularExpressions;
using Sailyk.Utils;

namespace Sailyk.Services;

public enum BlockLabelKind
{
    Geological,
    Blast,
    Any
}

public static class DxfBlockReader
{
    public static List<WorkBlock> ReadBlocks(
        string dxfPath,
        string labelLayerName,
        string boundaryLayerName)
    {
        return ReadLabeledBlocks(
            dxfPath,
            labelLayerName,
            boundaryLayerName,
            BlockLabelKind.Geological);
    }

    public static List<WorkBlock> ReadLabeledBlocks(
        string dxfPath,
        string labelLayerName,
        string boundaryLayerName,
        BlockLabelKind labelKind,
        int? boundaryColorIndex = null)
    {
        var entities = ReadDxfEntities(dxfPath);

        var labels = entities.Texts
            .Where(t => IsLayer(t.Layer, labelLayerName))
            .Select(t => new
            {
                RawText = t,
                Name = ExtractBlockLabel(t.Value, labelKind)
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .Select(x => new BlockLabel(x.RawText, x.Name!))
            .ToList();

        var closedContours = entities.Polylines
            .Where(p => IsLayer(p.Layer, boundaryLayerName))
            .Where(p => p.IsClosed)
            .Where(p => MatchesColor(p.ColorIndex, boundaryColorIndex))
            .ToList();

        Console.WriteLine();
        Console.WriteLine($"=== ЧТЕНИЕ ПОДПИСАННЫХ КОНТУРОВ: {labelKind} ===");
        Console.WriteLine($"Слой подписей: {labelLayerName}");
        Console.WriteLine($"Слой контуров: {boundaryLayerName}");
        Console.WriteLine($"Фильтр цвета контуров: {(boundaryColorIndex is null ? "нет" : boundaryColorIndex.Value.ToString())}");
        Console.WriteLine($"Найдено подписей: {labels.Count}");
        Console.WriteLine($"Найдено замкнутых контуров: {closedContours.Count}");

        var result = new List<WorkBlock>();

        foreach (var label in labels)
        {
            var containingContours = closedContours
                .Where(p => IsPointInBoundingBox(label.RawText.X, label.RawText.Y, p))
                .Where(p => GeometryUtils.IsPointInsidePolygon(
                    label.RawText.X,
                    label.RawText.Y,
                    p.Points))
                .OrderBy(p => p.Area)
                .ToList();

            if (containingContours.Count == 0)
            {
                Console.WriteLine($"{label.Name}: контур НЕ найден");
                continue;
            }

            var contour = containingContours.First();

            Console.WriteLine(
                $"{label.Name}: контур найден, площадь {contour.Area:F0} м², слой {contour.Layer}");

            result.Add(new WorkBlock(
                Name: label.Name,
                Label: label.RawText,
                Contour: contour));
        }

        return result
            .GroupBy(b =>
            {
                var c = b.Contour.GetCentroid();

                return
                    $"{b.Name}|" +
                    $"{Math.Round(c.X, 3)}|" +
                    $"{Math.Round(c.Y, 3)}|" +
                    $"{Math.Round(b.Contour.Area, 3)}";
            })
            .Select(g => g.First())
            .ToList();
    }

    public static List<WorkBlock> ReadUnlabeledClosedContours(
        string dxfPath,
        string boundaryLayerName,
        string namePrefix,
        int? boundaryColorIndex = null)
    {
        var entities = ReadDxfEntities(dxfPath);

        var closedContours = entities.Polylines
            .Where(p => IsLayer(p.Layer, boundaryLayerName))
            .Where(p => p.IsClosed)
            .Where(p => MatchesColor(p.ColorIndex, boundaryColorIndex))
            .OrderByDescending(p => p.Area)
            .ToList();

        Console.WriteLine();
        Console.WriteLine($"=== ЧТЕНИЕ НЕПОДПИСАННЫХ КОНТУРОВ: {namePrefix} ===");
        Console.WriteLine($"Слой контуров: {boundaryLayerName}");
        Console.WriteLine($"Фильтр цвета контуров: {(boundaryColorIndex is null ? "нет" : boundaryColorIndex.Value.ToString())}");
        Console.WriteLine($"Найдено замкнутых контуров: {closedContours.Count}");

        var result = new List<WorkBlock>();

        for (var i = 0; i < closedContours.Count; i++)
        {
            var contour = closedContours[i];
            var name = $"{namePrefix}-{i + 1:000}";
            var center = contour.GetCentroid();

            var syntheticLabel = new DxfText(
                Type: "AUTO",
                Layer: contour.Layer,
                Value: name,
                X: center.X,
                Y: center.Y,
                ColorIndex: contour.ColorIndex);

            Console.WriteLine($"{name}: площадь {contour.Area:F0} м², слой {contour.Layer}");

            result.Add(new WorkBlock(
                Name: name,
                Label: syntheticLabel,
                Contour: contour));
        }

        return result;
    }

    public static void PrintLayerDiagnostics(string dxfPath)
    {
        var entities = ReadDxfEntities(dxfPath);

        var layerNames = entities.Texts
            .Select(t => t.Layer)
            .Concat(entities.Polylines.Select(p => p.Layer))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Console.WriteLine("=== ДИАГНОСТИКА DXF-СЛОЁВ ===");
        Console.WriteLine($"Всего TEXT/MTEXT: {entities.Texts.Count}");
        Console.WriteLine($"Всего LWPOLYLINE: {entities.Polylines.Count}");
        Console.WriteLine();

        foreach (var layer in layerNames)
        {
            var textCount = entities.Texts.Count(t => IsLayer(t.Layer, layer));
            var closedPolylineCount = entities.Polylines.Count(p => IsLayer(p.Layer, layer) && p.IsClosed);
            var openPolylineCount = entities.Polylines.Count(p => IsLayer(p.Layer, layer) && !p.IsClosed);

            var colors = entities.Polylines
                .Where(p => IsLayer(p.Layer, layer))
                .Select(p => p.ColorIndex)
                .Distinct()
                .Select(c => c is null ? "ByLayer/нет 62" : c.Value.ToString())
                .OrderBy(x => x)
                .ToList();

            Console.WriteLine(
                $"Слой: {layer}; " +
                $"текстов: {textCount}; " +
                $"замкнутых полилиний: {closedPolylineCount}; " +
                $"открытых полилиний: {openPolylineCount}; " +
                $"цвета: {string.Join(", ", colors)}");
        }
    }

    private static (List<DxfText> Texts, List<DxfPolyline> Polylines) ReadDxfEntities(string dxfPath)
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

        return (texts, polylines);
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
        var colorIndex = GetFirstInt(pairs, 62);

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

        return new DxfText(
            Type: type,
            Layer: layer,
            Value: value,
            X: x.Value,
            Y: y.Value,
            ColorIndex: colorIndex);
    }

    private static DxfPolyline? ParseLwPolyline(List<DxfPair> pairs)
    {
        var layer = GetFirstValue(pairs, 8) ?? "";
        var colorIndex = GetFirstInt(pairs, 62);

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

        return new DxfPolyline(
            layer,
            isClosed,
            points,
            colorIndex);
    }

    private static string? GetFirstValue(List<DxfPair> pairs, int code)
    {
        return pairs.FirstOrDefault(p => p.Code == code)?.Value;
    }

    private static double? GetFirstDouble(List<DxfPair> pairs, int code)
    {
        var value = GetFirstValue(pairs, code);

        return value is null
            ? null
            : ParseDouble(value);
    }

    private static int? GetFirstInt(List<DxfPair> pairs, int code)
    {
        var value = GetFirstValue(pairs, code);

        if (value is null)
            return null;

        return int.TryParse(value, out var result)
            ? result
            : null;
    }

    private static double? ParseDouble(string value)
    {
        return double.TryParse(
            value,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var result)
            ? result
            : null;
    }

    private static bool IsLayer(string actualLayer, string expectedLayer)
    {
        if (string.IsNullOrWhiteSpace(expectedLayer) || expectedLayer.Trim() == "*")
            return true;

        return string.Equals(
            actualLayer.Trim(),
            expectedLayer.Trim(),
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesColor(int? actualColorIndex, int? expectedColorIndex)
    {
        if (expectedColorIndex is null)
            return true;

        return actualColorIndex == expectedColorIndex;
    }

    private static bool IsPointInBoundingBox(double x, double y, DxfPolyline polygon)
    {
        return
            x >= polygon.MinX &&
            x <= polygon.MaxX &&
            y >= polygon.MinY &&
            y <= polygon.MaxY;
    }

    private static string? ExtractBlockLabel(string text, BlockLabelKind labelKind)
    {
        var normalized = NormalizeDxfText(text);

        return labelKind switch
        {
            BlockLabelKind.Geological => ExtractGeologicalLabel(normalized),
            BlockLabelKind.Blast => ExtractBlastLabel(normalized),
            BlockLabelKind.Any => ExtractGeologicalLabel(normalized)
                                  ?? ExtractBlastLabel(normalized)
                                  ?? normalized,
            _ => null
        };
    }

    private static string? ExtractGeologicalLabel(string normalized)
    {
        var match = Regex.Match(normalized, @"[CС]\s*1\s*-\s*\d+");

        if (!match.Success)
            return null;

        return match.Value
            .Replace(" ", "")
            .Replace("С", "C");
    }

    private static string? ExtractBlastLabel(string normalized)
    {
        var match = Regex.Match(normalized, @"[ПP]\s*-\s*\d+");

        if (!match.Success)
            return null;

        var numberMatch = Regex.Match(match.Value, @"\d+");

        if (!numberMatch.Success)
            return null;

        return $"П-{numberMatch.Value}";
    }

    private static string NormalizeDxfText(string text)
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
        normalized = Regex.Replace(normalized, @"\s+", " ");

        return normalized.Trim();
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