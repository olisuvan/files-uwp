namespace Files.Core;

public enum NavigationKind { New, Back, Forward, Refresh }

// Owned by one UI thread. The reader may run in the background.
// Only the most recent successful request may publish a listing or change history.
public sealed class NavigationSession(IDirectoryReader reader) : IDisposable
{
    private readonly Stack<string> back = new();
    private readonly Stack<string> forward = new();
    private CancellationTokenSource? pending;
    private int version;
    public DirectoryListing? Current { get; private set; }
    public bool CanGoBack => back.Count > 0;
    public bool CanGoForward => forward.Count > 0;
    public string? Parent => Current is null ? null : Directory.GetParent(Current.Path)?.FullName;

    public async Task<bool> NavigateAsync(string path, bool showHidden, NavigationKind kind = NavigationKind.New)
    {
        if (kind == NavigationKind.Back && !CanGoBack || kind == NavigationKind.Forward && !CanGoForward)
            return false;
        var target = kind switch
        {
            NavigationKind.Back => back.Peek(),
            NavigationKind.Forward => forward.Peek(),
            _ => PathRules.Normalize(path)
        };
        var request = ++version;
        pending?.Cancel();
        var cancellation = new CancellationTokenSource();
        pending = cancellation;
        try
        {
            var listing = await reader.ReadAsync(target, showHidden, cancellation.Token);
            if (request != version || cancellation.IsCancellationRequested) return false;
            if (Current is not null && !PathRules.Same(Current.Path, listing.Path))
            {
                if (kind == NavigationKind.Back) { back.Pop(); forward.Push(Current.Path); }
                else if (kind == NavigationKind.Forward) { forward.Pop(); back.Push(Current.Path); }
                else { back.Push(Current.Path); forward.Clear(); }
            }
            Current = listing;
            return true;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return false; }
        catch (Exception) when (request != version) { return false; }
        finally
        {
            if (ReferenceEquals(pending, cancellation)) pending = null;
            cancellation.Dispose();
        }
    }

    public void Dispose()
    {
        version++;
        pending?.Cancel();
    }
}
