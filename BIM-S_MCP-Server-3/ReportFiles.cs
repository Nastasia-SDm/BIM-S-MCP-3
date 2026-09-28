using System.Text;

namespace BimS.Mcp3;

public class ReportFiles(string root = ReportFiles.DefaultRoot)
{
    public const string DefaultRoot = @"D:\BIM-S-MCP-3_Отчеты_Версии модели";
    public string Root { get; } = Path.GetFullPath(root);
    public string ValidateResultPath(string path)
    {
        var full = Path.GetFullPath(path);
        Data.Require(Path.IsPathFullyQualified(path) && string.Equals(Path.GetDirectoryName(full), Root, StringComparison.OrdinalIgnoreCase)
            && Path.GetExtension(full).Equals(".json", StringComparison.OrdinalIgnoreCase), "JSON результата должен находиться в каталоге отчётов MCP-3.");
        return full;
    }
    public virtual async Task<string> SaveAsync(string name, string contents, CancellationToken token)
    {
        Data.Require(Path.GetFileName(name) == name, "Неверное имя результата.");
        token.ThrowIfCancellationRequested(); Directory.CreateDirectory(Root);
        var target = Path.Combine(Root, name); var temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temp, contents, new UTF8Encoding(false), token);
            token.ThrowIfCancellationRequested(); File.Move(temp, target, false); return target;
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
