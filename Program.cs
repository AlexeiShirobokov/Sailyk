using System.Text;
using Sailyk;
using Sailyk.Services;

Console.OutputEncoding = Encoding.UTF8;

// =====================
// ВХОДНЫЕ ФАЙЛЫ
// =====================

const string DxfFileName = "blocks.dxf";

// Если обе поверхности в одном файле:
const string CombinedLandXmlFileName = "surfaces.xml";

// Если поверхности в отдельных файлах:
const string InitialSurfaceFileName = "Нач_БВР_Мега_блок.xml";
const string FinalSurfaceFileName = "27 04 26_ур. Сайылык.xml";

// =====================
// НАЗВАНИЯ ПОВЕРХНОСТЕЙ
// =====================

const string InitialSurfaceName = "Нач_БВР_Мега_блок";
const string FinalSurfaceName = "27 04 26_ур. Сайылык";

// =====================
// НАСТРОЙКИ РАСЧЁТА
// =====================

const double GridStep = 2.0;
const double WorkedAreaThresholdAbs = 1.0;      // где считаем, что реально работали
const double StrongChangeThresholdAbs = 5.0;    // граница сильного изменения для карты

// =====================
// СЛОИ DXF
// =====================

const string GeologicalLabelLayerName = "Geol_Block-C-TOPO";
const string GeologicalBoundaryLayerName = "Geol_Block-C-TOPO";

const string BlastLabelLayerName = "P_block-C-TOPO";
const string BlastBoundaryLayerName = "P_block-C-TOPO";

const string LocalAreaBoundaryLayerName = "uhodka-C-TOPO";

// Зелёный производственный контур
const string ProductionContourBoundaryLayerName = "Proiz_block-C-TOPO";

// Фильтр по цвету пока не используем
int? geologicalBoundaryColorIndex = null;
int? blastBoundaryColorIndex = null;
int? localAreaBoundaryColorIndex = null;
int? productionContourBoundaryColorIndex = null;

var dataDir = Path.Combine(AppContext.BaseDirectory, "Data");
var dxfPath = Path.Combine(dataDir, DxfFileName);

Console.WriteLine("=== ФАЙЛЫ ===");
Console.WriteLine($"Data: {dataDir}");
Console.WriteLine($"DXF:  {dxfPath}");
Console.WriteLine();

if (!File.Exists(dxfPath))
{
    Console.WriteLine($"Файл DXF не найден: {dxfPath}");
    return;
}

DxfBlockReader.PrintLayerDiagnostics(dxfPath);

Console.WriteLine();
Console.WriteLine("=== ЧТЕНИЕ ПОВЕРХНОСТЕЙ ===");

TinSurface initialSurface;
TinSurface finalSurface;

try
{
    initialSurface = ReadSurfaceSmart(
        dataDir,
        CombinedLandXmlFileName,
        InitialSurfaceFileName,
        InitialSurfaceName);

    finalSurface = ReadSurfaceSmart(
        dataDir,
        CombinedLandXmlFileName,
        FinalSurfaceFileName,
        FinalSurfaceName);
}
catch (Exception ex)
{
    Console.WriteLine("Ошибка чтения поверхностей:");
    Console.WriteLine(ex.Message);
    return;
}

PrintSurfaceInfo("ПЕРВОНАЧАЛЬНАЯ ПОВЕРХНОСТЬ", initialSurface);
PrintSurfaceInfo("ВТОРИЧНАЯ ПОВЕРХНОСТЬ", finalSurface);

Console.WriteLine();
Console.WriteLine("Строим TIN-индексы...");
var initialTin = new TinInterpolator(initialSurface);
var finalTin = new TinInterpolator(finalSurface);

Console.WriteLine();
Console.WriteLine("=== ЧТЕНИЕ КОНТУРОВ DXF ===");

var geologicalBlocks = DxfBlockReader.ReadLabeledBlocks(
    dxfPath,
    GeologicalLabelLayerName,
    GeologicalBoundaryLayerName,
    BlockLabelKind.Geological,
    geologicalBoundaryColorIndex);

var blastBlocks = DxfBlockReader.ReadLabeledBlocks(
    dxfPath,
    BlastLabelLayerName,
    BlastBoundaryLayerName,
    BlockLabelKind.Blast,
    blastBoundaryColorIndex);

var localAreas = DxfBlockReader.ReadUnlabeledClosedContours(
    dxfPath,
    LocalAreaBoundaryLayerName,
    "Локальная площадь",
    localAreaBoundaryColorIndex);

var productionContours = DxfBlockReader.ReadUnlabeledClosedContours(
    dxfPath,
    ProductionContourBoundaryLayerName,
    "Производственный контур",
    productionContourBoundaryColorIndex);

PrintBlockSet("Геологические блоки", geologicalBlocks);
PrintBlockSet("Взрывные блоки", blastBlocks);
PrintBlockSet("Локальные площади", localAreas);
PrintBlockSet("Производственный контур", productionContours);

if (productionContours.Count == 0)
{
    Console.WriteLine();
    Console.WriteLine("ОШИБКА: производственный контур не найден.");
    Console.WriteLine($"Проверь слой: {ProductionContourBoundaryLayerName}");
    return;
}

if (productionContours.Count > 1)
{
    Console.WriteLine();
    Console.WriteLine($"ПРЕДУПРЕЖДЕНИЕ: найдено производственных контуров: {productionContours.Count}");
    Console.WriteLine("Расчёт будет выполнен внутри всех найденных производственных контуров.");
}

if (geologicalBlocks.Count == 0)
{
    Console.WriteLine();
    Console.WriteLine("ПРЕДУПРЕЖДЕНИЕ: геологические блоки не найдены.");
    Console.WriteLine($"Проверь слой: {GeologicalBoundaryLayerName}");
}

if (blastBlocks.Count == 0)
{
    Console.WriteLine();
    Console.WriteLine("ПРЕДУПРЕЖДЕНИЕ: взрывные блоки не найдены.");
    Console.WriteLine($"Проверь слой: {BlastBoundaryLayerName}");
}

if (localAreas.Count == 0)
{
    Console.WriteLine();
    Console.WriteLine("ПРЕДУПРЕЖДЕНИЕ: локальные площади не найдены.");
    Console.WriteLine($"Проверь слой: {LocalAreaBoundaryLayerName}");
}

Console.WriteLine();
Console.WriteLine("=== РАСЧЁТ ОБЪЁМА ВСКРЫШИ ===");
Console.WriteLine($"Шаг сетки: {GridStep:F2} м");
Console.WriteLine("Расчётная область: внутри зелёного производственного контура.");
Console.WriteLine("Формула: объём вскрыши = Z первоначальной поверхности - Z вторичной поверхности");
Console.WriteLine("Если значение положительное — поверхность стала ниже, значит объём снят.");
Console.WriteLine("Если значение отрицательное — вторичная поверхность выше первоначальной.");
Console.WriteLine();

var noLocalCells = new List<DebugGridCell>();

var results = VolumeBreakdownAnalyzer.Analyze(
    geologicalBlocks,
    blastBlocks,
    localAreas,
    productionContours,
    initialTin,
    finalTin,
    GridStep,
    cell => noLocalCells.Add(cell));

var surfaceChangeCells = SurfaceChangeVisualizer.CollectCells(
    geologicalBlocks,
    blastBlocks,
    localAreas,
    productionContours,
    initialTin,
    finalTin,
    GridStep);

if (results.Count == 0)
{
    Console.WriteLine("Нет расчётных точек, где одновременно есть обе поверхности внутри производственного контура.");
    return;
}

var detailedPath = Path.Combine(dataDir, "volume_breakdown_detailed.csv");
var geoPath = Path.Combine(dataDir, "volume_by_geological_block.csv");
var blastPath = Path.Combine(dataDir, "volume_by_blast_block.csv");
var localPath = Path.Combine(dataDir, "volume_by_local_area.csv");
var productionPath = Path.Combine(dataDir, "volume_by_production_contour.csv");

var surfaceChangeCellsPath = Path.Combine(dataDir, "surface_change_cells.csv");
var surfaceChangeSummaryPath = Path.Combine(dataDir, "surface_change_summary.csv");
var globalSurfaceChangeMapPath = Path.Combine(dataDir, "global_surface_change_map.html");
var workedAreaMapPath = Path.Combine(dataDir, "worked_area_map.html");

var noLocalPointsPath = Path.Combine(dataDir, "debug_no_local_area_points.csv");
var noLocalSummaryPath = Path.Combine(dataDir, "debug_no_local_area_by_geo_blast.csv");
var noLocalMapPath = Path.Combine(dataDir, "debug_no_local_area_map.html");

CsvReportWriter.WriteDetailed(detailedPath, results);

CsvReportWriter.WriteSummary(
    geoPath,
    BuildSummary(results, r => r.Key.GeologicalBlock),
    "Геологический блок");

CsvReportWriter.WriteSummary(
    blastPath,
    BuildSummary(results, r => r.Key.BlastBlock),
    "Взрывной блок");

CsvReportWriter.WriteSummary(
    localPath,
    BuildSummary(results, r => r.Key.LocalArea),
    "Локальная площадь");

CsvReportWriter.WriteSummary(
    productionPath,
    new List<VolumeSummaryRow>
    {
        new(
            GroupName: "Производственный контур",
            Area: results.Sum(x => x.Area),
            ExcavationVolume: results.Sum(x => x.ExcavationVolume),
            FillOrErrorVolume: results.Sum(x => x.FillOrErrorVolume),
            NetVolume: results.Sum(x => x.NetVolume))
    },
    "Производственный контур");

DebugMapWriter.WriteNoLocalAreaPointsCsv(noLocalPointsPath, noLocalCells);
DebugMapWriter.WriteNoLocalAreaSummaryCsv(noLocalSummaryPath, noLocalCells);
DebugMapWriter.WriteNoLocalAreaMapHtml(
    noLocalMapPath,
    productionContours,
    geologicalBlocks,
    blastBlocks,
    localAreas,
    noLocalCells);


SurfaceChangeVisualizer.WriteCellsCsv(surfaceChangeCellsPath, surfaceChangeCells);

SurfaceChangeVisualizer.WriteCategorySummaryCsv(
    surfaceChangeSummaryPath,
    surfaceChangeCells,
    WorkedAreaThresholdAbs,
    StrongChangeThresholdAbs);

SurfaceChangeVisualizer.WriteGlobalChangeMapHtml(
    globalSurfaceChangeMapPath,
    productionContours,
    geologicalBlocks,
    blastBlocks,
    localAreas,
    surfaceChangeCells,
    WorkedAreaThresholdAbs,
    StrongChangeThresholdAbs);

SurfaceChangeVisualizer.WriteWorkedAreaMapHtml(
    workedAreaMapPath,
    productionContours,
    geologicalBlocks,
    blastBlocks,
    localAreas,
    surfaceChangeCells,
    WorkedAreaThresholdAbs);


PrintSummary("ИТОГО ПО ГЕОЛОГИЧЕСКИМ БЛОКАМ", BuildSummary(results, r => r.Key.GeologicalBlock));
PrintSummary("ИТОГО ПО ВЗРЫВНЫМ БЛОКАМ", BuildSummary(results, r => r.Key.BlastBlock));
PrintSummary("ИТОГО ПО ЛОКАЛЬНЫМ ПЛОЩАДЯМ", BuildSummary(results, r => r.Key.LocalArea));

PrintSummary(
    "ИТОГО ПО ПРОИЗВОДСТВЕННОМУ КОНТУРУ",
    new List<VolumeSummaryRow>
    {
        new(
            GroupName: "Производственный контур",
            Area: results.Sum(x => x.Area),
            ExcavationVolume: results.Sum(x => x.ExcavationVolume),
            FillOrErrorVolume: results.Sum(x => x.FillOrErrorVolume),
            NetVolume: results.Sum(x => x.NetVolume))
    });

Console.WriteLine();
Console.WriteLine("=== ОТЛАДКА: БЕЗ ЛОКАЛЬНОЙ ПЛОЩАДИ ===");
Console.WriteLine($"Точек сетки: {noLocalCells.Count}");
Console.WriteLine($"Площадь: {noLocalCells.Sum(x => x.CellSize * x.CellSize):F0} м²");
Console.WriteLine($"Баланс: {noLocalCells.Sum(x => x.Volume):F0} м³");

Console.WriteLine();
Console.WriteLine("=== CSV И HTML ФАЙЛЫ СОХРАНЕНЫ ===");
Console.WriteLine(detailedPath);
Console.WriteLine(geoPath);
Console.WriteLine(blastPath);
Console.WriteLine(localPath);
Console.WriteLine(productionPath);
Console.WriteLine(noLocalPointsPath);
Console.WriteLine(noLocalSummaryPath);
Console.WriteLine(noLocalMapPath);
Console.WriteLine(surfaceChangeCellsPath);
Console.WriteLine(surfaceChangeSummaryPath);
Console.WriteLine(globalSurfaceChangeMapPath);
Console.WriteLine(workedAreaMapPath);


static TinSurface ReadSurfaceSmart(
    string dataDir,
    string combinedLandXmlFileName,
    string separateSurfaceFileName,
    string surfaceName)
{
    var combinedPath = Path.Combine(dataDir, combinedLandXmlFileName);

    if (File.Exists(combinedPath))
    {
        Console.WriteLine($"Ищем поверхность '{surfaceName}' в общем файле:");
        Console.WriteLine(combinedPath);

        return LandXmlSurfaceReader.ReadSurfaceByName(combinedPath, surfaceName);
    }

    var separatePath = Path.Combine(dataDir, separateSurfaceFileName);

    if (!File.Exists(separatePath))
    {
        throw new FileNotFoundException(
            $"Не найден ни общий файл {combinedPath}, ни отдельный файл {separatePath}");
    }

    Console.WriteLine($"Читаем отдельный файл поверхности:");
    Console.WriteLine(separatePath);

    var surfaceNames = LandXmlSurfaceReader.ListSurfaceNames(separatePath);

    if (surfaceNames.Any(x => string.Equals(
            x.Trim(),
            surfaceName.Trim(),
            StringComparison.OrdinalIgnoreCase)))
    {
        return LandXmlSurfaceReader.ReadSurfaceByName(separatePath, surfaceName);
    }

    if (surfaceNames.Count == 1)
    {
        Console.WriteLine($"В файле одна поверхность: '{surfaceNames[0]}'. Берём её.");
        return LandXmlSurfaceReader.ReadFirstSurface(separatePath);
    }

    return LandXmlSurfaceReader.ReadSurfaceByName(separatePath, surfaceName);
}

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

static void PrintBlockSet(string title, IReadOnlyList<WorkBlock> blocks)
{
    Console.WriteLine();
    Console.WriteLine($"=== {title} ===");
    Console.WriteLine($"Количество: {blocks.Count}");

    foreach (var block in blocks.OrderBy(x => x.Name))
    {
        Console.WriteLine($"{block.Name}: площадь {block.Contour.Area:F0} м², слой {block.Contour.Layer}");
    }
}

static List<VolumeSummaryRow> BuildSummary(
    IEnumerable<VolumeBreakdownResult> results,
    Func<VolumeBreakdownResult, string> groupSelector)
{
    return results
        .GroupBy(groupSelector)
        .Select(g => new VolumeSummaryRow(
            GroupName: g.Key,
            Area: g.Sum(x => x.Area),
            ExcavationVolume: g.Sum(x => x.ExcavationVolume),
            FillOrErrorVolume: g.Sum(x => x.FillOrErrorVolume),
            NetVolume: g.Sum(x => x.NetVolume)))
        .OrderBy(x => x.GroupName, StringComparer.OrdinalIgnoreCase)
        .ToList();
}

static void PrintSummary(string title, IReadOnlyList<VolumeSummaryRow> rows)
{
    Console.WriteLine();
    Console.WriteLine($"=== {title} ===");

    foreach (var row in rows)
    {
        Console.WriteLine(
            $"{row.GroupName}: " +
            $"площадь {row.Area:F0} м²; " +
            $"вскрыша {row.ExcavationVolume:F0} м³; " +
            $"отрицательный объём {row.FillOrErrorVolume:F0} м³; " +
            $"баланс {row.NetVolume:F0} м³");
    }
}