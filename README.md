# Files — WinUI 3 modernization

A Windows file manager with a new WinUI 3 desktop implementation.
The original 2019 UWP application remains in Files.sln; open **Files.Modern.slnx**
for the modern implementation.

## Current status

Implemented: drive/folder navigation, back/forward/up, address entry, current-folder
name filtering, hidden-item toggle, system/light/dark themes, folder creation,
rename, clipboard copy/paste and Windows Recycle Bin integration.

The core regression suite passes locally. The WinUI C# backend passes an API-only
compile check against the real SDK references. **Full Windows/XAML compilation
and UI acceptance have not yet been completed.** See
[the review and validation checklist](docs/modernization-review.zh-CN.md).

The legacy photo album, classic view, thumbnails, drag-and-drop and overwrite
workflow have not been ported. Paste copies only and reports name collisions
without overwriting. Filtering is not a recursive search. Appearance preferences
are session-only.

## Requirements

- Build and run the UI on Windows; Windows 11 is recommended.
- Visual Studio 2026 with the WinUI application development workload and .NET 10 SDK.
- NuGet access for Windows App SDK **2.5.1** and Windows SDK BuildTools
  **10.0.26100.7175**, pinned in the project.
- The declared minimum OS is Windows 10 build 19041. Support depends on the
  Windows release and SDK support lifecycle.

## Verify the core

From the repository root, on Windows or Linux:

~~~powershell
dotnet run --project tests/Files.Core.Tests -c Release
~~~

This is a dependency-free executable regression suite, not a dotnet test project.
It returns a nonzero exit code on any failure. The Linux run covers 36 cases;
two symlink cases are skipped on Windows because creation requires extra privileges.

## Build and run on Windows

Open Files.Modern.slnx in Visual Studio 2026, select Files.WinUI as startup
project and x64 (or ARM64 on native hardware). The application is unpackaged.

Alternatively, use PowerShell on Windows with the .NET 10 SDK:

~~~powershell
dotnet publish src\Files.WinUI\Files.WinUI.csproj -c Release -p:Platform=x64 -r win-x64 -o artifacts\win-x64
.\artifacts\win-x64\Files.WinUI.exe
~~~

For ARM64, replace x64 with ARM64 for Platform and use win-arm64 for the runtime
and output directory. Distribute the **entire published folder**: both .NET and
Windows App SDK are self-contained. No old UWP certificate is required. This
change does not create MSIX packaging or automatic updates.

The workflow in .github/workflows/winui.yml runs core regressions on Linux and
Windows and builds portable x64/ARM64 folders. It has not yet been run remotely.

## Operation behavior

- Copy stages each selected root item before committing it with a move that
  refuses collisions. Source and existing destination content are not removed.
- Links/junctions are rejected for writes and recursive copy.
- Closing the window requests cancellation of managed file operations.
- Copying several selected items can partially succeed; errors remain visible
  after the list refreshes.
- Delete asks for confirmation and delegates to Windows. Windows may ask about
  permanent deletion on locations without a Recycle Bin. Validate on disposable
  test files before relying on recovery behavior.
- F5 refreshes; Alt+Left/Right navigates; Ctrl+L focuses the address bar.
  With the list focused, Enter opens, F2 renames and Delete starts confirmation.

## Apply the delivered patch

The update targets upstream commit a3f5449dbdc065bb2c2cfd4ecb160ccbb76699b4.
In a clean checkout, create a review branch and check before applying:

~~~powershell
git switch -c review/winui3-modernization
git apply --check files-winui3-modernization.patch
git apply files-winui3-modernization.patch
~~~

Alternatively, copy the updates directory from the delivered archive over that
checkout. It contains changed/new text sources, not a full repository or a
compiled application. Preserve the existing LICENSE and assets.

## Legacy UWP application

Files.sln and the root XAML files remain the original UWP application.
Only its deletion selection/error/refresh handling was fixed here. Its old
copy/overwrite logic still has the documented risks; use the modern project for
the replacement implementation. Legacy signing/dependency settings need a
separate Windows build review.

Original project screenshots:

![Files UWP home](Assets/FilesHome.jpg)
![Files UWP drive](Assets/FilesDrive.jpg)

Existing license and author attribution remain applicable; see LICENSE.

