namespace BimS.Mcp3;

public static class ComparisonEngine
{
    public static string[] ValidateRequest(ComparisonRequest request, VersionResolver? versions = null, bool checkSourceFiles = true)
    {
        Data.Require(request != null && request.OldVersion != null && request.NewVersion != null && request.Pairing != null, "Нужны версии и сведения о сопоставлении.");
        Data.Require(request!.Mode is "3d" or "2d" or "both", "mode: 3d, 2d или both.");
        Data.Require(!string.IsNullOrWhiteSpace(request.ModelKey), "Нужен modelKey одной линии версий.");
        Data.Require(request.Pairing.Confirmed && !string.IsNullOrWhiteSpace(request.Pairing.Method), "Подтвердите принадлежность файлов модели и версиям; укажите pairing.method.");
        Data.Require(!string.IsNullOrWhiteSpace(request.OldVersion.VersionId) && !string.IsNullOrWhiteSpace(request.NewVersion.VersionId)
            && request.OldVersion.VersionId != request.NewVersion.VersionId, "Нужны разные versionId.");
        Data.Require(!string.IsNullOrWhiteSpace(request.OldVersion.Label) && !string.IsNullOrWhiteSpace(request.NewVersion.Label), "Нужны подписи версий.");
        string[] dimensions = request.Mode == "both" ? ["3d", "2d"] : [request.Mode];
        if (dimensions.Contains("3d")) Data.Require(request.Pairing.Comparable3dScopeConfirmed && !string.IsNullOrWhiteSpace(request.Pairing.Scope3dDescription), "Для 3D подтвердите сопоставимость области и опишите правило отбора.");
        if (checkSourceFiles)
        foreach (var d in dimensions) foreach (var version in new[] { request.OldVersion, request.NewVersion })
            Data.Require(File.Exists((versions ?? new VersionResolver()).PathFor(version, d, request.ModelKey)), "Нет файла " + d + " для " + version.VersionId);
        return dimensions;
    }
    public static string PathFor(VersionInput version, string dimension, string modelKey) =>
        new VersionResolver().PathFor(version, dimension, modelKey);
    public static SectionResult Compare(Snapshot old, Snapshot newer, CancellationToken token = default)
    {
        Data.Require(old.Dimension == newer.Dimension, "Разные типы снимков.");
        if (old.Dimension == "2d")
        {
            Data.Require(Canonical.Equal(SnapshotReader.NormalizeScope(Data.Object(old.Source.Metadata["scope"])),
                SnapshotReader.NormalizeScope(Data.Object(newer.Source.Metadata["scope"]))), "Области 2D несовместимы.");
            Data.Require(Canonical.Equal(SnapshotReader.Coverage(old.Source.Metadata), SnapshotReader.Coverage(newer.Source.Metadata)), "coverage несовместим.");
        }
        var unchanged = new List<ElementResult>(); var changed = new List<ElementResult>();
        var added = new List<ElementResult>(); var removed = new List<ElementResult>();
        foreach (var id in old.States.Keys.Union(newer.States.Keys).Order())
        {
            token.ThrowIfCancellationRequested();
            old.States.TryGetValue(id, out var before); newer.States.TryGetValue(id, out var after);
            if (before == null) added.Add(new(id, null, after, []));
            else if (after == null) removed.Add(new(id, before, null, []));
            else
            {
                var a = before["uniqueId"]?.GetValue<string>(); var b = after["uniqueId"]?.GetValue<string>();
                Data.Require(string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b) || a == b, "Конфликт идентичности ElementId / uniqueId: " + id);
                var diff = Canonical.Diff(before, after, token);
                (diff.Count == 0 ? unchanged : changed).Add(new(id, before, after, diff));
            }
        }
        var counts = new Counts(old.States.Count, newer.States.Count, unchanged.Count, changed.Count, added.Count, removed.Count);
        Data.Require(counts.Old == counts.Unchanged + counts.Changed + counts.Removed && counts.New == counts.Unchanged + counts.Changed + counts.Added, "Нарушен баланс сравнения.");
        return new("complete", old.Source, newer.Source, counts, unchanged, changed, added, removed);
    }
    public static async Task<ComparisonResult> RunAsync(ComparisonRequest request, CancellationToken token = default, VersionResolver? versions = null)
    {
        versions ??= new VersionResolver();
        var dimensions = ValidateRequest(request, versions);
        var sections = new Dictionary<string, SectionResult> { ["3d"] = new("notRequested"), ["2d"] = new("notRequested") };
        var limitations = new List<string>
        {
            "Принадлежность одной линии версий предполагается по modelKey и схеме имён; содержимое JSON не доказывает идентичность модели.",
            "Равенство означает равенство сохранённых сравниваемых данных; появление и удаление относятся к заявленной области снимков.",
            "Время экспорта и documentSession не являются ревизией; порядок old/new задан аргументами сравнения. Причины изменений не анализировались."
        };
        if (dimensions.Contains("3d")) limitations.Add("MCP-1 не сохраняет идентификатор модели, uniqueId и активный вид; сопоставимость отбора 3D проверить по JSON невозможно.");
        if (dimensions.Contains("2d")) limitations.Add("MCP-2 отбирает аннотации по OwnerViewId; видимость на печати не вычисляется. Неподдерживаемые свойства не описывают фактическое состояние Revit.");
        foreach (var d in dimensions)
        {
            var old = await SnapshotReader.ReadAsync(versions.PathFor(request.OldVersion, d, request.ModelKey), d, token);
            var newer = await SnapshotReader.ReadAsync(versions.PathFor(request.NewVersion, d, request.ModelKey), d, token);
            sections[d] = Compare(old, newer, token);
            if (!Canonical.Equal(old.Source.Metadata["documentSession"], newer.Source.Metadata["documentSession"]))
                limitations.Add(d + ": documentSession старого и нового снимков различаются; это не определяет совместимость версий.");
            if (!Canonical.Equal(old.Source.Metadata["document"], newer.Source.Metadata["document"]))
                limitations.Add(d + ": сведения о документах различаются; принадлежность одной линии версий предполагается по линии версий.");
        }
        if (dimensions.Length == 2)
            foreach (var side in new[] { "old", "new" })
            {
                var a = side == "old" ? sections["3d"].OldSource! : sections["3d"].NewSource!;
                var b = side == "old" ? sections["2d"].OldSource! : sections["2d"].NewSource!;
                if (!Canonical.Equal(a.Metadata["documentSession"], b.Metadata["documentSession"]))
                    limitations.Add(side + ": сессии 3D и 2D различаются; соответствие пары предполагается по линии версий.");
            }
        return new(1, Data.Policy, Guid.NewGuid().ToString("N"), DateTime.UtcNow, request, limitations, sections);
    }
}
