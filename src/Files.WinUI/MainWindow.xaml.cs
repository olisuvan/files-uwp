using System.Diagnostics;
using System.Runtime.InteropServices;
using Files.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;
using WinRT.Interop;

namespace Files.WinUI;

public sealed partial class MainWindow : Window
{
    public BrowserViewModel ViewModel { get; } = new();
    private readonly LocalFileSystem fileSystem = new();
    private readonly NavigationSession navigation;
    private readonly CancellationTokenSource lifetime = new();
    private int loadingVersion;
    private bool loaded, closed, dialogOpen;
    private bool showHidden;

    public MainWindow()
    {
        navigation = new(fileSystem);
        InitializeComponent();
        Root.DataContext = ViewModel;
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1120, 760));
        SystemBackdrop = new MicaBackdrop();
        Closed += (_, _) =>
        {
            closed = true;
            navigation.Dispose();
            lifetime.Cancel();
            // An in-flight file operation owns its token until it finishes.
        };
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (loaded) return;
        loaded = true;
        AddLocation("个人文件夹", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Symbol.Home);
        AddLocation("桌面", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Symbol.ViewAll);
        AddLocation("文档", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), Symbol.Document);
        AddLocation("图片", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), Symbol.Pictures);
        Sidebar.MenuItems.Add(new NavigationViewItemSeparator());
        Sidebar.MenuItems.Add(new NavigationViewItemHeader { Content = "此电脑" });
        try
        {
            // Drive discovery may block for disconnected network locations.
            var drives = await Task.Run(() => DriveInfo.GetDrives().Select(x => x.Name).ToArray());
            if (closed) return;
            foreach (var drive in drives) AddLocation(drive, drive, Symbol.Folder);
        }
        catch (Exception ex) { ShowError(ex); }
        await NavigateAsync(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    }

    private void AddLocation(string name, string path, Symbol symbol)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        Sidebar.MenuItems.Add(new NavigationViewItem { Content = name, Tag = path, Icon = new SymbolIcon(symbol) });
    }

    private async Task NavigateAsync(string path, NavigationKind kind = NavigationKind.New)
    {
        if (closed || ViewModel.IsMutating || dialogOpen) return;
        var request = ++loadingVersion;
        ViewModel.IsLoading = true;
        ViewModel.Error = "";
        try
        {
            if (await navigation.NavigateAsync(path, showHidden, kind) && !closed)
            {
                ViewModel.CurrentPath = navigation.Current!.Path;
                Address.Text = ViewModel.CurrentPath;
                ApplyFilter();
            }
        }
        catch (Exception ex) { if (!closed) { Address.Text = ViewModel.CurrentPath; ShowError(ex); } }
        finally
        {
            if (!closed && request == loadingVersion)
            {
                ViewModel.IsLoading = false;
                ViewModel.CanGoBack = navigation.CanGoBack;
                ViewModel.CanGoForward = navigation.CanGoForward;
                ViewModel.CanGoUp = navigation.Parent is not null;
                UpdateSelection();
            }
        }
    }

    private void ApplyFilter()
    {
        if (navigation.Current is not { } current) return;
        var query = SearchBox.Text.Trim();
        ViewModel.Entries = current.Entries.Where(x => x.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        var skipped = current.SkippedCount > 0 ? $" · {current.SkippedCount} 项暂时无法读取" : "";
        ViewModel.Status = $"{ViewModel.Entries.Count} 项 / 共 {current.Entries.Count} 项{skipped}";
        UpdateSelection();
    }

    private FileEntry[] Selected() => FileList.SelectedItems.OfType<FileEntry>().ToArray();
    private bool CanOperate => !closed && !ViewModel.IsLoading && !ViewModel.IsMutating && !dialogOpen;
    private void UpdateSelection()
    {
        var count = FileList.SelectedItems.Count;
        OpenButton.IsEnabled = RenameButton.IsEnabled = CanOperate && count == 1;
        CopyButton.IsEnabled = DeleteButton.IsEnabled = CanOperate && count > 0;
        PasteButton.IsEnabled = CanOperate && navigation.Current is not null;
    }

    private async Task OpenAsync(FileEntry? entry)
    {
        if (!CanOperate || entry is null) return;
        if (entry.IsDirectory) await NavigateAsync(entry.FullPath);
        else
        {
            try { Process.Start(new ProcessStartInfo(entry.FullPath) { UseShellExecute = true }); }
            catch (Exception ex) { ShowError(ex); }
        }
    }

    private async Task MutateAsync(Func<Task> action)
    {
        if (!CanOperate || navigation.Current is null) return;
        ViewModel.IsMutating = true;
        ViewModel.Error = "";
        UpdateSelection();
        string? failure = null;
        try { await action(); }
        catch (OperationCanceledException) { failure = "操作已取消。已完成的项目会保留，请查看刷新后的列表。"; }
        catch (Exception ex) { failure = FriendlyError(ex); }
        finally
        {
            ViewModel.IsMutating = false;
            if (!closed)
            {
                await NavigateAsync(ViewModel.CurrentPath, NavigationKind.Refresh);
                if (failure is not null) ViewModel.Error = failure;
            }
        }
    }

    private async Task<string?> PromptNameAsync(string title, string initial)
    {
        if (!CanOperate) return null;
        var input = new TextBox { Text = initial, PlaceholderText = "请输入名称", MinWidth = 280 };
        var dialog = new ContentDialog
        {
            Title = title, Content = input, PrimaryButtonText = "确定", CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary, XamlRoot = Root.XamlRoot,
            RequestedTheme = Root.RequestedTheme
        };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            try { PathRules.ValidateName(input.Text); }
            catch (ArgumentException ex) { args.Cancel = true; input.Header = ex.Message; }
        };
        dialog.Opened += (_, _) => { input.Focus(FocusState.Programmatic); input.SelectAll(); };
        dialogOpen = true;
        try { return await dialog.ShowAsync() == ContentDialogResult.Primary ? input.Text : null; }
        finally { dialogOpen = false; }
    }

    private async void CreateFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!CanOperate || navigation.Current is null) return;
            var parent = ViewModel.CurrentPath;
            var name = await PromptNameAsync("新建文件夹", "新建文件夹");
            if (name is not null) await MutateAsync(() => fileSystem.CreateFolderAsync(parent, name, lifetime.Token));
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async void RenameSelected(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!CanOperate || Selected() is not [var entry]) return;
            var name = await PromptNameAsync("重命名", entry.Name);
            if (name is not null) await MutateAsync(() => fileSystem.RenameAsync(entry.FullPath, name, lifetime.Token));
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async void CopySelected(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!CanOperate) return;
            var selected = Selected();
            if (selected.Length == 0) return;
            var items = new List<IStorageItem>();
            foreach (var entry in selected)
                items.Add(entry.IsDirectory ? await StorageFolder.GetFolderFromPathAsync(entry.FullPath) :
                    await StorageFile.GetFileFromPathAsync(entry.FullPath));
            var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
            package.SetStorageItems(items);
            Clipboard.SetContent(package);
            ViewModel.Status = $"已复制 {selected.Length} 项，可在目标文件夹中粘贴";
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async void Paste(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!CanOperate || navigation.Current is null) return;
            var destination = ViewModel.CurrentPath;
            var data = Clipboard.GetContent();
            if (!data.Contains(StandardDataFormats.StorageItems))
            {
                ViewModel.Error = "剪贴板中没有可粘贴的文件或文件夹。";
                return;
            }
            // Retrieve clipboard data inside the operation gate. Always copy, including
            // an external application's cut payload; source items are never removed.
            await MutateAsync(async () =>
            {
                var sources = await data.GetStorageItemsAsync();
                var failed = new List<string>();
                foreach (var source in sources)
                {
                    lifetime.Token.ThrowIfCancellationRequested();
                    try { await fileSystem.CopyAsync(source.Path, destination, lifetime.Token); }
                    catch (IOException ex) { failed.Add($"{source.Name}：{ex.Message}"); }
                    catch (UnauthorizedAccessException ex) { failed.Add($"{source.Name}：{ex.Message}"); }
                }
                if (failed.Count > 0) throw new IOException(string.Join(Environment.NewLine, failed.Take(5)) +
                    (failed.Count > 5 ? $"\n另有 {failed.Count - 5} 项失败。" : ""));
            });
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async void DeleteSelected(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!CanOperate) return;
            var selection = Selected();
            if (selection.Length == 0) return;
            var dialog = new ContentDialog
            {
                Title = $"将 {selection.Length} 个项目移到回收站？",
                Content = "文件将交给 Windows 回收站处理。若该位置不支持回收站，Windows 会另行确认。",
                PrimaryButtonText = "移到回收站", CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close, XamlRoot = Root.XamlRoot,
                RequestedTheme = Root.RequestedTheme
            };
            dialogOpen = true;
            ContentDialogResult answer;
            try { answer = await dialog.ShowAsync(); }
            finally { dialogOpen = false; }
            if (answer == ContentDialogResult.Primary)
                await MutateAsync(() =>
                {
                    RecycleBin.Move(selection.Select(x => x.FullPath).ToArray(), WindowNative.GetWindowHandle(this));
                    return Task.CompletedTask;
                });
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async void OnLocationChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer?.Tag is string path) await NavigateAsync(path);
    }
    private async void OnAddressKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            await NavigateAsync(Address.Text);
        }
    }
    private async void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        var element = e.OriginalSource as DependencyObject;
        while (element is not null)
        {
            if (element is FrameworkElement { DataContext: FileEntry entry })
            {
                await OpenAsync(entry);
                e.Handled = true;
                return;
            }
            element = VisualTreeHelper.GetParent(element);
        }
    }
    private async void OnListKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter) { e.Handled = true; await OpenAsync(Selected().FirstOrDefault()); }
        else if (e.Key == VirtualKey.F2) { e.Handled = true; RenameSelected(sender, e); }
        else if (e.Key == VirtualKey.Delete) { e.Handled = true; DeleteSelected(sender, e); }
    }
    private async void OpenSelected(object sender, RoutedEventArgs e) => await OpenAsync(Selected().FirstOrDefault());
    private async void GoBack(object sender, RoutedEventArgs e) => await NavigateAsync(ViewModel.CurrentPath, NavigationKind.Back);
    private async void GoForward(object sender, RoutedEventArgs e) => await NavigateAsync(ViewModel.CurrentPath, NavigationKind.Forward);
    private async void GoUp(object sender, RoutedEventArgs e)
    {
        if (navigation.Parent is string path) await NavigateAsync(path);
    }
    private async void Refresh(object sender, RoutedEventArgs e)
    {
        if (navigation.Current is not null) await NavigateAsync(ViewModel.CurrentPath, NavigationKind.Refresh);
    }
    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateSelection();
    private void OnFilterChanged(object sender, TextChangedEventArgs e) { if (loaded) ApplyFilter(); }
    private async void OnHiddenChanged(object sender, RoutedEventArgs e)
    {
        if (!loaded) return;
        showHidden = HiddenToggle.IsOn;
        if (navigation.Current is not null) await NavigateAsync(ViewModel.CurrentPath, NavigationKind.Refresh);
    }
    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Root is null) return;
        Root.RequestedTheme = ThemeChoice.SelectedIndex switch
        {
            1 => ElementTheme.Light, 2 => ElementTheme.Dark, _ => ElementTheme.Default
        };
    }
    private void FocusAddress(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (!ViewModel.IsMutating) { Address.Focus(FocusState.Keyboard); Address.SelectAll(); }
        args.Handled = true;
    }
    private void RefreshAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) { Refresh(sender, new()); args.Handled = true; }
    private void BackAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) { GoBack(sender, new()); args.Handled = true; }
    private void ForwardAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) { GoForward(sender, new()); args.Handled = true; }
    private void ShowError(Exception ex) { if (!closed) ViewModel.Error = FriendlyError(ex); }
    private static string FriendlyError(Exception ex) => ex switch
    {
        UnauthorizedAccessException => "没有访问权限。请使用有权访问的位置，或检查文件权限。",
        DirectoryNotFoundException => "文件夹不存在或磁盘已断开，请刷新或选择其他位置。",
        FileNotFoundException => "项目已被移动或删除，请刷新列表。",
        COMException => "Windows 无法完成此操作。请检查文件是否仍然存在、是否被占用。",
        _ => ex.Message
    };
}
