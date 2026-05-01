using System.Text;
using Sailyk;
using Sailyk.Services;


Console.OutputEncoding = Encoding.UTF8;

const string LabelLayerName = "Blocklabel";
const string BoundaryLayerName = "BLOCK_BOUNDARIES";

const int ProjectSurfaceIndex = 37;
const int FactSurfaceIndex = 1;

const double GridStep = 5.0;
const string BlockNameForMap = "C1-59";
const double MapGridStep = 5.0;

//откосы
const string SlopeBlockName = "C1-59";
const double SlopeAngleThresholdDegrees = 10.0;
const double SlopeGridStep = 5.0;
//показать откосы
const string SlopeMapBlockName = "C1-59";
const double SlopeMapGridStep = 5.0;



var dataDir = Path.Combine(AppContext.BaseDirectory, "Data");

var dxfPath = Path.Combine(dataDir, "blocks.dxf");
var projectPath = Path.Combine(dataDir, "project_surface.xml");
var factPath = Path.Combine(dataDir, "fact_surface.xml");

Console.WriteLine("=== ФАЙЛЫ ===");
Console.WriteLine($"DXF:     {dxfPath}");
Console.WriteLine($"Project: {projectPath}");
Console.WriteLine($"Fact:    {factPath}");
Console.WriteLine();

if (!File.Exists(dxfPath))
{
    Console.WriteLine("blocks.dxf НЕ НАЙДЕН");
    return;
}

if (!File.Exists(projectPath))
{
    Console.WriteLine("project_surface.xml НЕ НАЙДЕН");
    return;
}

if (!File.Exists(factPath))
{
    Console.WriteLine("fact_surface.xml НЕ НАЙДЕН");
    return;
}

Console.WriteLine("Читаем DXF...");
var blocks = DxfBlockReader.ReadBlocks(dxfPath, LabelLayerName, BoundaryLayerName);

Console.WriteLine($"Найдено блоков с контурами: {blocks.Count}");
foreach (var block in blocks)
{
    Console.WriteLine($"{block.Name}: площадь контура {block.Contour.Area:F0} м², вершин {block.Contour.Points.Count}");
}

Console.WriteLine();
Console.WriteLine("Читаем поверхности LandXML...");

var projectSurface = LandXmlSurfaceReader.ReadSurfaceByIndex(projectPath, ProjectSurfaceIndex);
var factSurface = LandXmlSurfaceReader.ReadSurfaceByIndex(factPath, FactSurfaceIndex);

PrintSurfaceInfo("ПРОЕКТ", projectSurface);
PrintSurfaceInfo("ФАКТ", factSurface);

Console.WriteLine();
Console.WriteLine("Строим индексы TIN...");
var projectTin = new TinInterpolator(projectSurface);
var factTin = new TinInterpolator(factSurface);

var slopeMapBlock = blocks
    .FirstOrDefault(b => string.Equals(b.Name, SlopeMapBlockName, StringComparison.OrdinalIgnoreCase));

if (slopeMapBlock is not null)
{
    var slopeSvgPath = Path.Combine(dataDir, $"block_{slopeMapBlock.Name}_slopes.svg");

    SlopeVisualizer.ExportSlopeMapSvg(
        slopeMapBlock,
        projectTin,
        SlopeMapGridStep,
        SlopeAngleThresholdDegrees,
        slopeSvgPath
    );

    Console.WriteLine();
    Console.WriteLine($"Карта откосов блока {slopeMapBlock.Name} сохранена:");
    Console.WriteLine(slopeSvgPath);
}
else
{
    Console.WriteLine($"Блок {SlopeMapBlockName} для карты откосов не найден.");
}


Console.WriteLine();
Console.WriteLine("=== РАСЧЁТ ПО БЛОКАМ ===");
Console.WriteLine($"Шаг сетки: {GridStep} м");
Console.WriteLine();

foreach (var block in blocks.OrderBy(b => b.Name))
{
    var result = BlockAnalyzer.AnalyzeBlock(
        block,
        projectTin,
        factTin,
        GridStep
    );

    PrintBlockResult(block, result);
}

var slopeBlock = blocks
    .FirstOrDefault(b => string.Equals(b.Name, SlopeBlockName, StringComparison.OrdinalIgnoreCase));

if (slopeBlock is not null)
{
    var slopeResult = SlopeAnalyzer.AnalyzeSlopes(
        slopeBlock,
        projectTin,
        factTin,
        SlopeGridStep,
        SlopeAngleThresholdDegrees
    );

    Console.WriteLine();
    Console.WriteLine($"=== ОТКОСЫ ПО БЛОКУ {slopeBlock.Name} ===");
    Console.WriteLine($"Порог уклона: {SlopeAngleThresholdDegrees:F1}°");
    Console.WriteLine($"Шаг сетки: {SlopeGridStep:F1} м");
    Console.WriteLine($"Точек внутри блока: {slopeResult.PointsInsideBlock}");
    Console.WriteLine($"Точек откоса: {slopeResult.SlopePoints}");
    Console.WriteLine($"Площадь откосов в плане: {slopeResult.SlopePlanArea:F0} м²");
    Console.WriteLine($"Приближённая площадь поверхности откосов: {slopeResult.SlopeSurfaceArea:F0} м²");
    Console.WriteLine($"Средний уклон откосов: {slopeResult.AverageSlopeAngle:F2}°");
    Console.WriteLine($"Среднее dZ на откосах: {slopeResult.AverageDz:F3} м");
    Console.WriteLine($"Факт выше проекта на откосах: {slopeResult.PositiveVolume:F0} м³");
    Console.WriteLine($"Факт ниже проекта на откосах: {slopeResult.NegativeVolume:F0} м³");
    Console.WriteLine($"Баланс по откосам: {slopeResult.TotalVolume:F0} м³");
}
else
{
    Console.WriteLine($"Блок {SlopeBlockName} для анализа откосов не найден.");
}



var blockForMap = blocks
    .FirstOrDefault(b => string.Equals(b.Name, BlockNameForMap, StringComparison.OrdinalIgnoreCase));

if (blockForMap is not null)
{
    var svgPath = Path.Combine(dataDir, $"block_{blockForMap.Name}_dz_map.svg");

    BlockVisualizer.ExportDzMapSvg(
        blockForMap,
        projectTin,
        factTin,
        MapGridStep,
        svgPath
    );

    Console.WriteLine();
    Console.WriteLine($"SVG-карта блока {blockForMap.Name} сохранена:");
    Console.WriteLine(svgPath);
}
else
{
    Console.WriteLine($"Блок {BlockNameForMap} для карты не найден.");
}

Console.WriteLine();
Console.WriteLine("Пояснение:");
Console.WriteLine("dZ = Zфакт - Zпроект");
Console.WriteLine("dZ > 0: факт выше проекта. Для выемки это обычно недоработка.");
Console.WriteLine("dZ < 0: факт ниже проекта. Для выемки это обычно переработка.");

static void PrintSurfaceInfo(string title, TinSurface surface)
{
    Console.WriteLine();
    Console.WriteLine($"=== {title} ===");
    Console.WriteLine($"Название: {surface.Name}");
    Console.WriteLine($"Точек: {surface.Points.Count}");
    Console.WriteLine($"Треугольников: {surface.Triangles.Count}");
    Console.WriteLine($"X: {surface.MinX:F3} ... {surface.MaxX:F3}");
    Console.WriteLine($"Y: {surface.MinY:F3} ... {surface.MaxY:F3}");
    Console.WriteLine($"Z: {surface.MinZ:F3} ... {surface.MaxZ:F3}");
}

static void PrintBlockResult(WorkBlock block, BlockAnalysisResult result)
{
    Console.WriteLine($"Блок {block.Name}");
    Console.WriteLine($"  Площадь контура DXF: {block.Contour.Area:F0} м²");
    Console.WriteLine($"  Точек сетки внутри контура: {result.PointsInsideContour}");
    Console.WriteLine($"  Точек с проектом и фактом: {result.ValidPoints}");

    if (result.ValidPoints == 0)
    {
        Console.WriteLine("  Нет точек, где одновременно есть проектная и фактическая поверхность.");
        Console.WriteLine();
        return;
    }

    Console.WriteLine($"  Расчётная площадь по сетке: {result.CalculatedArea:F0} м²");
    Console.WriteLine($"  Среднее dZ: {result.AverageDz:F3} м");
    Console.WriteLine($"  Min dZ: {result.MinDz:F3} м");
    Console.WriteLine($"  Max dZ: {result.MaxDz:F3} м");
    Console.WriteLine($"  Факт выше проекта: {result.PositiveVolume:F0} м³");
    Console.WriteLine($"  Факт ниже проекта: {result.NegativeVolume:F0} м³");
    Console.WriteLine($"  Баланс: {result.TotalVolume:F0} м³");

    if (result.PositiveVolume > Math.Abs(result.NegativeVolume))
        Console.WriteLine("  Предварительно: больше недоработки, факт выше проекта.");
    else if (Math.Abs(result.NegativeVolume) > result.PositiveVolume)
        Console.WriteLine("  Предварительно: больше переработки, факт ниже проекта.");
    else
        Console.WriteLine("  Предварительно: баланс близок к нулю.");

    Console.WriteLine();
}