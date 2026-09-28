using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BimS.Mcp3;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

var workspace = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
if (args.Contains("--production-smoke-only"))
{
    await ProductionSmoke.RunAsync(workspace);
    return;
}
var output = Path.Combine(workspace, "artifacts", "tests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(output); var passed = 0;
void Check(bool condition, string label) { if (!condition) throw new Exception(label); passed++; Console.WriteLine("PASS " + label); }
void Reject(Action action, string label) { try { action(); } catch (InvalidDataException) { Check(true, label); return; } throw new Exception("Expected rejection: " + label); }
JsonObject Copy(JsonObject root) => (JsonObject)root.DeepClone();
JsonObject ResultData(CallToolResult result) => Data.Object(JsonNode.Parse(result.StructuredContent!.Value.GetRawText()));
SectionResult Compare(JsonObject a, JsonObject b, string dimension = "3d") => ComparisonEngine.Compare(Fixtures.Snapshot(a, dimension), Fixtures.Snapshot(b, dimension));

var original = Fixtures.Model(1, 2, 3); var changed = Fixtures.Model(1, 2, 4);
changed["elements"]![1]!["instanceParameters"]![0]!["rawValue"] = 2;
var comparison = Compare(original, changed);
Check(comparison.Counts == new Counts(3, 3, 1, 1, 1, 1), "four mutually exclusive groups and balances");
Check(comparison.Unchanged![0].ElementId == 1 && comparison.Changed![0].ElementId == 2 && comparison.Added![0].ElementId == 4 && comparison.Removed![0].ElementId == 3, "ElementId preserved in each group");
Check(comparison.Changed![0].Differences.Single().Path.EndsWith("/rawValue"), "factual stable parameter difference");
Check(Canonical.Equal(Data.Node(comparison), Data.Node(Compare(original, changed))), "repeatable result");
var reordered = Copy(original); var elements = Data.Array(reordered, "elements"); var first = elements[0]!; elements.RemoveAt(0); elements.Add(first);
reordered["startedAtUtc"] = "2026-09-28T06:00:00Z";
reordered["elements"]![0]!["instanceParameters"]![0]!["builtInParameterNames"] = new JsonArray("A", "B");
Check(Compare(original, reordered).Counts!.Unchanged == 3, "element and alias order, export time ignored");
var typed = Copy(original); foreach (var e in Data.Array(typed, "elements")) e!["typeParameters"]![0]!["rawValue"] = 9;
Check(Compare(original, typed).Counts!.Changed == 3, "type parameter affects all owning rows");
var paramAdd = Copy(original); Data.Array(Data.Object(paramAdd["elements"]![0]), "instanceParameters").Add(Fixtures.Parameter(1, pid: -2));
Check(Compare(original, paramAdd).Counts!.Changed == 1 && Compare(paramAdd, original).Counts!.Changed == 1, "parameter addition and removal");
var paramOrder = Copy(paramAdd); var ps = Data.Array(Data.Object(paramOrder["elements"]![0]), "instanceParameters"); var p0 = ps[0]!; ps.RemoveAt(0); ps.Add(p0);
Check(Compare(paramAdd, paramOrder).Counts!.Unchanged == 3, "parameter order ignored");
Check(Canonical.Equal(JsonNode.Parse("1.0"), JsonNode.Parse("1e0")) && Canonical.Equal(JsonNode.Parse("-0.0"), JsonNode.Parse("0")), "exact number normalization");
Check(!Canonical.Equal(JsonNode.Parse("9007199254740992"), JsonNode.Parse("9007199254740993")) && !Canonical.Equal(JsonNode.Parse("1.00000000000000000000001"), JsonNode.Parse("1")), "no loss of integer or fractional precision");
Check(Compare(Fixtures.Model(long.MaxValue), Fixtures.Model(long.MaxValue)).Unchanged![0].ElementId == long.MaxValue, "Int64 maximum ID");
var absent = new JsonObject(); var nullValue = new JsonObject { ["x"] = null }; var diffMissing = Canonical.Diff(absent, nullValue).Single();
Check(!diffMissing.Old.Exists && diffMissing.New.Exists && diffMissing.New.Value == null, "missing differs from null");
Check(!Canonical.Equal(JsonValue.Create(""), null) && !Canonical.Equal(JsonValue.Create(false), JsonValue.Create(0)), "empty string, false, zero, null distinct");
var noValue = Copy(original); var p = noValue["elements"]![0]!["instanceParameters"]![0]!; p["status"] = "noValue"; p["hasValue"] = false; p["rawValue"] = null; p["displayValue"] = null; p["convertedValue"] = null;
Check(Compare(original, noValue).Counts!.Changed == 1, "noValue is valid and compared");
var partial = Copy(original); partial["status"] = "partial"; Reject(() => Fixtures.Snapshot(partial), "partial rejected");
var duplicate = Copy(original); Data.Array(duplicate, "elements").Add(duplicate["elements"]![0]!.DeepClone()); Reject(() => Fixtures.Snapshot(duplicate), "duplicate entity rejected");
var duplicateParameter = Copy(original); Data.Array(Data.Object(duplicateParameter["elements"]![0]), "instanceParameters").Add(duplicateParameter["elements"]![0]!["instanceParameters"]![0]!.DeepClone()); Reject(() => Fixtures.Snapshot(duplicateParameter), "duplicate parameter rejected");
Reject(() => Data.Parse(Encoding.UTF8.GetBytes("{\"a\":1,\"a\":2}")), "duplicate JSON object keys rejected");
var broken = Copy(original); broken["elements"]!.AsArray().RemoveAt(0); Reject(() => Fixtures.Snapshot(broken), "incomplete requested IDs rejected");
Check(Compare(Fixtures.Model(), Fixtures.Model()).Counts == new Counts(0, 0, 0, 0, 0, 0), "explicit complete empty selection accepted");
var missingArray = Fixtures.Model(); missingArray.Remove("elements"); Reject(() => Fixtures.Snapshot(missingArray), "missing array is not empty snapshot");

var graph = Fixtures.Documentation(); var graphChanged = Copy(graph);
graphChanged["elements"]![0]!["properties"]!["text"]!["value"] = "Новый текст";
graphChanged["placements"]![0]!["properties"]!["point"] = Fixtures.Property(new JsonObject { ["x"] = 1, ["y"] = 2 });
var twoD = Compare(graph, graphChanged, "2d"); Check(twoD.Counts!.Changed == 2, "2D text and position properties");
var relation = Copy(graph); relation["relations"]![0]!["toId"] = 2;
var relationDiff = Compare(graph, relation, "2d"); Check(relationDiff.Counts!.Changed == 1 && relationDiff.Changed![0].ElementId == 3, "relation changes only fromId, no propagation");
var conflict = Copy(graph); conflict["views"]![0]!["uniqueId"] = "another"; Reject(() => Compare(graph, conflict, "2d"), "ElementId / uniqueId conflict rejected");
var scope = Copy(graph); scope["scope"]!["includeTemplates"] = true; Reject(() => Compare(graph, scope, "2d"), "incompatible scope rejected");
var coverage = Copy(graph); coverage["coverage"] = new JsonArray("views"); Reject(() => Compare(graph, coverage, "2d"), "incompatible coverage rejected");
var noParameters = Copy(graph); noParameters["parameters"]!.AsArray().RemoveAt(0); Reject(() => Fixtures.Snapshot(noParameters, "2d"), "parameters for every 2D entity required");
var badRelation = Copy(graph); badRelation["relations"]![0]!["toId"] = 800; Reject(() => Fixtures.Snapshot(badRelation, "2d"), "dangling graph relation rejected");
var duplicateRelation = Copy(graph); badRelation = Copy(graph); Data.Array(duplicateRelation, "relations").Add(duplicateRelation["relations"]![0]!.DeepClone()); Reject(() => Fixtures.Snapshot(duplicateRelation, "2d"), "duplicate relation rejected");
var unsupported = Copy(graph); unsupported["elements"]![0]!["properties"]!["test"] = new JsonObject { ["status"] = "unsupported", ["value"] = null, ["source"] = "fixture" };
Check(Compare(graph, unsupported, "2d").Counts!.Changed == 1, "unsupported property accepted and distinguished");
var propertyError = Copy(unsupported); propertyError["elements"]![0]!["properties"]!["test"]!["status"] = "error"; Reject(() => Fixtures.Snapshot(propertyError, "2d"), "hidden property error rejected");
var dependencies = Copy(graph); dependencies["views"]![0]!["properties"]!["dependentViewIds"] = Fixtures.Property(new JsonArray(5, 6));
var dependencyOrder = Copy(dependencies); dependencyOrder["views"]![0]!["properties"]!["dependentViewIds"]!["value"] = new JsonArray(6, 5);
Check(Compare(dependencies, dependencyOrder, "2d").Counts!.Unchanged == 4, "dependentViewIds is a set");
var curve = Copy(graph); curve["elements"]![0]!["properties"]!["curve"] = Fixtures.Property(new JsonObject { ["sampledPoints"] = new JsonArray(1, 2) });
var curveOrder = Copy(curve); curveOrder["elements"]![0]!["properties"]!["curve"]!["value"]!["sampledPoints"] = new JsonArray(2, 1);
Check(Compare(curve, curveOrder, "2d").Counts!.Changed == 1, "geometric point order preserved");

var aPath = Path.Combine(output, "old3d.json"); var bPath = Path.Combine(output, "new3d.json");
var cPath = Path.Combine(output, "old2d.json"); var dPath = Path.Combine(output, "new2d.json");
await File.WriteAllTextAsync(aPath, original.ToJsonString()); await File.WriteAllTextAsync(bPath, changed.ToJsonString());
await File.WriteAllTextAsync(cPath, graph.ToJsonString()); await File.WriteAllTextAsync(dPath, graphChanged.ToJsonString());
var request = Fixtures.Request("3d", aPath, bPath);
Reject(() => ComparisonEngine.ValidateRequest(request with { Pairing = request.Pairing with { Confirmed = false } }), "explicit pairing required");
Reject(() => ComparisonEngine.ValidateRequest(request with { Pairing = request.Pairing with { Comparable3dScopeConfirmed = false } }), "3D scope confirmation required");
var only3d = await ComparisonEngine.RunAsync(request); Check(only3d.Sections["2d"].Status == "notRequested", "3D independent mode");
var only2d = await ComparisonEngine.RunAsync(Fixtures.Request("2d", cPath, dPath)); Check(only2d.Sections["3d"].Status == "notRequested", "2D independent mode");
var bothRequest = request with { Mode = "both", OldVersion = request.OldVersion with { Json2dPath = cPath }, NewVersion = request.NewVersion with { Json2dPath = dPath } };
var both = await ComparisonEngine.RunAsync(bothRequest); var html = ComparisonReport.Render(both);
Check(both.Sections.Values.All(s => s.Status == "complete"), "both mode");
Check(html.Contains("3D — Модель") && html.Contains("2D — Документация") && new[] { "НЕ ИЗМЕНИЛИСЬ", "ИЗМЕНИЛИСЬ", "ПОЯВИЛИСЬ", "УДАЛИЛИСЬ", "ElementId", "#eceff1", "#fff4cc", "#e2f2df", "#f8e1e7" }.All(html.Contains), "HTML sections, IDs, groups and colors");
Check(!html.Contains("<script>alert") && html.Contains("&lt;script&gt;"), "HTML injection encoded");
var tampered = both with { Sections = new(both.Sections) { ["3d"] = both.Sections["3d"] with { Counts = new Counts(0, 0, 0, 0, 0, 0) } } };
Reject(() => ComparisonReport.Render(tampered), "report checks result integrity");
var tools = new ComparisonTools(new ReportFiles(output)); var saved = await tools.CompareAsync(bothRequest, default); var savedData = ResultData(saved);
Check(saved.IsError != true && File.Exists(savedData["jsonPath"]!.GetValue<string>()) && File.Exists(savedData["htmlPath"]!.GetValue<string>()), "JSON and HTML saved");
// No input access on regeneration: move only our own test inputs inside the workspace.
File.Move(aPath, aPath + ".saved"); File.Move(bPath, bPath + ".saved");
var regenerated = await tools.CreateReportAsync(savedData["jsonPath"]!.GetValue<string>(), default);
Check(regenerated.IsError != true, "regenerate HTML without original snapshots");
File.Move(aPath + ".saved", aPath); File.Move(bPath + ".saved", bPath);
var failing = await new ComparisonTools(new FailingHtmlFiles(output)).CompareAsync(request, default);
Check(failing.IsError == true && ResultData(failing)["stage"]!.GetValue<string>() == "html-report" && File.Exists(ResultData(failing)["jsonPath"]!.GetValue<string>()), "HTML failure preserves JSON and returns path");
var canceled = await tools.CompareAsync(request, new CancellationToken(true)); Check(canceled.IsError == true && ResultData(canceled)["jsonPath"] == null, "cancellation before save");
await File.WriteAllTextAsync(Path.Combine(output, "no-overwrite.json"), "original");
try { await new ReportFiles(output).SaveAsync("no-overwrite.json", "replacement", default); throw new Exception("overwrite accepted"); }
catch (IOException) { Check(await File.ReadAllTextAsync(Path.Combine(output, "no-overwrite.json")) == "original" && !Directory.GetFiles(output, "*.tmp").Any(), "atomic save never overwrites and cleans temp"); }

// Real files are only read; never claim that these exports form paired model revisions.
foreach (var (folder, dimension) in new[] { (@"D:\BIM-S-MCP-1_Отчеты_Версии модели", "3d"), (@"D:\BIM-S-MCP-2_Отчеты_Версии модели", "2d") })
    if (Directory.Exists(folder)) foreach (var file in Directory.GetFiles(folder, "*.json"))
    {
        var snapshot = await SnapshotReader.ReadAsync(file, dimension);
        var self = ComparisonEngine.Compare(snapshot, snapshot);
        Check(self.Counts!.Unchanged == snapshot.States.Count && self.Counts.Changed == 0, "real snapshot validation and self comparison: " + Path.GetFileName(file));
    }

using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
var server = Path.Combine(workspace, "BIM-S_MCP-Server-3", "bin", "Debug", "net10.0", "BIM-S_MCP-Server-3.dll");
var stderr = new List<string>();
await using var client = await McpClient.CreateAsync(new StdioClientTransport(new StdioClientTransportOptions
{ Name = "MCP3 tests", Command = "dotnet", Arguments = [server], WorkingDirectory = workspace, StandardErrorLines = stderr.Add }), cancellationToken: deadline.Token);
var exposed = await client.ListToolsAsync(cancellationToken: deadline.Token);
Check(exposed.Select(t => t.Name).Order().SequenceEqual(new[] { "compare-model-versions", "create-model-comparison-report" }), "stdio handshake, exactly two tools");
await File.WriteAllTextAsync(Path.Combine(output, "tool-schemas.json"), JsonSerializer.Serialize(exposed.Select(t => new { t.Name, t.JsonSchema }), Data.Json));
var invalid = await client.CallToolAsync("compare-model-versions", new Dictionary<string, object?> { ["request"] = request with { Pairing = request.Pairing with { Confirmed = false } } }, cancellationToken: deadline.Token);
Check(invalid.IsError == true && ResultData(invalid)["stage"]!.GetValue<string>() == "validation-and-comparison", "structured validation error over MCP");
Check(stderr.Count == 0, "clean protocol stdout and no unexpected stderr");
Console.WriteLine($"{passed} checks passed. Artifacts: {output}");

sealed class FailingHtmlFiles(string root) : ReportFiles(root)
{
    public override Task<string> SaveAsync(string name, string contents, CancellationToken token) =>
        name.EndsWith(".html") ? throw new IOException("simulated HTML failure") : base.SaveAsync(name, contents, token);
}
