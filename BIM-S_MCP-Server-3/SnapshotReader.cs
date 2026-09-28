using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace BimS.Mcp3;

public static class SnapshotReader
{
    public static readonly string[] EntityArrays = ["sheets", "views", "placements", "elements"];
    public static async Task<Snapshot> ReadAsync(string path, string dimension, CancellationToken token = default)
    {
        Data.Require(Path.IsPathFullyQualified(path) && Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase), "Нужен абсолютный путь к JSON.");
        var full = Path.GetFullPath(path);
        var bytes = await File.ReadAllBytesAsync(full, token);
        var root = Data.Parse(bytes);
        token.ThrowIfCancellationRequested();
        var states = Validate(root, dimension, token);
        var metadata = Data.Pick(root, "schemaVersion", "contractVersion", "server", "snapshotId", "documentSession", "document",
            "startedAtUtc", "completedAtUtc", "status", "scope", "coverage", "counts", "warnings");
        return new(dimension, new(full, Convert.ToHexStringLower(SHA256.HashData(bytes)), metadata), states);
    }

    public static SortedDictionary<long, JsonObject> Validate(JsonObject root, string dimension, CancellationToken token = default)
    {
        Data.Require(dimension is "3d" or "2d", "Неизвестный раздел.");
        Data.Require(Data.Long(root["schemaVersion"]) == 1, "Поддерживается schemaVersion=1.");
        Data.GuidField(root, "documentSession");
        Data.Require(Data.Text(root, "status") == "complete", "Сравнение требует полного снимка (complete).");
        Data.Empty(root, "errors"); Data.Empty(root, "unprocessedElementIds");
        var start = DateTimeOffset.Parse(Data.Text(root, "startedAtUtc"), System.Globalization.CultureInfo.InvariantCulture);
        var end = DateTimeOffset.Parse(Data.Text(root, "completedAtUtc"), System.Globalization.CultureInfo.InvariantCulture);
        Data.Require(start.Offset == TimeSpan.Zero && end.Offset == TimeSpan.Zero && start <= end, "Неверный интервал чтений UTC.");
        var requested = IdSet(Data.Array(root, "requestedElementIds"));
        var states = new SortedDictionary<long, JsonObject>();
        if (dimension == "3d")
        {
            Data.Require(!root.ContainsKey("server") && !root.ContainsKey("parameters"), "Ожидается снимок MCP-1.");
            foreach (var n in Data.Array(root, "elements"))
            {
                token.ThrowIfCancellationRequested();
                var row = Data.Object(n); var id = Data.Id(row);
                var state = ParameterRow(row, false);
                Data.Require(states.TryAdd(id, state), "Повтор ElementId: " + id);
            }
        }
        else
        {
            Data.Require(Data.Text(root, "server") == "BIM-S_MCP-Server-2" && Data.Long(root["contractVersion"]) == 1, "Ожидается контракт MCP-2 версии 1.");
            Data.GuidField(root, "snapshotId");
            var document = Data.Object(root["document"]);
            Data.Text(document, "title"); Data.Text(document, "revitVersion");
            foreach (var warning in Data.Array(root, "warnings")) Data.Require(warning?.GetValueKind() == System.Text.Json.JsonValueKind.String, "Неверное предупреждение.");
            NormalizeScope(Data.Object(root["scope"]));
            Coverage(root);
            var counts = Data.Object(root["counts"]);
            var uniqueIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var kind in EntityArrays)
            {
                var rows = Data.Array(root, kind);
                Data.Require(Data.Long(counts[kind]) == rows.Count, "Неверный counts." + kind);
                foreach (var n in rows)
                {
                    token.ThrowIfCancellationRequested();
                    var row = Data.Object(n); var id = Data.Id(row);
                    Ok(row); Data.Text(row, "kind"); Data.Text(row, "class");
                    Data.Fields(row, "uniqueId", "name", "categoryId", "category", "familyName", "typeName", "typeId", "ownerViewId");
                    foreach (var key in new[] { "uniqueId", "name", "category", "familyName", "typeName" }) NullableText(row, key);
                    foreach (var key in new[] { "categoryId", "typeId", "ownerViewId" }) if (row[key] != null) Data.Long(row[key]);
                    if (row["uniqueId"]?.GetValue<string>() is { Length: > 0 } uid)
                        Data.Require(uniqueIds.Add(uid), "Повтор uniqueId.");
                    var properties = Data.Object(row["properties"]);
                    foreach (var p in properties)
                    {
                        var value = Data.Object(p.Value); Data.Fields(value, "value"); Data.Text(value, "source");
                        Data.Require(Data.Text(value, "status") is "ok" or "noValue" or "notApplicable" or "unsupported", "Ошибка свойства: " + p.Key);
                        if (value.ContainsKey("unit")) NullableText(value, "unit");
                        if (value.ContainsKey("coordinateSystem")) NullableText(value, "coordinateSystem");
                    }
                    var state = Data.Pick(row, "uniqueId", "kind", "class", "name", "categoryId", "category", "familyName", "typeName", "typeId", "ownerViewId", "properties");
                    state["entityArray"] = kind;
                    if (state["properties"]?["dependentViewIds"] is JsonObject dependent && dependent["value"] is JsonArray ids)
                        dependent["value"] = Data.Node(IdSet(ids).Order().ToArray());
                    state["outgoingRelations"] = new JsonObject();
                    Data.Require(states.TryAdd(id, state), "Повтор ElementId в графе: " + id);
                }
            }
            var relationKeys = new HashSet<(string, long, long)>();
            foreach (var n in Data.Array(root, "relations"))
            {
                var r = Data.Object(n); var kind = Data.Text(r, "kind"); var from = Data.Id(r, "fromId"); var to = Data.Id(r, "toId");
                Data.Require(states.ContainsKey(from) && states.ContainsKey(to), "Связь с отсутствующей сущностью.");
                Data.Require(relationKeys.Add((kind, from, to)), "Повтор связи.");
                var key = System.Text.Json.JsonSerializer.Serialize(new object[] { kind, from, to });
                states[from]["outgoingRelations"]![key] = r.DeepClone();
            }
            var returned = new HashSet<long>();
            foreach (var n in Data.Array(root, "parameters"))
            {
                token.ThrowIfCancellationRequested();
                var row = Data.Object(n); var id = Data.Id(row, "ElementId");
                Data.Require(states.ContainsKey(id) && returned.Add(id), "Лишний или повторный ElementId параметров.");
                var parameters = ParameterRow(row, true);
                // Duplicate descriptive fields are exported by both readers and must not contradict.
                foreach (var key in new[] { "category", "familyName", "typeName", "typeId" })
                    Data.Require(Canonical.Equal(states[id][key], parameters[key]), "Несогласованность графа и параметров: " + id + "/" + key);
                states[id]["parameters"] = parameters;
            }
            Data.Require(returned.SetEquals(states.Keys), "Для полного сравнения нужны параметры всех сущностей графа.");
        }
        Data.Require(requested.SetEquals(states.Keys), "requestedElementIds не совпадает с составом снимка.");
        foreach (var id in states.Keys.ToArray()) states[id] = Data.Object(Canonical.Normalize(states[id]));
        return states;
    }

    private static void NullableText(JsonObject row, string key)
    { if (row[key] != null) Data.Text(row, key); }
    private static void Ok(JsonObject row)
    { Data.Require(Data.Text(row, "status") == "ok", "Неполная строка данных."); Data.Empty(row, "errors"); }
    public static HashSet<long> IdSet(JsonArray array)
    {
        var ids = new HashSet<long>();
        foreach (var n in array) { var id = Data.Long(n); Data.Require(id > 0 && ids.Add(id), "Неверный или повторный ID."); }
        return ids;
    }
    public static JsonObject NormalizeScope(JsonObject scope)
    {
        var kind = Data.Text(scope, "kind"); var sheets = IdSet(Data.Array(scope, "sheetIds")); var views = IdSet(Data.Array(scope, "viewIds"));
        Data.Require(kind switch { "document" => sheets.Count == 0 && views.Count == 0, "sheets" => sheets.Count > 0 && views.Count == 0,
            "views" => views.Count > 0 && sheets.Count == 0, _ => false }, "Неверная область scope.");
        Data.Require(scope["includeTemplates"]?.GetValueKind() is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False, "Нет includeTemplates.");
        return new() { ["kind"] = kind, ["sheetIds"] = Data.Node(sheets.Order().ToArray()), ["viewIds"] = Data.Node(views.Order().ToArray()), ["includeTemplates"] = scope["includeTemplates"]!.DeepClone() };
    }
    public static JsonArray Coverage(JsonObject root)
    {
        var values = Data.Array(root, "coverage").Select(n => n?.GetValue<string>()).ToArray();
        Data.Require(values.Length > 0 && values.All(v => v is "sheets" or "views" or "elements") && values.Distinct().Count() == values.Length, "Неверный coverage.");
        return new JsonArray(values.Order(StringComparer.Ordinal).Select(v => (JsonNode?)JsonValue.Create(v)).ToArray());
    }
    private static JsonObject ParameterRow(JsonObject row, bool pascal)
    {
        Ok(row); var id = Data.Id(row, pascal ? "ElementId" : "elementId");
        string Key(string key) => pascal ? char.ToUpperInvariant(key[0]) + key[1..] : key;
        var result = new JsonObject();
        foreach (var key in new[] { "category", "familyName", "typeName", "typeId" })
        {
            Data.Fields(row, Key(key));
            if (key != "typeId") NullableText(row, Key(key)); else if (row[Key(key)] != null) Data.Long(row[Key(key)]);
            result[key] = row[Key(key)]?.DeepClone();
        }
        foreach (var source in new[] { "instance", "type" })
        {
            var parameters = new SortedDictionary<(string Source, long Owner, long Parameter), JsonObject>();
            foreach (var n in Data.Array(row, Key(source + "Parameters")))
            {
                var p = Data.Object(n); var pid = Data.Long(p["parameterId"]); var owner = Data.Id(p, "ownerElementId");
                Data.Require(Data.Text(p, "source") == source && owner == (source == "instance" ? id : row[Key("typeId")] == null ? null : Data.Long(row[Key("typeId")])), "Неверный владелец параметра.");
                Data.Require(Data.Text(p, "status") is "ok" or "noValue", "Ошибка или неполный параметр."); Data.Empty(p, "errors");
                Data.Fields(p, "name", "storageType", "hasValue", "isReadOnly", "dataTypeId", "rawValue", "displayValue", "unitTypeId", "convertedValue");
                Data.Text(p, "name"); Data.Text(p, "dataTypeId"); NullableText(p, "displayValue"); NullableText(p, "unitTypeId");
                var storage = Data.Text(p, "storageType");
                Data.Require(storage is "None" or "Integer" or "Double" or "String" or "ElementId", "Неверный storageType.");
                foreach (var key in new[] { "hasValue", "isReadOnly" })
                    Data.Require(p[key]?.GetValueKind() is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False, "Неверный " + key);
                if (p["rawValue"] != null)
                {
                    if (storage is "Integer" or "ElementId") Data.Long(p["rawValue"]);
                    else if (storage == "String") Data.Text(p, "rawValue");
                    else Data.Require(storage == "Double" && p["rawValue"]!.GetValueKind() == System.Text.Json.JsonValueKind.Number, "Неверный rawValue.");
                }
                if (p["convertedValue"] != null) Data.Require(p["convertedValue"]!.GetValueKind() == System.Text.Json.JsonValueKind.Number, "Неверный convertedValue.");
                var aliases = Data.Array(p, "builtInParameterNames").Select(n => n?.GetValue<string>() ?? throw Data.Error("Неверный alias.")).ToArray();
                var copy = (JsonObject)p.DeepClone(); copy.Remove("errors");
                copy["builtInParameterNames"] = Data.Node(aliases.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
                Data.Require(parameters.TryAdd((source, owner, pid), copy), "Повтор ключа параметра.");
            }
            var map = new JsonObject();
            foreach (var (key, value) in parameters) map[$"{key.Source}:{key.Owner}:{key.Parameter}"] = value;
            result[source + "Parameters"] = map;
        }
        return result;
    }
}
