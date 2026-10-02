using System.Text.Json.Nodes;
using BimS.Mcp3;

static class SemanticTests
{
    public static void Run(Action<bool, string> check, string output)
    {
        var old = Fixtures.Model(13826754, 13826755, 13826756);
        var row = old["elements"]![0]!;
        var parameters = row["instanceParameters"]!.AsArray();
        parameters.Clear();
        var names = new[] { "Неприсоединенная высота", "Площадь", "Объём" };
        var before = new[] { "1000 мм", "1.00 м²", "0.25 м³" };
        var after = new[] { "2650.888 мм", "2.65 м²", "0.66 м³" };
        for (var i = 0; i < 3; i++)
        {
            var p = Fixtures.Parameter(13826754, pid: -i - 1);
            p["name"] = names[i]; p["displayValue"] = before[i]; parameters.Add(p);
        }
        var newer = (JsonObject)old.DeepClone();
        for (var i = 0; i < 3; i++)
        {
            var p = newer["elements"]![0]!["instanceParameters"]![i]!;
            p["displayValue"] = after[i]; p["rawValue"] = 2; p["convertedValue"] = 2;
        }
        JsonObject Project(JsonObject a, JsonObject b, string dimension = "3d") =>
            Data.Object(Data.Node(SemanticChanges.Section(ComparisonEngine.Compare(Fixtures.Snapshot(a, dimension), Fixtures.Snapshot(b, dimension)))));
        var result = Project(old, newer);
        var changed = result["changed"]!.AsArray();
        var changes = changed[0]!["semanticChanges"]!.AsArray();
        check(changed.Count == 1 && changes.Count == 3, "nine value differences become three semantic changes");
        check(changed[0]!["elementId"]!.GetValue<long>() == 13826754 && changed[0]!["elementLabel"]!.GetValue<string>() == "Стена", "wall identity retained");
        foreach (var i in Enumerable.Range(0, 3))
            check(changes.Any(c => c!["name"]!.GetValue<string>() == names[i] && c["old"]!.GetValue<string>() == before[i] && c["new"]!.GetValue<string>() == after[i]), "display precision: " + names[i]);
        check(!result.ContainsKey("unchanged") && !result.ToJsonString().Contains("rawValue") && !result.ToJsonString().Contains("oldState"), "no raw states or unchanged fields in presentation");
        File.WriteAllText(Path.Combine(output, "semantic-example.json"), result.ToJsonString(Data.Json));
        File.WriteAllText(Path.Combine(output, "semantic-example.txt"), $"{changed[0]!["elementLabel"]} ID: {changed[0]!["elementId"]}\n\n" +
            string.Join("\n", changes.Select(c => $"- {c!["name"]}: {c["old"]} → {c["new"]}")));
        newer["elements"]![1]!["instanceParameters"]![0]!["rawValue"] = 4;
        result = Project(old, newer);
        check(result["changed"]!.AsArray().Count == 2 && result["changed"]![1]!["semanticChanges"]![0]!["exactValues"]!["new"]!.GetValue<string>() == "4", "multiple IDs and rounded display preserve exact change");
        var a = old["elements"]![0]!["instanceParameters"]![0]!;
        var b = newer["elements"]![0]!["instanceParameters"]![0]!;
        a["displayValue"] = null; b["displayValue"] = "";
        check(Project(old, newer)["changed"]![0]!["semanticChanges"]!.AsArray().Any(c => c!["old"]!.GetValue<string>() == "1 ft" && c["new"]!.GetValue<string>() == "2 ft"), "converted fallback includes known unit");
        a["convertedValue"] = null; b["convertedValue"] = null;
        check(Project(old, newer)["changed"]![0]!["semanticChanges"]!.AsArray().Any(c => c!["old"]!.GetValue<string>() == "1" && c["new"]!.GetValue<string>() == "2"), "raw fallback does not invent units");
        var graph = Fixtures.Documentation(); var graphNew = (JsonObject)graph.DeepClone();
        graphNew["parameters"]![0]!["InstanceParameters"]![0]!["displayValue"] = "2";
        check(Project(graph, graphNew, "2d")["changed"]![0]!["semanticChanges"]!.AsArray().Count == 1, "2d nested parameter aggregation");
        check(Project(old, old)["changed"]!.AsArray().Count == 0, "unchanged snapshots have no semantic changes");
        var edge = Fixtures.Model(1); var edgeNew = (JsonObject)edge.DeepClone();
        var edgeParameters = edgeNew["elements"]![0]!["instanceParameters"]!.AsArray();
        var added = Fixtures.Parameter(1, pid: -2); added["displayValue"] = "0";
        edgeParameters.Add(added);
        var addition = Project(edge, edgeNew)["changed"]![0]!["semanticChanges"]!.AsArray();
        check(addition.Count == 1 && addition[0]!["old"]!.GetValue<string>() == "(отсутствует)" && addition[0]!["new"]!.GetValue<string>() == "0", "added parameter is one change and zero is preserved");
        check(Project(edgeNew, edge)["changed"]![0]!["semanticChanges"]![0]!["new"]!.GetValue<string>() == "(отсутствует)", "removed parameter is one change");
        edgeNew["elements"]![0]!["instanceParameters"]![0]!["displayValue"] = "3";
        edgeNew["elements"]![0]!["typeParameters"]![0]!["displayValue"] = "4";
        var sameNames = Project(edge, edgeNew)["changed"]![0]!["semanticChanges"]!.AsArray();
        check(sameNames.Count == 3 && sameNames.Select(c => c!["parameterKey"]!.GetValue<string>()).Distinct().Count() == 3,
            "same names do not merge distinct parameter IDs or instance/type parameters");
        edgeNew = (JsonObject)edge.DeepClone();
        edgeNew["elements"]![0]!["instanceParameters"]![0]!["isReadOnly"] = true;
        var metadata = Project(edge, edgeNew)["changed"]![0]!["semanticChanges"]!.AsArray();
        check(metadata.Count == 1 && metadata[0]!["name"]!.GetValue<string>().EndsWith("только чтение"), "metadata change does not invent a value change");
    }
}
