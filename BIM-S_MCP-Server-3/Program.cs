using BimS.Mcp3;
using ModelContextProtocol.Server;

var handlers = new ComparisonTools(new ReportFiles());
var options = new McpServerOptions
{
    ServerInfo = new() { Name = "BIM-S_MCP-Server-3", Version = "1.0.0" },
    ToolCollection =
    [
        McpServerTool.Create(handlers.CompareAsync, new() { Name = "compare-model-versions", Description = "Детерминированно сравнивает явно сопоставленные полные JSON MCP-1/MCP-2; сохраняет JSON и HTML. Режимы 3d, 2d, both. Revit не требуется." }),
        McpServerTool.Create(handlers.CreateReportAsync, new() { Name = "create-model-comparison-report", Description = "Создаёт HTML BIM-S из сохранённого JSON сравнения MCP-3 без исходных снимков и Revit." })
    ]
};
using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; shutdown.Cancel(); };
await using var server = McpServer.Create(new StdioServerTransport(options), options);
try { await server.RunAsync(shutdown.Token); }
catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
