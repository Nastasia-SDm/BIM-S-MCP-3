using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BimS.Mcp3;

public sealed record VersionInput(string VersionId, string Label, string? Json3dPath = null, string? Json2dPath = null);
public sealed record PairingInput(bool Confirmed, string Method, string? Scope3dDescription = null,
    bool Comparable3dScopeConfirmed = false);
public sealed record ComparisonRequest(string Mode, string ModelKey, VersionInput OldVersion,
    VersionInput NewVersion, PairingInput Pairing);
public sealed record SourceInfo(string Path, string Sha256, JsonObject Metadata);
public sealed record Snapshot(string Dimension, SourceInfo Source, SortedDictionary<long, JsonObject> States);
public sealed record FieldValue(bool Exists, JsonNode? Value);
public sealed record Difference(string Path, FieldValue Old, FieldValue New);
public sealed record ElementResult(long ElementId, JsonObject? OldState, JsonObject? NewState, List<Difference> Differences);
public sealed record Counts(int Old, int New, int Unchanged, int Changed, int Added, int Removed);
public sealed record SectionResult(string Status, SourceInfo? OldSource = null, SourceInfo? NewSource = null,
    Counts? Counts = null, List<ElementResult>? Unchanged = null, List<ElementResult>? Changed = null,
    List<ElementResult>? Added = null, List<ElementResult>? Removed = null);
public sealed record ComparisonResult(int SchemaVersion, string PolicyVersion, string ComparisonId,
    DateTime CreatedAtUtc, ComparisonRequest Request, List<string> Limitations,
    Dictionary<string, SectionResult> Sections);

public static class Data
{
    public const string Policy = "saved-state-v1";
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };
    public static InvalidDataException Error(string message) => new(message);
    public static JsonObject Object(JsonNode? n) => n as JsonObject ?? throw Error("Ожидается JSON object.");
    public static JsonArray Array(JsonObject o, string key) => o[key] as JsonArray ?? throw Error("Нет массива: " + key);
    public static string Text(JsonObject o, string key) => o[key] is JsonValue v && v.TryGetValue<string>(out var s)
        ? s : throw Error("Нет строки: " + key);
    public static long Long(JsonNode? n) => n is JsonValue v && v.GetValueKind() == JsonValueKind.Number
        && long.TryParse(v.ToJsonString(), System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out var id)
        ? id : throw Error("Ожидается целое Int64.");
    public static long Id(JsonObject o, string key = "elementId") => Long(o[key]) is > 0 and var id
        ? id : throw Error("ID должен быть положительным: " + key);
    public static void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool condition, string message) { if (!condition) throw Error(message); }
    public static void Empty(JsonObject o, string key) => Require(Array(o, key).Count == 0, "Неполные данные: " + key);
    public static void GuidField(JsonObject o, string key) => Require(Guid.TryParseExact(Text(o, key), "N", out _), "Неверный " + key);
    public static void Fields(JsonObject o, params string[] names)
    { foreach (var name in names) Require(o.ContainsKey(name), "Отсутствует поле: " + name); }
    public static JsonObject Pick(JsonObject o, params string[] keys)
    {
        var result = new JsonObject();
        foreach (var key in keys) if (o.ContainsKey(key)) result[key] = o[key]?.DeepClone();
        return result;
    }
    public static JsonNode? Node<T>(T value) => JsonSerializer.SerializeToNode(value, Json);

    // Reject duplicate object keys before JsonNode can hide them. Also bounds nesting.
    public static JsonObject Parse(byte[] bytes)
    {
        ReadOnlySpan<byte> input = bytes;
        if (input.StartsWith(new byte[] { 239, 187, 191 })) input = input[3..];
        using var doc = JsonDocument.Parse(input.ToArray(), new JsonDocumentOptions { MaxDepth = 128 });
        void Visit(JsonElement e)
        {
            if (e.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var p in e.EnumerateObject()) { Require(names.Add(p.Name), "Повтор JSON-поля: " + p.Name); Visit(p.Value); }
            }
            else if (e.ValueKind == JsonValueKind.Array) foreach (var v in e.EnumerateArray()) Visit(v);
        }
        Visit(doc.RootElement);
        return Object(JsonNode.Parse(doc.RootElement.GetRawText(), documentOptions: new() { MaxDepth = 128 }));
    }
}
