namespace Files.Core;

public static class PathRules
{
    private static readonly char[] InvalidWindowsCharacters = "<>:\"/\\|?*".ToCharArray();
    public static StringComparison Comparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static string Normalize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    public static bool Same(string left, string right) =>
        string.Equals(Normalize(left), Normalize(right), Comparison);

    public static bool IsSameOrDescendant(string candidate, string parent)
    {
        candidate = Normalize(candidate);
        parent = Normalize(parent);
        return string.Equals(candidate, parent, Comparison) ||
            candidate.StartsWith(Path.EndsInDirectorySeparator(parent) ? parent : parent + Path.DirectorySeparatorChar, Comparison);
    }

    public static void ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name is "." or ".." || name.Length > 255 || name.IndexOfAny(InvalidWindowsCharacters) >= 0 ||
            name.Any(char.IsControl) || name.EndsWith(' ') || name.EndsWith('.'))
            throw new ArgumentException("名称包含 Windows 不支持的字符，或以空格、句点结尾。", nameof(name));
        var stem = name.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" ||
            (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) &&
             "123456789¹²³".Contains(stem[3])))
            throw new ArgumentException("不能使用 Windows 设备保留名称。", nameof(name));
    }

    // Reject links/junctions in every existing ancestor before a recursive write.
    // This protects ordinary path aliases; it is not a sandbox against hostile concurrent changes.
    public static void RejectReparsePoints(string path)
    {
        var current = Normalize(path);
        while (true)
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("此操作不支持符号链接或目录联接，请使用原始路径。");
            var parent = Directory.GetParent(current)?.FullName;
            if (parent is null) break;
            current = parent;
        }
    }
}
