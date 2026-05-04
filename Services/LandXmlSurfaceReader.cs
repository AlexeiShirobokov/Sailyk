using System.Globalization;
using System.Xml.Linq;

namespace Sailyk.Services;

public static class LandXmlSurfaceReader
{
    public static IReadOnlyList<string> ListSurfaceNames(string filePath)
    {
        var document = XDocument.Load(filePath);

        return document
            .Descendants()
            .Where(e => e.Name.LocalName == "Surface")
            .Select((e, i) => e.Attribute("name")?.Value ?? $"Surface {i + 1}")
            .ToList();
    }

    public static TinSurface ReadFirstSurface(string filePath)
    {
        var document = XDocument.Load(filePath);

        var surfaceElement = document
            .Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "Surface");

        if (surfaceElement is null)
            throw new InvalidOperationException($"В файле {filePath} не найдено ни одной поверхности.");

        return ReadSurfaceFromElement(surfaceElement, "Surface 1");
    }

    public static TinSurface ReadSurfaceByIndex(string filePath, int surfaceNumber)
    {
        var document = XDocument.Load(filePath);

        var surfaces = document
            .Descendants()
            .Where(e => e.Name.LocalName == "Surface")
            .ToList();

        if (surfaceNumber < 1 || surfaceNumber > surfaces.Count)
        {
            throw new InvalidOperationException(
                $"Поверхность №{surfaceNumber} не найдена. Всего поверхностей: {surfaces.Count}");
        }

        return ReadSurfaceFromElement(
            surfaces[surfaceNumber - 1],
            $"Surface {surfaceNumber}");
    }

    public static TinSurface ReadSurfaceByName(string filePath, string surfaceName)
    {
        var document = XDocument.Load(filePath);

        var surfaces = document
            .Descendants()
            .Where(e => e.Name.LocalName == "Surface")
            .ToList();

        var normalizedTarget = NormalizeName(surfaceName);

        var surfaceElement = surfaces.FirstOrDefault(e =>
            string.Equals(
                NormalizeName(e.Attribute("name")?.Value ?? ""),
                normalizedTarget,
                StringComparison.OrdinalIgnoreCase));

        surfaceElement ??= surfaces.FirstOrDefault(e =>
            NormalizeName(e.Attribute("name")?.Value ?? "")
                .Contains(normalizedTarget, StringComparison.OrdinalIgnoreCase));

        if (surfaceElement is null)
        {
            var available = surfaces
                .Select((e, i) => $"{i + 1}. {e.Attribute("name")?.Value ?? $"Surface {i + 1}"}")
                .ToList();

            throw new InvalidOperationException(
                $"Поверхность '{surfaceName}' не найдена в файле {filePath}." +
                Environment.NewLine +
                "Доступные поверхности:" +
                Environment.NewLine +
                string.Join(Environment.NewLine, available));
        }

        return ReadSurfaceFromElement(surfaceElement, surfaceName);
    }

    private static TinSurface ReadSurfaceFromElement(
        XElement surfaceElement,
        string fallbackName)
    {
        var surfaceName = surfaceElement.Attribute("name")?.Value ?? fallbackName;

        var points = surfaceElement
            .Descendants()
            .Where(e => e.Name.LocalName == "P")
            .Select(ParsePoint)
            .Where(p => p is not null)
            .Select(p => p!)
            .ToList();

        var triangles = surfaceElement
            .Descendants()
            .Where(e => e.Name.LocalName == "F")
            .Select(ParseTriangle)
            .Where(t => t is not null)
            .Select(t => t!)
            .ToList();

        if (points.Count == 0)
            throw new InvalidOperationException($"Поверхность '{surfaceName}' не содержит точек.");

        if (triangles.Count == 0)
            throw new InvalidOperationException($"Поверхность '{surfaceName}' не содержит треугольников.");

        return new TinSurface
        {
            Name = surfaceName,
            Points = points,
            Triangles = triangles
        };
    }

    private static TinPoint? ParsePoint(XElement pointElement)
    {
        var idText = pointElement.Attribute("id")?.Value;

        if (!int.TryParse(idText, out var id))
            return null;

        var values = pointElement.Value
            .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(x => double.Parse(x, CultureInfo.InvariantCulture))
            .ToList();

        if (values.Count < 3)
            return null;

        // В LandXML обычно порядок координат такой:
        // Y X Z
        // Поэтому X = values[1], Y = values[0].
        return new TinPoint
        {
            Id = id,
            X = values[1],
            Y = values[0],
            Z = values[2]
        };
    }

    private static TinTriangle? ParseTriangle(XElement faceElement)
    {
        var ids = faceElement.Value
            .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(x => int.TryParse(x, out var value) ? value : -1)
            .Where(x => x > 0)
            .ToList();

        if (ids.Count < 3)
            return null;

        return new TinTriangle
        {
            P1 = ids[0],
            P2 = ids[1],
            P3 = ids[2]
        };
    }

    private static string NormalizeName(string value)
    {
        return value
            .Replace('\u00A0', ' ')
            .Trim();
    }
}