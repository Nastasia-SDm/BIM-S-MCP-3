using System.Globalization;
using System.Text.RegularExpressions;

namespace BimS.Mcp3;

// Catalogs and naming belong to MCP3. Alternate roots are used by isolated tests.
public sealed class VersionResolver(
    string modelRoot = @"D:\BIM-S-MCP-1_Отчеты_Версии модели",
    string documentationRoot = @"D:\BIM-S-MCP-2_Отчеты_Версии модели")
{
    public static bool IsAlias(string? value) => value is "previous" or "latest";

    public static void Validate(string? value) => Data.Require(
        value != null && (IsAlias(value) || Regex.IsMatch(value, @"\AV[0-9]{3,}\z")),
        "Версия должна быть V003 либо previous/latest.");

    public string PathFor(VersionInput version, string dimension, string modelKey)
    {
        Data.Require(dimension is "3d" or "2d", "Раздел: 3d или 2d.");
        Validate(version.VersionId);
        Data.Require(!IsAlias(version.VersionId), "Сначала разрешите aliases.");
        Data.Require(!string.IsNullOrWhiteSpace(modelKey) &&
            modelKey.IndexOfAny(Path.GetInvalidFileNameChars()) < 0,
            "modelKey должен быть именем одной линии версий.");
        return Path.Combine(Path.GetFullPath(dimension == "3d" ? modelRoot : documentationRoot),
            $"{modelKey}_{version.VersionId}_{(dimension == "3d" ? "model" : "documentation")}.json");
    }

    public (string Old, string New) Resolve(
        string version1, string version2, string dimension, string modelKey)
    {
        Validate(version1);
        Validate(version2);
        // Also validate the dimension/key for explicit calls.
        var example = PathFor(new VersionInput("V000", "V000"), dimension, modelKey);
        if (!IsAlias(version1) && !IsAlias(version2))
        {
            Data.Require(version1 != version2, "Нужны разные версии.");
            return (version1, version2);
        }

        var directory = Path.GetDirectoryName(example)!;
        var suffix = dimension == "3d" ? "model" : "documentation";
        var pattern = @"\A" + Regex.Escape(modelKey) +
            @"_(?<version>V[0-9]{3,})_" + suffix + @"\.json\z";
        Data.Require(Directory.Exists(directory), $"Нет каталога снимков {dimension}.");

        var versions = Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
            .Select(path => Regex.Match(Path.GetFileName(path), pattern))
            .Where(match => match.Success)
            .Select(match => match.Groups["version"].Value)
            .Select(id =>
            {
                Data.Require(long.TryParse(id.AsSpan(1), NumberStyles.None,
                    CultureInfo.InvariantCulture, out var number), "Слишком большой номер версии: " + id);
                return (Id: id, Number: number);
            })
            .OrderBy(item => item.Number)
            .ToArray();

        Data.Require(versions.Length > 0, $"Нет версий {dimension}.");
        Data.Require(versions.Select(item => item.Number).Distinct().Count() == versions.Length,
            $"Неоднозначная нумерация версий {dimension}.");

        string ResolveOne(string value)
        {
            if (value == "latest") return versions[^1].Id;
            if (value != "previous") return value;
            Data.Require(versions.Length >= 2, $"Для {dimension} нет предыдущей версии.");
            return versions[^2].Id;
        }

        var oldVersion = ResolveOne(version1);
        var newVersion = ResolveOne(version2);
        Data.Require(oldVersion != newVersion, "Нужны разные версии.");
        return (oldVersion, newVersion);
    }
}
