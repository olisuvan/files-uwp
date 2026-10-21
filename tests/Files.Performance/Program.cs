using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Files.Core;

// An opt-in measurement tool, not a timing assertion or a disk benchmark.
var output = Path.GetFullPath(args.FirstOrDefault() ?? "core-performance.json");
var root = Path.Combine(Path.GetTempPath(), "files-perf-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var fileSystem = new LocalFileSystem();
var measurements = new List<object>();
try
{
    // JIT/thread-pool warmup is outside measured samples. OS caches are not purged.
    await fileSystem.ReadAsync(root, false, default);
    foreach (var count in new[] { 500, 5_000, 20_000 })
    {
        var directory = Directory.CreateDirectory(Path.Combine(root, $"entries-{count}")).FullName;
        for (var i = 0; i < count; i++)
            File.WriteAllBytes(Path.Combine(directory, $"document-{i:D6}.txt"), [42]);
        var first = Stopwatch.StartNew();
        var listing = await fileSystem.ReadAsync(directory, false, default);
        first.Stop();
        Require(listing.Entries.Count == count && listing.SkippedCount == 0, "Listing lost entries.");
        var samples = new List<double>();
        for (var i = 0; i < 5; i++)
        {
            var watch = Stopwatch.StartNew();
            listing = await fileSystem.ReadAsync(directory, false, default);
            watch.Stop();
            Require(listing.Entries.Count == count, "Repeated listing lost entries.");
            samples.Add(watch.Elapsed.TotalMilliseconds);
        }
        measurements.Add(Summarize($"read_metadata_and_sort_{count}", count, samples,
            firstAfterFixtureMs: first.Elapsed.TotalMilliseconds));

        // Same expression as MainWindow.ApplyFilter. Excludes UI binding/layout/painting.
        // Batch 100 scans per sample to reduce stopwatch quantization.
        foreach (var query in new[] { "document", "not-found" })
        {
            const int repeats = 100;
            var filterSamples = new List<double>();
            for (var sample = 0; sample < 5; sample++)
            {
                var watch = Stopwatch.StartNew();
                for (var repeat = 0; repeat < repeats; repeat++)
                {
                    var matches = listing.Entries.Where(x => x.Name.Contains(query,
                        StringComparison.OrdinalIgnoreCase)).ToArray();
                    Require(matches.Length == (query == "document" ? count : 0), "Wrong filter result.");
                }
                watch.Stop();
                filterSamples.Add(watch.Elapsed.TotalMilliseconds / repeats);
            }
            measurements.Add(Summarize($"filter_{count}_{query}", count, filterSamples));
        }
    }

    const int largeMiB = 128;
    var largeFile = Path.Combine(root, "payload.bin");
    var buffer = new byte[1024 * 1024];
    Random.Shared.NextBytes(buffer);
    using (var stream = File.Create(largeFile))
        for (var i = 0; i < largeMiB; i++) stream.Write(buffer);
    var expectedHash = Hash(largeFile);
    var copySamples = new List<double>();
    for (var i = 0; i < 3; i++)
    {
        var destination = Directory.CreateDirectory(Path.Combine(root, $"large-copy-{i}")).FullName;
        var watch = Stopwatch.StartNew();
        var copied = await fileSystem.CopyAsync(largeFile, destination);
        watch.Stop();
        Require(new FileInfo(copied).Length == largeMiB * 1024L * 1024 &&
            Hash(copied) == expectedHash, "Large copy differs from source.");
        copySamples.Add(watch.Elapsed.TotalMilliseconds);
        Directory.Delete(destination, true);
    }
    measurements.Add(Summarize("copy_128_MiB_file", 1, copySamples, largeMiB));

    const int smallFiles = 1_000;
    var smallDirectory = Directory.CreateDirectory(Path.Combine(root, "small-files")).FullName;
    for (var i = 0; i < smallFiles; i++)
        File.WriteAllBytes(Path.Combine(smallDirectory, $"item-{i:D4}.bin"), buffer[..1024]);
    copySamples.Clear();
    for (var i = 0; i < 3; i++)
    {
        var destination = Directory.CreateDirectory(Path.Combine(root, $"small-copy-{i}")).FullName;
        var watch = Stopwatch.StartNew();
        var copied = await fileSystem.CopyAsync(smallDirectory, destination);
        watch.Stop();
        var files = Directory.GetFiles(copied);
        Require(files.Length == smallFiles && files.All(x =>
            File.ReadAllBytes(x).AsSpan().SequenceEqual(buffer.AsSpan(0, 1024))), "Small copy differs.");
        copySamples.Add(watch.Elapsed.TotalMilliseconds);
        Directory.Delete(destination, true);
    }
    measurements.Add(Summarize("copy_1000_files_1_KiB_each", smallFiles, copySamples));

    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
    var report = new
    {
        Schema = 1,
        SourceCommit = Environment.GetEnvironmentVariable("GITHUB_SHA") ?? "local-working-tree",
        MeasuredAtUtc = DateTime.UtcNow,
        Environment = new
        {
            OS = RuntimeInformation.OSDescription,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            Runtime = RuntimeInformation.FrameworkDescription,
            LogicalProcessors = System.Environment.ProcessorCount,
            GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,
            RunnerImage = System.Environment.GetEnvironmentVariable("ImageVersion")
        },
        Method = "Release; sequential; freshly-created local fixtures; no cache purge; 5 read/filter samples, 3 copy samples; copy includes staging commit and buffered FlushAsync, not durable media flush; validation/hashing excluded; UI excluded.",
        Measurements = measurements
    };
    await File.WriteAllTextAsync(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine(await File.ReadAllTextAsync(output));
}
finally
{
    Directory.Delete(root, true);
}

static object Summarize(string name, int items, List<double> samples, double? miB = null,
    double? firstAfterFixtureMs = null)
{
    var sorted = samples.Order().ToArray();
    var median = sorted[sorted.Length / 2];
    return new
    {
        Name = name, Items = items, SamplesMs = samples.ToArray(), MedianMs = median,
        MinimumMs = sorted[0], MaximumMs = sorted[^1], FirstAfterFixtureMs = firstAfterFixtureMs,
        MiB = miB, MedianMiBPerSecond = miB / (median / 1000)
    };
}

static string Hash(string path)
{
    using var input = File.OpenRead(path);
    return Convert.ToHexString(SHA256.HashData(input));
}

static void Require(bool valid, string message)
{
    if (!valid) throw new InvalidOperationException(message);
}
