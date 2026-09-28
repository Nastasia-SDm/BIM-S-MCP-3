using System.Text.Json;
using BimS.Mcp3;
using ModelContextProtocol.Client;

static class ProductionSmoke
{
    public static async Task RunAsync(string workspace)
    {
        var input = Path.Combine(workspace, "artifacts", "demo-inputs"); Directory.CreateDirectory(input);
        var a = Fixtures.Model(1, 2, 3); var b = Fixtures.Model(1, 2, 4);
        b["elements"]![1]!["instanceParameters"]![0]!["rawValue"] = 2;
        var c = Fixtures.Documentation(); var d = Fixtures.Documentation();
        d["elements"]![0]!["properties"]!["text"]!["value"] = "Новый текст";
        string[] paths = [Path.Combine(input, "old3d.json"), Path.Combine(input, "new3d.json"), Path.Combine(input, "old2d.json"), Path.Combine(input, "new2d.json")];
        var roots = new[] { a, b, c, d };
        for (var i = 0; i < paths.Length; i++) await File.WriteAllTextAsync(paths[i], roots[i].ToJsonString());
        var request = new ComparisonRequest("both", "DEMO — синтетические данные, не версии Revit",
            new("demo-old", "Демонстрационная старая версия", paths[0], paths[2]),
            new("demo-new", "Демонстрационная новая версия", paths[1], paths[3]),
            new(true, "Автоматический тест на синтетических данных; не сопоставление реальных файлов MCP-1/MCP-2", "Синтетическая область теста", true));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        await using var client = await McpClient.CreateAsync(new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "MCP3 production directory smoke", Command = "dotnet",
            Arguments = [Path.Combine(workspace, "BIM-S_MCP-Server-3", "bin", "Debug", "net10.0", "BIM-S_MCP-Server-3.dll")], WorkingDirectory = workspace
        }), cancellationToken: deadline.Token);
        var result = await client.CallToolAsync("compare-model-versions", new Dictionary<string, object?> { ["request"] = request }, cancellationToken: deadline.Token);
        if (result.IsError == true) throw new Exception(result.StructuredContent?.GetRawText());
        var content = result.StructuredContent!.Value;
        var json = content.GetProperty("jsonPath").GetString()!; var html = content.GetProperty("htmlPath").GetString()!;
        var saved = JsonSerializer.Deserialize<ComparisonResult>(await File.ReadAllTextAsync(json), Data.Json)!;
        ComparisonReport.Validate(saved);
        if (saved.Sections["3d"].Counts != new Counts(3, 3, 1, 1, 1, 1) || saved.Sections["2d"].Counts?.Changed != 1 || !File.Exists(html)) throw new Exception("Wrong MCP comparison result.");
        var report = await client.CallToolAsync("create-model-comparison-report", new Dictionary<string, object?> { ["filePath"] = json }, cancellationToken: deadline.Token);
        if (report.IsError == true) throw new Exception(report.StructuredContent?.GetRawText());
        Console.WriteLine("PASS both tools via stdio; output in production directory; synthetic data only.");
        Console.WriteLine("JSON: " + json); Console.WriteLine("HTML: " + html);
        Console.WriteLine("Regenerated HTML: " + report.StructuredContent!.Value.GetProperty("htmlPath").GetString());
    }
}
