using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace BimS.Mcp3;

public sealed class ComparisonTools(ReportFiles files)
{
    private static CallToolResult Result(object data, string text, bool error = false) => new()
    { IsError = error, StructuredContent = JsonSerializer.SerializeToElement(data, Data.Json), Content = [new TextContentBlock { Text = text }] };
    public async Task<CallToolResult> CompareAsync(ComparisonRequest request, CancellationToken cancellationToken)
    {
        string? jsonPath = null; var stage = "validation-and-comparison";
        try
        {
            var result = await ComparisonEngine.RunAsync(request, cancellationToken);
            stage = "save-json";
            var stem = "comparison_" + result.ComparisonId;
            jsonPath = await files.SaveAsync(stem + ".json", JsonSerializer.Serialize(result, Data.Json), cancellationToken);
            stage = "html-report";
            var saved = await ReadResult(jsonPath, cancellationToken);
            var htmlPath = await files.SaveAsync(stem + ".html", ComparisonReport.Render(saved, cancellationToken), cancellationToken);
            return Result(new { status = "complete", result.ComparisonId, mode = request.Mode, jsonPath, htmlPath,
                counts = result.Sections.ToDictionary(p => p.Key, p => new { p.Value.Status, p.Value.Counts }), limitations = result.Limitations },
                $"Сравнение завершено. JSON: {jsonPath}; HTML: {htmlPath}");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not AccessViolationException)
        { return Failure(stage, ex, jsonPath); }
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
