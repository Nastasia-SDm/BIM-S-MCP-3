using System.Text.Json.Nodes;
using BimS.Mcp3;

static class Fixtures
{
    public static JsonObject Parameter(long owner, string source = "instance", long pid = -1) => new()
    {
        ["parameterId"] = pid, ["ownerElementId"] = owner, ["source"] = source, ["name"] = "Параметр", ["storageType"] = "Double",
        ["hasValue"] = true, ["isReadOnly"] = false, ["dataTypeId"] = "length", ["rawValue"] = 1, ["displayValue"] = "1",
        ["unitTypeId"] = "ft", ["convertedValue"] = 1, ["status"] = "ok", ["errors"] = new JsonArray(), ["builtInParameterNames"] = new JsonArray("B", "A")
    };
    public static JsonObject Row(long id, bool pascal = false)
    {
        var row = new JsonObject { ["elementId"] = id, ["category"] = "Стены", ["familyName"] = "Family", ["typeName"] = "Type", ["typeId"] = 99,
            ["instanceParameters"] = new JsonArray(Parameter(id)), ["typeParameters"] = new JsonArray(Parameter(99, "type")), ["status"] = "ok", ["errors"] = new JsonArray() };
        if (!pascal) return row;
        var result = new JsonObject();
        foreach (var p in row) result[p.Key is "status" or "errors" ? p.Key : char.ToUpperInvariant(p.Key[0]) + p.Key[1..]] = p.Value?.DeepClone();
        return result;
    }
    public static JsonObject Base() => new()
    {
        ["schemaVersion"] = 1, ["startedAtUtc"] = "2026-09-28T07:00:00Z", ["completedAtUtc"] = "2026-09-28T07:01:00Z",
        ["documentSession"] = "0123456789abcdef0123456789abcdef", ["status"] = "complete", ["errors"] = new JsonArray(), ["unprocessedElementIds"] = new JsonArray()
    };
    public static JsonObject Model(params long[] ids)
    {
        var root = Base(); root["requestedElementIds"] = Data.Node(ids); root["elements"] = new JsonArray(ids.Select(id => (JsonNode)Row(id)).ToArray()); return root;
    }
    public static JsonObject Property(JsonNode? value) => new() { ["status"] = "ok", ["value"] = value, ["source"] = "fixture", ["unit"] = null, ["coordinateSystem"] = null };
    public static JsonObject Entity(long id, string kind) => new()
    {
        ["elementId"] = id, ["uniqueId"] = "unique-" + id, ["kind"] = kind, ["class"] = "Fixture." + kind,
        ["categoryId"] = -1, ["category"] = "Стены", ["name"] = "<script>alert(1)</script>", ["familyName"] = "Family", ["typeName"] = "Type", ["typeId"] = 99,
        ["ownerViewId"] = null, ["status"] = "ok", ["errors"] = new JsonArray(), ["properties"] = new JsonObject { ["text"] = Property(JsonValue.Create("Текст")) }
    };
    public static JsonObject Documentation()
    {
        var root = Base();
        root["contractVersion"] = 1; root["server"] = "BIM-S_MCP-Server-2"; root["snapshotId"] = "1123456789abcdef0123456789abcdef";
        root["document"] = new JsonObject { ["title"] = "Fixture", ["revitVersion"] = "2024" };
        root["scope"] = new JsonObject { ["kind"] = "document", ["sheetIds"] = new JsonArray(), ["viewIds"] = new JsonArray(), ["includeTemplates"] = false };
        root["coverage"] = new JsonArray("elements"); root["warnings"] = new JsonArray();
        root["sheets"] = new JsonArray(Entity(1, "sheet")); root["views"] = new JsonArray(Entity(2, "view"));
        root["placements"] = new JsonArray(Entity(3, "viewport")); root["elements"] = new JsonArray(Entity(4, "text"));
        root["counts"] = new JsonObject { ["sheets"] = 1, ["views"] = 1, ["placements"] = 1, ["elements"] = 1 };
        root["relations"] = new JsonArray(new JsonObject { ["kind"] = "onSheet", ["fromId"] = 3, ["toId"] = 1 });
        root["requestedElementIds"] = new JsonArray(1, 2, 3, 4);
        root["parameters"] = new JsonArray(Enumerable.Range(1, 4).Select(id => (JsonNode)Row(id, true)).ToArray()); return root;
    }
    public static Snapshot Snapshot(JsonObject root, string dimension = "3d") => new(dimension,
        new(Path.GetFullPath("fixture.json"), new string('a', 64), Data.Pick(root, "scope", "coverage", "documentSession", "document")), SnapshotReader.Validate(root, dimension));
    public static ComparisonRequest Request(string mode, string a, string b) => new(mode, "Test_AI-Work",
        new(a, "Старая"), new(b, "Новая"), new(true, "Синтетические тестовые данные", "Одинаковый тестовый вид", true));
}
