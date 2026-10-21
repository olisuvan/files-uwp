using System.ComponentModel;
using System.Runtime.CompilerServices;
using Files.Core;

namespace Files.WinUI;

public sealed class BrowserViewModel : INotifyPropertyChanged
{
    private string currentPath = "";
    private string status = "选择文件夹开始浏览";
    private string error = "";
    private bool isLoading, isMutating, canGoBack, canGoForward, canGoUp;
    private IReadOnlyList<FileEntry> entries = Array.Empty<FileEntry>();
    public IReadOnlyList<FileEntry> Entries { get => entries; set => Set(ref entries, value); }
    public string CurrentPath { get => currentPath; set => Set(ref currentPath, value); }
    public string Status { get => status; set => Set(ref status, value); }
    public string Error { get => error; set { Set(ref error, value); Changed(nameof(HasError)); } }
    public bool HasError => !string.IsNullOrEmpty(Error);
    public bool IsLoading { get => isLoading; set => Set(ref isLoading, value); }
    public bool IsMutating
    {
        get => isMutating;
        set { Set(ref isMutating, value); Changed(nameof(CanInteract)); }
    }
    public bool CanInteract => !IsMutating;
    public bool CanGoBack { get => canGoBack; set => Set(ref canGoBack, value); }
    public bool CanGoForward { get => canGoForward; set => Set(ref canGoForward, value); }
    public bool CanGoUp { get => canGoUp; set => Set(ref canGoUp, value); }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        Changed(name);
    }
    private void Changed(string? name) => PropertyChanged?.Invoke(this, new(name));
}
