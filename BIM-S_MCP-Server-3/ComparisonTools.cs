using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace BimS.Mcp3;

public sealed class ComparisonTools(ReportFiles files, VersionResolver? versions = null)
{
    private static CallToolResult Result(object data, string text, bool error = false) => new()
    { IsError = error, StructuredContent = JsonSerializer.SerializeToElement(data, Data.Json), Content = [new TextContentBlock { Text = text }] };
    private readonly VersionResolver _versions = versions ?? new();

    public async Task<CallToolResult> CompareAsync(
        [System.ComponentModel.Description("Старая версия: V003, previous или latest. previous — предыдущая существующая версия относительно latest; aliases разрешаются при запуске.")]
        string version1,
        [System.ComponentModel.Description("Новая версия: V007, previous или latest. latest — максимальный сохранённый номер версии нужного раздела.")]
        string version2,
        CancellationToken cancellationToken,
      [System.ComponentModel.Description("auto — сравнить доступные разделы; 3d — модель; 2d — документация; both — оба раздела. По умолчанию auto.")]
string mode = "auto")
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            VersionResolver.Validate(version1);
            VersionResolver.Validate(version2);
            Data.Require(mode is "auto" or "3d" or "2d" or "both",
    "mode: auto, 3d, 2d или both.");
            if (mode == "auto")
                mode = DetectAvailableMode(version1, version2);

            // Preserve the single combined report for explicit version pairs.
            if (!VersionResolver.IsAlias(version1) && !VersionResolver.IsAlias(version2))
                return await CompareResolvedAsync(version1, version2, mode, cancellationToken);

            string[] dimensions = mode == "both" ? ["3d", "2d"] : [mode];
            var pairs = dimensions.Select(dimension =>
            {
                var pair = _versions.Resolve(version1, version2, dimension, "Test_AI-Work");
                return (Dimension: dimension, pair.Old, pair.New);
            }).ToArray();

            if (pairs.Length == 1)
                return await CompareResolvedAsync(pairs[0].Old, pairs[0].New,
                    pairs[0].Dimension, cancellationToken);

            var comparisons = new Dictionary<string, JsonElement>();
            var hasError = false;
            foreach (var pair in pairs)
            {
                var result = await CompareResolvedAsync(pair.Old, pair.New,
                    pair.Dimension, cancellationToken);
                comparisons.Add(pair.Dimension, result.StructuredContent!.Value.Clone());
                if (result.IsError == true) { hasError = true; break; }
            }

            return Result(new
            {
                status = hasError ? "error" : "complete",
                requested = new { version1, version2, mode },
                comparisons
            }, hasError
                ? "Сравнение остановлено из-за ошибки; см. результаты разделов."
                : "Сравнение модели и документации завершено; отчёты сохранены отдельно по разделам.",
                hasError);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not AccessViolationException)
        {
            return Failure("resolve-versions", ex, null);
        }
    }


    private string DetectAvailableMode(string version1, string version2)
    {
        const string modelKey = "Test_AI-Work";

        bool Available(string dimension)
        {
            try
            {
                var pair = _versions.Resolve(
                    version1,
                    version2,
                    dimension,
                    modelKey);

                var oldPath = _versions.PathFor(
                    new VersionInput(pair.Old, pair.Old),
                    dimension,
                    modelKey);

                var newPath = _versions.PathFor(
                    new VersionInput(pair.New, pair.New),
                    dimension,
                    modelKey);

                return File.Exists(oldPath) && File.Exists(newPath);
            }
            catch (InvalidDataException)
            {
                return false;
            }
        }

        var has3d = Available("3d");
        var has2d = Available("2d");

        Data.Require(
            has3d || has2d,
            "Для указанных версий нет доступной пары снимков 3d или 2d.");

        if (has3d && has2d)
            return "both";

        return has3d ? "3d" : "2d";
    }
    private async Task<CallToolResult> CompareResolvedAsync(
     string version1,
     string version2,
     string mode,
     CancellationToken cancellationToken)
    {
        string? jsonPath = null;
        var stage = "validation-and-comparison";

        try
        {
            VersionResolver.Validate(version1);
            VersionResolver.Validate(version2);
            Data.Require(!VersionResolver.IsAlias(version1) && !VersionResolver.IsAlias(version2),
                "Внутреннее сравнение требует разрешённых версий.");

            Data.Require(
                version1 != version2,
                "Нужно указать две разные версии.");

            const string modelKey = "Test_AI-Work";

            var request = new ComparisonRequest(
                mode,
                modelKey,
                new VersionInput(version1, version1),
                new VersionInput(version2, version2),
                new PairingInput(
                    true,
                    "version-name",
                    "Сопоставимость области 3D предполагается по принятой линии версий.",
                    true));

            var result = await ComparisonEngine.RunAsync(
                request,
                cancellationToken, _versions);

            stage = "save-json";
            var stem = "comparison_" + result.ComparisonId;

            jsonPath = await files.SaveAsync(
                stem + ".json",
                JsonSerializer.Serialize(result, Data.Json),
                cancellationToken);

            stage = "html-report";
            var saved = await ReadResult(jsonPath, cancellationToken);

            var htmlPath = await files.SaveAsync(
                stem + ".html",
                ComparisonReport.Render(saved, cancellationToken),
                cancellationToken);

            return Result(
                new
                {
                    status = "complete",
                    result.ComparisonId,
                    version1,
                    version2,
                    jsonPath,
                    htmlPath,
                    counts = result.Sections.ToDictionary(
                        p => p.Key,
                        p => new { p.Value.Status, p.Value.Counts }),
                    semanticSchemaVersion = 1,
                    sections = result.Sections.ToDictionary(p => p.Key, p => SemanticChanges.Section(p.Value)),
                    limitations = result.Limitations
                },
                $"Сравнение {version1} → {version2} завершено. JSON: {jsonPath}; HTML: {htmlPath}");
        }
        catch (Exception ex) when (
            ex is not OutOfMemoryException and not AccessViolationException)
        {
            return Failure(stage, ex, jsonPath);
        }
    }
    public async Task<CallToolResult> CreateReportAsync(string filePath, CancellationToken cancellationToken)
    {
        string? jsonPath = null;
        try
        {
            jsonPath = files.ValidateResultPath(filePath);
            var result = await ReadResult(jsonPath, cancellationToken);
            var html = ComparisonReport.Render(result, cancellationToken);
            var htmlPath = await files.SaveAsync($"comparison_{result.ComparisonId}_report_{Guid.NewGuid():N}.html", html, cancellationToken);
            return Result(new { status = "complete", result.ComparisonId, jsonPath, htmlPath }, "HTML: " + htmlPath);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not AccessViolationException)
        { return Failure("html-report", ex, jsonPath); }
    }
    private static async Task<ComparisonResult> ReadResult(string path, CancellationToken token) =>
        Data.Parse(await File.ReadAllBytesAsync(path, token)).Deserialize<ComparisonResult>(Data.Json) ?? throw Data.Error("Нет результата сравнения.");
    private static CallToolResult Failure(string stage, Exception ex, string? jsonPath)
    {
        var message = ex is InvalidDataException ? ex.Message : ex is OperationCanceledException ? "Операция отменена." : "Ошибка чтения, формата или записи: " + ex.GetType().Name;
        return Result(new { status = "error", stage, message, jsonPath }, message + (jsonPath == null ? "" : " JSON сохранён: " + jsonPath), true);
    }
}
