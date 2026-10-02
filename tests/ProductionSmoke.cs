using System.Text.Json;
using BimS.Mcp3;
using ModelContextProtocol.Client;
using ModelContextProtocol.Server;

static class ProductionSmoke
{
    public static async Task ServeAsync(string root)
    {
        var handlers = new ComparisonTools(new ReportFiles(root),
            new VersionResolver(Path.Combine(root, "3d"), Path.Combine(root, "2d")));
        var options = new McpServerOptions
        {
            ServerInfo = new() { Name = "MCP3 isolated test server", Version = "1.0.0" },
            ToolCollection =
            [
                McpServerTool.Create(handlers.CompareAsync, new() { Name = "compare-model-versions" }),
                McpServerTool.Create(handlers.CreateReportAsync, new() { Name = "create-model-comparison-report" })
            ]
        };
        await using var server = McpServer.Create(new StdioServerTransport(options), options);
        await server.RunAsync();
    }

    public static async Task RunAsync(string workspace)
    {
        var root = Path.Combine(@"D:\BIM-S\_TestArtifacts", "MCP3", "smoke", Guid.NewGuid().ToString("N"));
        var catalog = new VersionResolver(Path.Combine(root, "3d"), Path.Combine(root, "2d"));
        foreach (var dimension in new[] { "3d", "2d" })
        foreach (var version in new[] { "V003", "V007" })
        {
            var path = catalog.PathFor(new(version, version), dimension, "Test_AI-Work");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var data = dimension == "3d" ? Fixtures.Model(1, version == "V003" ? 2 : 3) : Fixtures.Documentation();
            await File.WriteAllTextAsync(path, data.ToJsonString());
        }
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        await using var client = await McpClient.CreateAsync(new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "MCP3 isolated smoke", Command = "dotnet",
            Arguments = [typeof(ProductionSmoke).Assembly.Location, "--test-server", root], WorkingDirectory = workspace
        }), cancellationToken: deadline.Token);
        foreach (var pair in new[] { ("V003", "V007"), ("previous", "latest") })
        {
            var result = await client.CallToolAsync("compare-model-versions", new Dictionary<string, object?>
                { ["version1"] = pair.Item1, ["version2"] = pair.Item2 }, cancellationToken: deadline.Token);
            if (result.IsError == true) throw new Exception(result.StructuredContent?.GetRawText());
            var content = result.StructuredContent!.Value;
            if (content.TryGetProperty("comparisons", out var comparisons)) content = comparisons.GetProperty("3d");
            var saved = JsonSerializer.Deserialize<ComparisonResult>(
                await File.ReadAllTextAsync(content.GetProperty("jsonPath").GetString()!), Data.Json)!;
            ComparisonReport.Validate(saved);
            if (saved.Sections["3d"].Counts != new Counts(2, 2, 1, 0, 1, 1) ||
                !File.Exists(content.GetProperty("htmlPath").GetString())) throw new Exception("Wrong comparison result.");
            var report = await client.CallToolAsync("create-model-comparison-report", new Dictionary<string, object?>
                { ["filePath"] = content.GetProperty("jsonPath").GetString() }, cancellationToken: deadline.Token);
            if (report.IsError == true) throw new Exception(report.StructuredContent?.GetRawText());
        }
        Console.WriteLine("PASS explicit versions and aliases over stdio; JSON/HTML regenerated; synthetic data only.");
    }
}
