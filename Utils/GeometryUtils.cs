namespace Sailyk.Utils;

public static class GeometryUtils
{
    public static bool IsPointInsidePolygon(double x, double y, List<DxfPoint> polygon)
    {
        var inside = false;

        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            var xi = polygon[i].X;
            var yi = polygon[i].Y;
            var xj = polygon[j].X;
            var yj = polygon[j].Y;

            var intersect =
                ((yi > y) != (yj > y)) &&
                (x < (xj - xi) * (y - yi) / ((yj - yi) + 0.0000000001) + xi);

            if (intersect)
                inside = !inside;
        }

        return inside;
    }
}