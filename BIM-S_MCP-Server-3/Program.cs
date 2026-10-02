using BimS.Mcp3;
using ModelContextProtocol.Server;

var handlers = new ComparisonTools(new ReportFiles());
var options = new McpServerOptions
{
    ServerInfo = new() { Name = "BIM-S_MCP-Server-3", Version = "1.0.0" },
    ToolCollection =
    [
        McpServerTool.Create(handlers.CompareAsync, new() { Name = "compare-model-versions", Description = "Детерминированно сравнивает сохранённые состояния во времени и сохраняет JSON/HTML. version1/version2: явные номера V003 или previous/latest. latest — максимальный сохранённый номер, previous — предыдущий существующий номер. Aliases разрешаются при запуске независимо для каждого раздела. mode: 3d (модель), 2d (документация), both (по умолчанию). При both с aliases возвращает отдельные отчёты разделов. Для изменений текущего состояния сначала создайте свежие snapshots через MCP1 get-model / MCP2 get-documentation. Сам инструмент Revit не читает. Результат содержит разрешённые версии и сами изменения; отсутствие предыдущего снимка — ошибка, не отсутствие изменений." }),
        McpServerTool.Create(handlers.CreateReportAsync, new() { Name = "create-model-comparison-report", Description = "Создаёт HTML BIM-S из сохранённого JSON сравнения MCP-3 без исходных снимков и Revit." })
    ]
};
using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; shutdown.Cancel(); };
await using var server = McpServer.Create(new StdioServerTransport(options), options);
try { await server.RunAsync(shutdown.Token); }
catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
