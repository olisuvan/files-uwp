using Files.Core;

var failures = new List<string>();
var total = 0;
var root = Path.Combine(Path.GetTempPath(), "files-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var fs = new LocalFileSystem();
try
{
    foreach (var invalid in new[] { "", " ", ".", "..", "a/b", @"a\b", "a:b", "name.", "name ", "CON", "nul.txt", "COM1", "LPT9.log", "COM¹.txt" })
        await Test($"Reject invalid Windows name: '{invalid}'", async () =>
            await Throws<ArgumentException>(() => fs.CreateFolderAsync(root, invalid)));
    await Test("Accept Unicode folder name", async () =>
    {
        await fs.CreateFolderAsync(root, "中文 文件夹");
        Assert(Directory.Exists(Path.Combine(root, "中文 文件夹")));
    });
    await Test("Existing folder is a collision", async () =>
        await Throws<IOException>(() => fs.CreateFolderAsync(root, "中文 文件夹")));
    await Test("Path boundaries distinguish sibling prefixes", () =>
    {
        Assert(!PathRules.IsSameOrDescendant(Path.Combine(root, "folder2"), Path.Combine(root, "folder")));
        Assert(PathRules.IsSameOrDescendant(Path.Combine(root, "folder", "child"), Path.Combine(root, "folder")));
        Assert(PathRules.IsSameOrDescendant(root, root));
        Assert(PathRules.IsSameOrDescendant(root, Path.GetPathRoot(root)!));
        return Task.CompletedTask;
    });
    var source = Directory.CreateDirectory(Path.Combine(root, "source")).FullName;
    var target = Directory.CreateDirectory(Path.Combine(root, "target")).FullName;
    var file = Path.Combine(source, "a.txt");
    await File.WriteAllTextAsync(file, "original data");
    Directory.CreateDirectory(Path.Combine(source, "nested", "empty"));
    await File.WriteAllTextAsync(Path.Combine(source, "nested", "b.txt"), "nested data");
    await Test("Folder copy awaits every descendant, including empty folders", async () =>
    {
        var result = await fs.CopyAsync(source, target);
        Assert(await File.ReadAllTextAsync(Path.Combine(result, "nested", "b.txt")) == "nested data");
        Assert(Directory.Exists(Path.Combine(result, "nested", "empty")));
    });
    await Test("Second copy has independent state", async () =>
    {
        var second = Directory.CreateDirectory(Path.Combine(root, "target2")).FullName;
        var result = await fs.CopyAsync(source, second);
        Assert(File.Exists(Path.Combine(result, "a.txt")));
    });
    await Test("Collision preserves existing destination and source", async () =>
    {
        var existing = Path.Combine(target, "source", "a.txt");
        await File.WriteAllTextAsync(existing, "keep this");
        await Throws<IOException>(() => fs.CopyAsync(source, target));
        Assert(await File.ReadAllTextAsync(existing) == "keep this");
        Assert(await File.ReadAllTextAsync(file) == "original data");
    });
    await Test("Copy into own descendant is rejected without output", async () =>
    {
        await Throws<IOException>(() => fs.CopyAsync(source, Path.Combine(source, "nested")));
        Assert(!Directory.Exists(Path.Combine(source, "nested", "source")));
    });
    await Test("Copy file onto itself is rejected", async () =>
        await Throws<IOException>(() => fs.CopyAsync(file, source)));
    await Test("File copy preserves bytes", async () =>
    {
        var result = await fs.CopyAsync(file, target);
        Assert(await File.ReadAllTextAsync(result) == "original data");
    });
    await Test("Missing source does not leave staging entries", async () =>
    {
        await Throws<FileNotFoundException>(() => fs.CopyAsync(Path.Combine(root, "missing.txt"), target));
        Assert(!Directory.EnumerateFileSystemEntries(target, ".files-copy-*").Any());
    });
    await Test("Cancelled copy does not create a destination", async () =>
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelledTarget = Directory.CreateDirectory(Path.Combine(root, "cancelled")).FullName;
        await Throws<OperationCanceledException>(() => fs.CopyAsync(source, cancelledTarget, cancellation.Token));
        Assert(!Directory.EnumerateFileSystemEntries(cancelledTarget).Any());
    });
    await Test("Rename collision does not overwrite either item", async () =>
    {
        await File.WriteAllTextAsync(Path.Combine(source, "existing.txt"), "existing");
        await Throws<IOException>(() => fs.RenameAsync(file, "existing.txt"));
        Assert(await File.ReadAllTextAsync(file) == "original data");
        Assert(await File.ReadAllTextAsync(Path.Combine(source, "existing.txt")) == "existing");
    });
    await Test("Rename completes before returning", async () =>
    {
        await fs.RenameAsync(file, "renamed.txt");
        Assert(!File.Exists(file) && File.Exists(Path.Combine(source, "renamed.txt")));
    });
    await Test("Enumeration sorts folders first and reports file size", async () =>
    {
        var listing = await fs.ReadAsync(source, true, default);
        Assert(listing.Entries[0].IsDirectory);
        Assert(listing.Entries.Single(x => x.Name == "renamed.txt").Length == 13);
    });
    await Test("Missing folder enumeration fails explicitly", async () =>
        await Throws<DirectoryNotFoundException>(() => fs.ReadAsync(Path.Combine(root, "absent"), true, default)));
    if (!OperatingSystem.IsWindows())
    {
        await Test("Recursive copy rejects links and cleans its staging tree", async () =>
        {
            var linkSource = Directory.CreateDirectory(Path.Combine(root, "link-source")).FullName;
            await File.WriteAllTextAsync(Path.Combine(linkSource, "first.txt"), "stage this");
            Directory.CreateSymbolicLink(Path.Combine(linkSource, "link"), source);
            await Throws<IOException>(() => fs.CopyAsync(linkSource, target));
            Assert(!Directory.Exists(Path.Combine(target, "link-source")));
            Assert(!Directory.EnumerateFileSystemEntries(target, ".files-copy-*").Any());
            Directory.Delete(Path.Combine(linkSource, "link"));
        });
        await Test("Ancestor aliases cannot bypass descendant protection", async () =>
        {
            var alias = Path.Combine(root, "alias");
            Directory.CreateSymbolicLink(alias, source);
            await Throws<IOException>(() => fs.CopyAsync(source, Path.Combine(alias, "nested")));
            Directory.Delete(alias);
        });
    }
    await Test("Failed navigation preserves location and history", async () =>
    {
        using var session = new NavigationSession(fs);
        await session.NavigateAsync(root, false);
        await session.NavigateAsync(source, false);
        await Throws<DirectoryNotFoundException>(() => session.NavigateAsync(Path.Combine(root, "absent"), false));
        Assert(session.Current!.Path == source && session.CanGoBack && !session.CanGoForward);
        await session.NavigateAsync("", false, NavigationKind.Back);
        Assert(session.Current.Path == root && session.CanGoForward);
        await session.NavigateAsync("", false, NavigationKind.Forward);
        Assert(session.Current.Path == source);
    });
    await Test("Refresh and duplicate navigation do not add history entries", async () =>
    {
        using var session = new NavigationSession(fs);
        await session.NavigateAsync(root, false);
        await session.NavigateAsync(root, true, NavigationKind.Refresh);
        await session.NavigateAsync(root, false);
        Assert(!session.CanGoBack && !session.CanGoForward);
    });
    await Test("New navigation clears forward history only after success", async () =>
    {
        using var session = new NavigationSession(fs);
        await session.NavigateAsync(root, false);
        await session.NavigateAsync(source, false);
        await session.NavigateAsync("", false, NavigationKind.Back);
        await Throws<DirectoryNotFoundException>(() => session.NavigateAsync(Path.Combine(root, "absent"), false));
        Assert(session.CanGoForward);
        await session.NavigateAsync(target, false);
        Assert(!session.CanGoForward);
    });
    await Test("Slow stale request cannot replace a newer location", async () =>
    {
        var reader = new ControlledReader();
        using var session = new NavigationSession(reader);
        var slow = session.NavigateAsync(source, false);
        var fast = session.NavigateAsync(target, false);
        reader.Complete(target);
        Assert(await fast);
        reader.Complete(source);
        Assert(!await slow && session.Current!.Path == target);
        Assert(!session.CanGoBack);
    });
    await Test("Closing prevents an outstanding read from publishing", async () =>
    {
        var reader = new ControlledReader();
        using var session = new NavigationSession(reader);
        var pending = session.NavigateAsync(source, false);
        session.Dispose();
        reader.Complete(source);
        Assert(!await pending && session.Current is null);
    });
}
finally { Directory.Delete(root, recursive: true); }
Console.WriteLine($"{total - failures.Count}/{total} tests passed.");
foreach (var failure in failures) Console.Error.WriteLine(failure);
return failures.Count == 0 ? 0 : 1;

async Task Test(string name, Func<Task> action)
{
    total++;
    try { await action(); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failures.Add("FAIL " + name + ": " + ex); }
}
static void Assert(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
static async Task Throws<T>(Func<Task> action) where T : Exception
{
    try { await action(); }
    catch (T) { return; }
    throw new Exception("Expected " + typeof(T).Name);
}

sealed class ControlledReader : IDirectoryReader
{
    private readonly Dictionary<string, TaskCompletionSource<DirectoryListing>> requests = new();
    public Task<DirectoryListing> ReadAsync(string path, bool showHidden, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<DirectoryListing>();
        requests.Add(path, completion);
        return completion.Task;
    }
    public void Complete(string path) => requests[path].SetResult(new(path, Array.Empty<FileEntry>()));
}
