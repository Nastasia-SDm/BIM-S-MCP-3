using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace BimS.Mcp3;

public static class ComparisonReport
{
    public static void Validate(ComparisonResult result, CancellationToken token = default)
    {
        Data.Require(result.SchemaVersion == 1 && result.PolicyVersion == Data.Policy && Guid.TryParseExact(result.ComparisonId, "N", out _), "Неподдерживаемый результат сравнения.");
        var requested = ComparisonEngine.ValidateRequest(result.Request);
        Data.Require(result.Sections != null && result.Sections.Count == 2 && result.Sections.ContainsKey("3d") && result.Sections.ContainsKey("2d") && result.Limitations != null, "Неверные разделы результата.");
        foreach (var d in new[] { "3d", "2d" })
        {
            var s = result.Sections[d];
            if (!requested.Contains(d))
            { Data.Require(s == new SectionResult("notRequested"), "Незапрошенный раздел содержит данные."); continue; }
            Data.Require(s.Status == "complete" && s.Counts != null && s.OldSource != null && s.NewSource != null && s.Unchanged != null && s.Changed != null && s.Added != null && s.Removed != null, "Неполный результат.");
            foreach (var source in new[] { s.OldSource!, s.NewSource! })
                Data.Require(Path.IsPathFullyQualified(source.Path) && source.Sha256.Length == 64 && source.Sha256.All(Uri.IsHexDigit) && source.Metadata != null, "Неверное происхождение результата.");
            var seen = new HashSet<long>();
            foreach (var (kind, rows) in Groups(s)) foreach (var row in rows)
            {
                token.ThrowIfCancellationRequested();
                Data.Require(row.ElementId > 0 && seen.Add(row.ElementId) && row.Differences != null, "Повтор или неверный ElementId результата.");
                Data.Require(kind switch
                {
                    "added" => row.OldState == null && row.NewState != null && row.Differences.Count == 0,
                    "removed" => row.OldState != null && row.NewState == null && row.Differences.Count == 0,
                    "unchanged" => row.OldState != null && row.NewState != null && Canonical.Equal(row.OldState, row.NewState) && row.Differences.Count == 0,
                    _ => row.OldState != null && row.NewState != null && !Canonical.Equal(row.OldState, row.NewState)
                        && Canonical.Equal(Data.Node(Canonical.Diff(row.OldState, row.NewState, token)), Data.Node(row.Differences))
                }, "Группа не соответствует состоянию элемента.");
            }
            var c = s.Counts!;
            Data.Require(c.Unchanged == s.Unchanged!.Count && c.Changed == s.Changed!.Count && c.Added == s.Added!.Count && c.Removed == s.Removed!.Count
                && c.Old == c.Unchanged + c.Changed + c.Removed && c.New == c.Unchanged + c.Changed + c.Added, "Неверные счётчики результата.");
        }
    }
    private static IEnumerable<(string Kind, List<ElementResult> Rows)> Groups(SectionResult s)
    { yield return ("unchanged", s.Unchanged!); yield return ("changed", s.Changed!); yield return ("added", s.Added!); yield return ("removed", s.Removed!); }
    private static string E(object? value) => WebUtility.HtmlEncode(value?.ToString() ?? "null");
    private static string Pretty(JsonNode? node) => E(node?.ToJsonString(Data.Json) ?? "null");
    public static string Render(ComparisonResult result, CancellationToken token = default)
    {
        Validate(result, token);
        var html = new StringBuilder("<!doctype html><html lang='ru'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'><title>BIM-S — Сравнение версий модели</title><style>");
        html.Append("*{box-sizing:border-box}body{margin:0;background:#eef2f6;color:#17243a;font:16px/1.5 Segoe UI,Arial,sans-serif}main{max-width:1400px;margin:48px auto;padding:0 24px}header{background:#13283f;color:white;padding:32px;border-radius:20px}h1{margin:0;font-size:30px}.total{font-size:28px;font-weight:700;color:#73e1c0}.table,.info{margin-top:24px;background:white;border-radius:16px;overflow:auto;padding:20px;box-shadow:0 8px 30px #13283f12}table{width:100%;border-collapse:collapse}th,td{padding:12px;border-bottom:1px solid #edf0f4;vertical-align:top;text-align:left}th{background:#dfe8f1}tr:hover{background:#f3faf8}summary{cursor:pointer;color:#14765e}pre{white-space:pre-wrap;overflow-wrap:anywhere;max-width:100%}.id{font-family:Consolas,monospace}h3{padding:14px;border-radius:8px}.unchanged{background:#eceff1}.changed{background:#fff4cc}.added{background:#e2f2df}.removed{background:#f8e1e7}.info{overflow-wrap:anywhere}@media print{.table{overflow:visible}header{color:#17243a;background:white}}</style></head><body><main><header><h1>BIM-S — Сравнение версий модели</h1>");
        html.Append($"<p>{E(result.Request.ModelKey)}</p><div class='total'>{E(result.Request.OldVersion.Label)} → {E(result.Request.NewVersion.Label)}</div><p>{E(result.Request.OldVersion.VersionId)} → {E(result.Request.NewVersion.VersionId)}</p></header>");
        html.Append("<section class='info'><h2>Сопоставление и ограничения</h2><p>Сопоставление подтверждено пользователем: " + E(result.Request.Pairing.Method) + "</p>");
        if (result.Request.Mode != "2d") html.Append("<p>Область 3D: " + E(result.Request.Pairing.Scope3dDescription) + "</p>");
        html.Append("<ul>"); foreach (var text in result.Limitations) html.Append("<li>" + E(text) + "</li>"); html.Append("</ul></section>");
        foreach (var d in new[] { "3d", "2d" })
        {
            var s = result.Sections[d]; html.Append("<h2>" + (d == "3d" ? "3D — Модель" : "2D — Документация") + "</h2>");
            if (s.Status == "notRequested") { html.Append("<p>Не запрошен.</p>"); continue; }
            html.Append("<details class='info'><summary>Исходные файлы, SHA-256 и метаданные</summary>");
            foreach (var source in new[] { s.OldSource!, s.NewSource! }) html.Append("<p>" + E(source.Path) + "</p><p>SHA-256: " + E(source.Sha256) + "</p><pre>" + Pretty(source.Metadata) + "</pre>");
            html.Append("</details>");
            foreach (var (kind, rows) in Groups(s))
            {
                var label = kind switch { "unchanged" => "НЕ ИЗМЕНИЛИСЬ", "changed" => "ИЗМЕНИЛИСЬ", "added" => "ПОЯВИЛИСЬ", _ => "УДАЛИЛИСЬ" };
                html.Append($"<section class='table'><h3 class='{kind}'>{label} — {rows.Count}</h3><table><thead><tr><th>ElementId</th><th>Элемент</th><th>Состояние</th></tr></thead><tbody>");
                foreach (var row in rows.OrderBy(r => r.ElementId))
                {
                    token.ThrowIfCancellationRequested(); var state = row.NewState ?? row.OldState!;
                    html.Append($"<tr><td class='id'>{row.ElementId}</td><td>{E(state["name"] ?? state["typeName"])}<br>{E(state["category"])}<br>{E(state["kind"])}</td><td>");
                    if (kind == "changed")
                    {
                        html.Append("<details><summary>Различия</summary><table><thead><tr><th>Поле</th><th>Было</th><th>Стало</th></tr></thead><tbody>");
                        foreach (var diff in row.Differences) html.Append("<tr><td>" + E(diff.Path) + "</td><td><pre>" + (diff.Old.Exists ? Pretty(diff.Old.Value) : "Поле отсутствовало") + "</pre></td><td><pre>" + (diff.New.Exists ? Pretty(diff.New.Value) : "Поле отсутствует") + "</pre></td></tr>");
                        html.Append("</tbody></table></details>");
                    }
                    // One state per element plus factual differences; do not embed source snapshots again.
                    html.Append("<details><summary>" + (kind == "removed" ? "Старое состояние" : "Сохранённое состояние") + "</summary><pre>" + Pretty(state) + "</pre></details></td></tr>");
                }
                html.Append("</tbody></table>"); if (rows.Count == 0) html.Append("<p>Нет элементов.</p>"); html.Append("</section>");
            }
        }
        html.Append("<footer><p>Сравнение " + E(result.ComparisonId) + "; UTC " + E(result.CreatedAtUtc.ToString("O")) + "; политика " + E(result.PolicyVersion) + "</p></footer></main></body></html>");
        return html.ToString();
    }
}
