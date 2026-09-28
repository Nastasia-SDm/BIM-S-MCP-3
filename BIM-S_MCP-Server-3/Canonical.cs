using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BimS.Mcp3;

public static class Canonical
{
    // Exact base-ten normalization, never through double/decimal (including Int64 IDs).
    public static string Number(string text)
    {
        var parts = text.ToLowerInvariant().Split('e');
        var exponent = parts.Length == 2 ? BigInteger.Parse(parts[1], CultureInfo.InvariantCulture) : BigInteger.Zero;
        var mantissa = parts[0]; var negative = mantissa.StartsWith('-');
        if (negative) mantissa = mantissa[1..];
        var dot = mantissa.IndexOf('.');
        if (dot >= 0) { exponent -= mantissa.Length - dot - 1; mantissa = mantissa.Remove(dot, 1); }
        mantissa = mantissa.TrimStart('0');
        if (mantissa.Length == 0) return "0";
        var trimmed = mantissa.TrimEnd('0'); exponent += mantissa.Length - trimmed.Length;
        return (negative ? "-" : "") + trimmed + "e" + exponent.ToString(CultureInfo.InvariantCulture);
    }
    public static JsonNode? Normalize(JsonNode? value)
    {
        if (value is JsonObject o)
        {
            var result = new JsonObject();
            foreach (var p in o.OrderBy(p => p.Key, StringComparer.Ordinal)) result[p.Key] = Normalize(p.Value);
            return result;
        }
        if (value is JsonArray a) return new JsonArray(a.Select(Normalize).ToArray());
        return value?.DeepClone();
    }
    public static bool Equal(JsonNode? a, JsonNode? b)
    {
        if (a is null || b is null) return a is null && b is null;
        if (a is JsonObject ao && b is JsonObject bo)
            return ao.Count == bo.Count && ao.All(p => bo.ContainsKey(p.Key) && Equal(p.Value, bo[p.Key]));
        if (a is JsonArray aa && b is JsonArray ba)
            return aa.Count == ba.Count && aa.Zip(ba).All(p => Equal(p.First, p.Second));
        if (a is not JsonValue || b is not JsonValue) return false;
        var ak = a.GetValueKind(); var bk = b.GetValueKind();
        if (ak != bk) return false;
        if (ak == JsonValueKind.Number) return Number(a.ToJsonString()) == Number(b.ToJsonString());
        if (ak == JsonValueKind.String) return a.GetValue<string>() == b.GetValue<string>();
        return a.ToJsonString() == b.ToJsonString();
    }
    public static List<Difference> Diff(JsonObject old, JsonObject newer, CancellationToken token = default)
    {
        var differences = new List<Difference>();
        void Walk(JsonNode? a, bool ae, JsonNode? b, bool be, string path)
        {
            token.ThrowIfCancellationRequested();
            if (ae == be && Equal(a, b)) return;
            if (ae && be && a is JsonObject ao && b is JsonObject bo)
            {
                foreach (var key in ao.Select(p => p.Key).Union(bo.Select(p => p.Key)).Order(StringComparer.Ordinal))
                    Walk(ao[key], ao.ContainsKey(key), bo[key], bo.ContainsKey(key), path + "/" + key.Replace("~", "~0").Replace("/", "~1"));
            }
            else differences.Add(new(path, new(ae, a?.DeepClone()), new(be, b?.DeepClone())));
        }
        Walk(old, true, newer, true, "");
        return differences;
    }
}
