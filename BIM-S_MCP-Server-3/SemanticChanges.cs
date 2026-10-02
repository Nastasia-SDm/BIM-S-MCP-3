using System.Text.Json.Nodes;

namespace BimS.Mcp3;

// Presentation contract only. The exact comparison and saved reports remain unchanged.
public static class SemanticChanges
{
    private static readonly string[] ValueFields = ["displayValue", "convertedValue", "rawValue"];

    public static object Section(SectionResult section) => new
    {
        section.Status, section.Counts,
        changed = (section.Changed ?? []).Select(Element).ToArray(),
        added = (section.Added ?? []).Select(Identity).ToArray(),
        removed = (section.Removed ?? []).Select(Identity).ToArray()
    };

    private static string Label(ElementResult element)
    {
        var state = element.NewState ?? element.OldState;
        var category = state?["category"]?.GetValue<string>();
        return category switch { "Стены" or "Walls" => "Стена", null or "" => "Элемент", _ => category };
    }

    private static object Identity(ElementResult element) => new { element.ElementId, elementLabel = Label(element) };

    public static object Element(ElementResult element)
    {
        var changes = new List<object>();
        void Walk(JsonNode? old, bool oldExists, JsonNode? newer, bool newExists, string path)
        {
            if (oldExists == newExists && Canonical.Equal(old, newer)) return;
            var a = old as JsonObject; var b = newer as JsonObject;
            var parameter = b ?? a;
            if (parameter != null && parameter.ContainsKey("parameterId") && parameter.ContainsKey("ownerElementId") && parameter.ContainsKey("rawValue"))
            {
                var key = $"{parameter["source"]}:{parameter["ownerElementId"]}:{parameter["parameterId"]}";
                var name = parameter["name"]!.GetValue<string>();
                if (a == null || b == null || ValueFields.Any(k => !Canonical.Equal(a[k], b[k])))
                {
                    var before = Display(a); var after = Display(b);
                    // Rounded display strings can hide a real saved-value difference.
                    object? exactValues = before == after && !Canonical.Equal(a?["rawValue"], b?["rawValue"])
                        ? new { old = Text(a?["rawValue"]), @new = Text(b?["rawValue"]) } : null;
                    changes.Add(new { parameterKey = key, name, old = before, @new = after, exactValues });
                }
                if (a != null && b != null)
                    foreach (var diff in Canonical.Diff(a, b).Where(d => !ValueFields.Contains(d.Path[1..])))
                        changes.Add(new { parameterKey = key, name = name + " — " + MetadataLabel(diff.Path[1..]),
                            old = Field(diff.Old), @new = Field(diff.New) });
                return;
            }
            if (a != null && b != null)
            {
                foreach (var key in a.Select(p => p.Key).Union(b.Select(p => p.Key)).Order(StringComparer.Ordinal))
                    Walk(a[key], a.ContainsKey(key), b[key], b.ContainsKey(key), path + "/" + key);
                return;
            }
            changes.Add(new { name = path.TrimStart('/'), old = oldExists ? Text(old) : "(отсутствует)",
                @new = newExists ? Text(newer) : "(отсутствует)" });
        }
        Walk(element.OldState, element.OldState != null, element.NewState, element.NewState != null, "");
        return new { element.ElementId, elementLabel = Label(element), semanticChanges = changes };
    }

    private static string Field(FieldValue value) => value.Exists ? Text(value.Value) : "(отсутствует)";
    private static string MetadataLabel(string field) => field switch
    {
        "name" => "название", "unitTypeId" => "единица измерения", "hasValue" => "наличие значения",
        "status" => "статус", "isReadOnly" => "только чтение", "storageType" => "тип хранения",
        "dataTypeId" => "тип данных", "builtInParameterNames" => "системные имена", _ => field
    };
    private static string Text(JsonNode? value) => value == null ? "(нет значения)"
        : value is JsonValue v && v.TryGetValue<string>(out var s) ? s : value.ToJsonString();
    private static string Display(JsonObject? parameter)
    {
        if (parameter == null) return "(отсутствует)";
        foreach (var field in new[] { "displayValue", "convertedValue", "rawValue" })
        {
            var value = parameter[field];
            if (value == null || field == "displayValue" && string.IsNullOrWhiteSpace(Text(value))) continue;
            var text = Text(value);
            if (field == "convertedValue" && parameter["unitTypeId"] is { } unit)
                text += " " + Text(unit);
            return text;
        }
        return "(нет значения)";
    }
}
