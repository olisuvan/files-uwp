namespace Files.Core;

public sealed class LocalFileSystem : IDirectoryReader
{
    public Task<DirectoryListing> ReadAsync(string path, bool showHidden, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            var normalized = PathRules.Normalize(path);
            var entries = new List<FileEntry>();
            var skipped = 0;
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = false,
                IgnoreInaccessible = false,
                AttributesToSkip = showHidden ? 0 : FileAttributes.Hidden | FileAttributes.System
            };
            foreach (var item in new DirectoryInfo(normalized).EnumerateFileSystemInfos("*", options))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var attributes = item.Attributes;
                    var isDirectory = (attributes & FileAttributes.Directory) != 0;
                    entries.Add(new(item.FullName, item.Name, isDirectory,
                        isDirectory ? null : ((FileInfo)item).Length, item.LastWriteTime, attributes));
                }
                catch (IOException) { skipped++; }
                catch (UnauthorizedAccessException) { skipped++; }
            }
            cancellationToken.ThrowIfCancellationRequested();
            return new DirectoryListing(normalized, entries.OrderByDescending(x => x.IsDirectory)
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray(), skipped);
        }, cancellationToken);

    public Task CreateFolderAsync(string parent, string name, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            PathRules.ValidateName(name);
            PathRules.RejectReparsePoints(parent);
            cancellationToken.ThrowIfCancellationRequested();
            var destination = Path.Combine(parent, name);
            if (Path.Exists(destination)) throw new IOException("同名项目已存在。");
            // Directory.CreateDirectory alone silently succeeds on an existing directory.
            // A concurrent creator may still race this check; no existing content is removed.
            Directory.CreateDirectory(destination);
        }, cancellationToken);

    public Task RenameAsync(string source, string name, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            PathRules.ValidateName(name);
            source = PathRules.Normalize(source);
            var parent = Directory.GetParent(source)?.FullName ?? throw new IOException("不能重命名磁盘根目录。");
            PathRules.RejectReparsePoints(source);
            var destination = Path.Combine(parent, name);
            if (string.Equals(source, destination, StringComparison.Ordinal)) return;
            cancellationToken.ThrowIfCancellationRequested();
            // File.Move/Directory.Move fail on collisions instead of overwriting.
            if (Directory.Exists(source)) Directory.Move(source, destination);
            else File.Move(source, destination);
        }, cancellationToken);

    public Task<string> CopyAsync(string source, string destinationDirectory, CancellationToken cancellationToken = default) =>
        Task.Run(async () =>
        {
            source = PathRules.Normalize(source);
            destinationDirectory = PathRules.Normalize(destinationDirectory);
            PathRules.RejectReparsePoints(source);
            PathRules.RejectReparsePoints(destinationDirectory);
            if (!Directory.Exists(destinationDirectory)) throw new DirectoryNotFoundException(destinationDirectory);
            var isDirectory = Directory.Exists(source);
            if (isDirectory && PathRules.IsSameOrDescendant(destinationDirectory, source))
                throw new IOException("不能把文件夹复制到自身或其子文件夹。");
            var name = Path.GetFileName(source);
            if (string.IsNullOrEmpty(name)) throw new IOException("不能复制整个磁盘根目录。");
            var destination = Path.Combine(destinationDirectory, name);
            if (PathRules.Same(source, destination)) throw new IOException("源位置与目标位置相同。");
            if (Path.Exists(destination)) throw new IOException("目标已有同名项目；现有内容未被覆盖。");
            cancellationToken.ThrowIfCancellationRequested();

            // Stage beside the destination and commit with a non-overwriting move.
            // Failure/cancellation only removes our uniquely named staging entry.
            var staging = Path.Combine(destinationDirectory, ".files-copy-" + Guid.NewGuid().ToString("N"));
            try
            {
                if (isDirectory)
                {
                    Directory.CreateDirectory(staging);
                    await CopyDirectoryAsync(source, staging, cancellationToken).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    Directory.Move(staging, destination);
                }
                else
                {
                    await CopyFileAsync(source, staging, cancellationToken).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    File.Move(staging, destination);
                }
                return destination;
            }
            finally
            {
                try
                {
                    if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
                    else if (File.Exists(staging)) File.Delete(staging);
                }
                catch (IOException) { /* A leftover staging entry is preferable to hiding the original failure. */ }
                catch (UnauthorizedAccessException) { }
            }
        }, cancellationToken);

    private static async Task CopyDirectoryAsync(string source, string destination, CancellationToken cancellationToken)
    {
        foreach (var entry in new DirectoryInfo(source).EnumerateFileSystemInfos())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("复制已停止：文件夹包含符号链接或目录联接。");
            var target = Path.Combine(destination, entry.Name);
            if ((entry.Attributes & FileAttributes.Directory) != 0)
            {
                Directory.CreateDirectory(target);
                await CopyDirectoryAsync(entry.FullName, target, cancellationToken).ConfigureAwait(false);
            }
            else await CopyFileAsync(entry.FullName, target, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task CopyFileAsync(string source, string destination, CancellationToken cancellationToken)
    {
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
