using System.Globalization;
using System.Text;

namespace Sailyk.Services;

public static class CsvReportWriter
{
    private static readonly CultureInfo RuCulture = CultureInfo.GetCultureInfo("ru-RU");

    public static void WriteDetailed(
        string filePath,
        IEnumerable<VolumeBreakdownResult> results)
    {
        var lines = new List<string>
        {
            string.Join(";", new[]
            {
                "Геологический блок",
                "Взрывной блок",
                "Локальная площадь",
                "Точек сетки",
                "Площадь, м2",
                "Средняя мощность вскрыши, м",
                "Мин. мощность, м",
                "Макс. мощность, м",
                "Объем вскрыши, м3",
                "Отрицательный объем, м3",
                "Баланс, м3"
            })
        };

        foreach (var row in results)
        {
            lines.Add(string.Join(";", new[]
            {
                Csv(row.Key.GeologicalBlock),
                Csv(row.Key.BlastBlock),
                Csv(row.Key.LocalArea),
                row.GridPoints.ToString(CultureInfo.InvariantCulture),
                F(row.Area, 0),
                F(row.AverageCutHeight, 3),
                F(row.MinCutHeight, 3),
                F(row.MaxCutHeight, 3),
                F(row.ExcavationVolume, 0),
                F(row.FillOrErrorVolume, 0),
                F(row.NetVolume, 0)
            }));
        }

        File.WriteAllLines(
            filePath,
            lines,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    public static void WriteSummary(
        string filePath,
        IEnumerable<VolumeSummaryRow> rows,
        string groupColumnName)
    {
        var lines = new List<string>
        {
            string.Join(";", new[]
            {
                groupColumnName,
                "Площадь, м2",
                "Объем вскрыши, м3",
                "Отрицательный объем, м3",
                "Баланс, м3"
            })
        };

        foreach (var row in rows)
        {
            lines.Add(string.Join(";", new[]
            {
                Csv(row.GroupName),
                F(row.Area, 0),
                F(row.ExcavationVolume, 0),
                F(row.FillOrErrorVolume, 0),
                F(row.NetVolume, 0)
            }));
        }

        File.WriteAllLines(
            filePath,
            lines,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private static string F(double value, int digits)
    {
        return value.ToString($"F{digits}", RuCulture);
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