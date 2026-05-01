using System.Globalization;
using System.Xml.Linq;

namespace Sailyk.Services;

public static class LandXmlSurfaceReader
{
    public static TinSurface ReadSurfaceByIndex(string filePath, int surfaceNumber)
    {
        var document = XDocument.Load(filePath);

        var surfaces = document
            .Descendants()
            .Where(e => e.Name.LocalName == "Surface")
            .ToList();

        if (surfaceNumber < 1 || surfaceNumber > surfaces.Count)
            throw new InvalidOperationException(
                $"Поверхность №{surfaceNumber} не найдена. Всего поверхностей: {surfaces.Count}");

        var surfaceElement = surfaces[surfaceNumber - 1];

        var surfaceName = surfaceElement.Attribute("name")?.Value ?? $"Surface {surfaceNumber}";

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
}