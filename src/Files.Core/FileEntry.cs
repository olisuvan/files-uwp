using System.Globalization;

namespace Files.Core;

public sealed record FileEntry(
    string FullPath, string Name, bool IsDirectory, long? Length,
    DateTime LastWriteTime, FileAttributes Attributes)
{
    public string Kind => IsDirectory ? "文件夹" :
        string.IsNullOrEmpty(Path.GetExtension(Name)) ? "文件" : Path.GetExtension(Name).ToUpperInvariant() + " 文件";
    public string Glyph => IsDirectory ? "\uE8B7" : "\uE8A5";
    public string ModifiedText => LastWriteTime.ToString("g", CultureInfo.CurrentCulture);
    public string SizeText => Length is not long size ? "—" :
        size >= 1L << 30 ? $"{size / (double)(1L << 30):0.##} GB" :
        size >= 1L << 20 ? $"{size / (double)(1L << 20):0.##} MB" :
        size >= 1L << 10 ? $"{size / 1024d:0.##} KB" : $"{size} B";
}

public sealed record DirectoryListing(string Path, IReadOnlyList<FileEntry> Entries, int SkippedCount = 0);

public interface IDirectoryReader
{
    Task<DirectoryListing> ReadAsync(string path, bool showHidden, CancellationToken cancellationToken);
}
