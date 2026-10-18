namespace Spindle.Core;

/// <summary>Çıktılar her zaman orijinal dosyanın yanına yazılır ve hiçbir dosyanın üzerine yazılmaz.</summary>
public static class OutputPaths
{
    public static string BaseName(string path)
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        if (name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)) return name[..^7];
        return Directory.Exists(path) ? name : Path.GetFileNameWithoutExtension(name);
    }

    public static string DirectoryOf(string path) =>
        Path.GetDirectoryName(Path.GetFullPath(Path.TrimEndingDirectorySeparator(path)))!;

    public static string ForTarget(string input, string ext) =>
        Unique(Path.Combine(DirectoryOf(input), BaseName(input) + "." + ext));

    public static string WithSuffix(string input, string suffix, string? ext = null)
    {
        ext ??= Path.GetExtension(input).TrimStart('.');
        return Unique(Path.Combine(DirectoryOf(input), $"{BaseName(input)} ({suffix}).{ext}"));
    }

    public static string FolderFor(string input, string? suffix = null)
    {
        var name = suffix is null ? BaseName(input) : $"{BaseName(input)} ({suffix})";
        return Unique(Path.Combine(DirectoryOf(input), name));
    }

    public static string Unique(string path)
    {
        if (!Exists(path)) return path;
        var dir = Path.GetDirectoryName(path)!;
        var ext = Path.GetExtension(path);
        var name = Path.GetFileNameWithoutExtension(path);
        if (Directory.Exists(path)) { name = Path.GetFileName(path); ext = ""; }
        for (var i = 2; ; i++)
        {
            var candidate = Path.Combine(dir, $"{name} {i}{ext}");
            if (!Exists(candidate)) return candidate;
        }
    }

    static bool Exists(string p) => File.Exists(p) || Directory.Exists(p);
}
