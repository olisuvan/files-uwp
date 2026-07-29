# CLAUDE.md

Guidance for AI assistants working in this repository.

## What this is

**Files UWP** — a Universal Windows Platform file manager for Windows 10, written in C# + XAML,
intended as a modern replacement for Windows File Explorer. Package version is currently `0.4.6.0`
(`Package.appxmanifest` → `<Identity Version="…">`).

Single project, single solution: `Files.sln` → `Files.csproj` (assembly and root namespace `Files`).

## Build & run

This project **cannot be built or run on Linux/macOS or from this container.** It requires:

- Windows 10 (build 17134 / 1803 or newer — `TargetPlatformMinVersion`)
- Visual Studio 2017+ (`MinimumVisualStudioVersion = 14`) with the UWP workload
- Windows 10 SDK `10.0.18327.0` (`TargetPlatformVersion`)

Open `Files.sln` in Visual Studio, pick a configuration (`Debug|x86` is the default) and F5 to deploy
locally. Configurations exist for `Debug`/`Release` × `x86`/`x64`/`ARM`.

Notes that bite people:

- `Files_TemporaryKey.pfx` is referenced by `Files.csproj` (`PackageCertificateKeyFile`) but is
  **gitignored and not in the repo**. A fresh clone will fail to build until you create a new test
  certificate (Package.appxmanifest → Packaging → *Choose Certificate…* → *Create test certificate*).
  `PackageCertificateThumbprint` in the csproj refers to the original author's cert and will not match yours.
- `AppxPackageDir` is hardcoded to `C:\Users\Luke\Documents\Packages\` — change it locally if you
  produce packages; don't commit that change.
- **There is no test suite, no linter, and no CI.** Verification is manual: build and exercise the UI.
  Don't claim a change is tested when it isn't — say it compiles-by-inspection only if you can't build.

## Layout

All source lives at the repository root (no `src/` folder). File → responsibility:

| File | Contains |
| --- | --- |
| `App.xaml` / `.cs` | App entry point, theme resolution at startup, acrylic brush theme dictionaries |
| `MainPage.xaml` / `.cs` | Shell: custom title bar, `NavigationView` sidebar, `ContentFrame`, drive enumeration |
| `YourHome.xaml` / `.cs` | The "Home" landing page — gradient location cards, recents |
| `GenericFileBrowser.xaml` / `.cs` | Details/list view (`Microsoft.Toolkit.Uwp.UI.Controls.DataGrid`), command bar, context menus |
| `PhotoAlbum.xaml` / `.cs` | Grid/thumbnail view used for the Pictures library |
| `ClassicMode.xaml` / `.cs` | Experimental Explorer-style `TreeView` + list layout; **not reachable from the UI** |
| `FileLoader.cs` | `ItemListPresenter` namespace: `ListedItem` model + `ItemViewModel` directory enumeration |
| `NavigationSystem.cs` | `Navigation` namespace: back/forward `History`, `NavigationActions`, `PhotoAlbumNavActions`, `UniversalPath` |
| `ItemInteractions.cs` | `Interacts` namespace: `Interaction` — open/share/delete/rename/cut/copy/paste, collision handling |
| `ProgressBoxSystem.cs` | **Global namespace**: a stack of tiny `INotifyPropertyChanged` state holders |
| `LocationsList.cs` | `Locations` namespace: the hardcoded home-screen card list |
| `AddItem.xaml` / `.cs` | "New item" flyout page, opened in a separate app view |
| `Settings.xaml` / `.cs` + `SettingsPages/` | Settings shell and its `Personalization` / `Preferences` / `About` pages |
| `Assets/` | Tile/splash/store logos (scale-100…400 variants) and `Cards/Gradients/*.png` |
| `Properties/` | `AssemblyInfo.cs`, `Default.rd.xml` (.NET Native runtime directives) |

### Namespaces do not follow folders

This is deliberate in the existing code and worth internalizing before you go looking for a type:

- `Files` — all XAML pages (including `SettingsPages/` which uses `Files.SettingsPages`)
- `ItemListPresenter` — `FileLoader.cs`
- `Navigation` — `NavigationSystem.cs`
- `Interacts` — `ItemInteractions.cs`
- `Locations` — `LocationsList.cs`
- *(global)* — `ProgressBoxSystem.cs`

XAML files alias these as `local`, `local1`, `local2`, `local3`, `uilib`/`UILib`, `controls`.

### Adding files

`Files.csproj` is the **old-style, non-SDK** MSBuild format — there is no glob. Any new `.cs` must be
added to the `<Compile>` `ItemGroup`, any new `.xaml` needs both a `<Page>` entry (with
`<Generator>MSBuild:Compile</Generator>`) and a `<Compile … DependentUpon>` entry for its code-behind,
and any new asset needs a `<Content Include>`. Forgetting this produces confusing "type does not
exist" errors.

## Dependencies

- `Microsoft.NETCore.UniversalWindowsPlatform` 6.1.9
- `Microsoft.Toolkit.Uwp` (+ `.UI`, `.UI.Animations`, `.UI.Controls`, `.UI.Controls.DataGrid`, `.DeveloperTools`) 5.0.0
- `Microsoft.UI.Xaml` 2.1.190131001-**prerelease** — WinUI 2.1 preview. `NavigationView` and `TreeView`
  come from here, which is why nav code is littered with fully-qualified
  `Microsoft.UI.Xaml.Controls.NavigationViewItem` references (they collide with `Windows.UI.Xaml.Controls`).

## Architecture & conventions

The codebase predates any MVVM discipline. Match what's there rather than importing a new pattern
unless you were asked to refactor.

### Static global state

Cross-page communication happens through public statics, not DI or messaging:

- `ItemViewModel.FilesAndFolders` — the single `ObservableCollection<ListedItem>` every view binds to
- `MainPage.nv`, `MainPage.accessibleContentFrame`, `MainPage.accessibleAutoSuggestBox` — the shell's
  controls, published statically so navigation helpers can drive them
- `GenericFileBrowser.data` (the `DataGrid`), `.context`, `.collisionBox`, `.reviewBox`, `.GFBPageName`
- `PhotoAlbum.gv`, `.PAPageName`
- `Interaction.page` — the page currently being interacted with; handlers branch on `page.Name`
  (`"GenericItemView"`, `"PhotoAlbumViewer"`, `"ClassicModePage"`)

### The notify-property wrapper pattern

Instead of one view model with many properties, each observable value gets its own tiny
`INotifyPropertyChanged` class, instantiated as a static singleton and exposed through a static
property. See `ProgressBoxSystem.cs` (`ProgressUIHeader`, `ProgressUIPath`, `CollisionBoxHeader`, …),
`BackState`/`ForwardState`/`UniversalPath` in `NavigationSystem.cs`, `SelectItem` in `MainPage.xaml.cs`,
`PasteState` in `ItemInteractions.cs`, `EmptyFolderTextState` in `GenericFileBrowser.xaml.cs`,
`ThemeValueClass` in `SettingsPages/Personalization.xaml.cs`.

XAML binds straight to those statics:

```xml
Visibility="{x:Bind local2:ItemViewModel.TextState.isVisible, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
```

Note the lowercase-`isVisible`/`path`/`prog`/`isEnabled` member names — they're inconsistent with normal
C# casing but the XAML depends on them, so don't rename without updating every binding.

New observable state should follow this same shape for consistency (a class with a public backing field,
a property that compares before assigning, and `NotifyPropertyChanged("Name")`).

### Event wiring lives in code-behind

Most handlers are attached imperatively in a page's constructor or `OnNavigatedTo`, not via XAML
`Click=` attributes — e.g. `GenericFileBrowser()` wires `OpenItem.Click`, `DeleteItem.Click`,
`Back.Click`, `Paste.Click`, `Clipboard.ContentChanged` and more. Add new commands the same way.
Handlers are `public static async void` by convention (UWP event handlers, so `async void` is expected).

### Directory loading

`new ItemViewModel(path, page)` *is* the load operation — construction kicks off `GetItemsAsync`, which:

1. Resolves `StorageFolder.GetFolderFromPathAsync(path)`, lists folders then files
2. Pushes `ListedItem`s into `FilesAndFolders` (or `ClassicFolderList`/`ClassicFileList` for classic mode)
3. Fetches a per-file thumbnail — 20px `ListView` mode normally, 275px `PicturesView` mode when
   `isPhotoAlbumMode`
4. Shows a progress bar only when the folder has ≥ 250 items
5. Cooperatively cancels via the `IsStopRequested` / `IsTerminated` static flag pair — navigation calls
   set `IsStopRequested = true` and the enumeration loops bail out on the next iteration

Callers are expected to `ItemViewModel.FilesAndFolders.Clear()` *before* constructing a new
`ItemViewModel`. Two failure paths are handled explicitly: `UnauthorizedAccessException` →
`DisplayConsentDialog()`, and `COMException` (drive yanked) → message box + navigate back to `MainPage`.

### Known-library special-casing — the big footgun

Navigating to Desktop/Documents/Downloads/Pictures/Music/Videos/OneDrive is special-cased with a long
`if/else if` chain that (a) sets the friendly `GenericFileBrowser.P.path` label instead of the raw path,
(b) walks `MainPage.nv.MenuItems` to sync the sidebar's selected item by `x:Name`, and (c) routes
Pictures to `PhotoAlbum` while everything else goes to `GenericFileBrowser`.

**That same chain is duplicated in at least seven places**: `NavigationActions.Back_Click`,
`Forward_Click`, `Refresh_Click`, `PhotoAlbumNavActions.Back_Click`/`Refresh_Click`,
`GenericFileBrowser.OnNavigatedTo`, `PhotoAlbum.OnNavigatedTo`, `Interaction.List_ItemClick`, and
`YourHome.CardPressed`. If you add or change a known location, grep for
`SpecialFolder.MyDocuments` and update **every** occurrence — partial edits produce a UI where the
sidebar highlight and breadcrumb disagree with the actual folder.

Sidebar item names to match against: `homeIc`, `DesktopIC`, `DownloadsIC`, `DocumentsIC`, `PicturesIC`,
`MusicIC`, `VideosIC`, `LocD_IC`, `OneD_IC`. Drives beyond C: are added at runtime by
`MainPage.PopulateNavViewWithExternalDrives()`, which probes `A:`–`Z:` and labels each
`"Removable Drive (X:\)"` or `"Local Disk (X:\)"` depending on whether it appears under
`KnownFolders.RemovableDevices`; those items carry the drive letter in `Tag`.

### Navigation history

`Navigation.History` keeps two plain `List<string>`s: `HistoryList` (capped at 25, shifted left when
full) and `ForwardList`. `BackState.isEnabled` / `ForwardState.isEnabled` drive the command bar buttons.
Opening a folder clears `ForwardList`.

### File operations

In `Interacts.Interaction`. Copy/paste goes through the system `Clipboard` +
`DataPackage`/`StandardDataFormats.StorageItems`; paste recurses with `CloneDirectoryAsync` and handles
name collisions through the `CollisionBox` / `ReviewBox` `ContentDialog`s in `GenericFileBrowser.xaml`
plus the `isSkipEnabled` / `isReplaceEnabled` / `isReviewEnabled` / `isLoopPaused` flags. Rename is
in-place `DataGrid` cell editing — `RenameItem_Click` calls `BeginEdit()`, and the actual
`RenameAsync` happens in `AllView_CellEditEnded`. `CutItem_Click` is an empty stub. Delete is permanent
(`StorageDeleteOption.Default`) and there is no confirmation dialog — the commented-out code for one is
still in `DeleteItem_Click`.

Prefer `ContentDialog` over `MessageDialog` for new UI — that migration is in progress (commit
"Standardize MessageBoxes With Transition to ContentDialog") but `MessageDialog` still lingers.

### Capabilities & permissions

`Package.appxmanifest` declares `internetClient`, `removableStorage`, and the restricted
`broadFileSystemAccess`. That last one is why an `UnauthorizedAccessException` sends the user to
`ms-settings:privacy-broadfilesystemaccess` (`Interaction.GrantAccessPermissionHandler`). If you touch
capabilities, remember restricted ones require the `rescap` namespace declaration and Store
justification. `SupportsMultipleInstances` is on; `runFullTrust` and the app-service extension are
present but commented out.

### Theming

Theme is a string in `ApplicationData.Current.LocalSettings.Values["theme"]` (`"Light"` / `"Dark"` /
`"Default"`). It's read in the `App` constructor and applied via `RequestedTheme` — which means
**theme changes only take effect after an app restart**, and `Personalization.xaml.cs` shows a
`RestartReminder` accordingly.

### Icons

UI glyphs are Segoe MDL2 Assets code points, written either as XAML `Glyph="&#xE76B;"` or as C# escapes
(`new SymbolIcon((Symbol)0xE88E)`, `Icon = "\xE896"`). Keep to that font rather than adding image assets.

## Style

Match the surrounding file. Observed conventions:

- 4-space indent, Allman braces, `using` blocks at the top ordered loosely (not alphabetized)
- PascalCase for methods/classes; the observable-property members are lowercase (see above)
- Event handlers named `Control_Event` (`AllView_RightTapped`, `navView_Loaded`, `CardPressed`)
- `Debug.WriteLine` is the logging mechanism; there is no logging framework
- Comments are sparse and explanatory — several loops are annotated inline; keep that density
- Dead/commented-out code is common in this repo. Don't add more of it, but don't take a cleanup pass
  through unrelated files either unless asked.

## Git

- Default branch: `master`.
- Commit messages are short Title Case phrases, imperative-ish, no prefix or scope:
  `Implement Delete Function and Fix Removable Drive Bugs`, `Update PhotoAlbum Layout UI`,
  `Bump version number to 0.4.6`.
- `.gitignore` is the standard Visual Studio one; `*.pfx`, `bin/`, `obj/`, `.vs/` are excluded.
- `.gitattributes` normalizes line endings — C#/XAML files are treated as text, so don't fight it.
- Version bumps mean editing `Identity Version` in `Package.appxmanifest` (four-part, e.g. `0.4.6.0`).
