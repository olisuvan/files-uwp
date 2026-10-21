# Files — WinUI 3 modernization

A Windows file manager with a new WinUI 3 desktop implementation.
The original 2019 UWP application remains in Files.sln; open **Files.Modern.slnx**
for the modern implementation.

## Current status

Implemented: drive/folder navigation, back/forward/up, address entry, current-folder
name filtering, hidden-item toggle, system/light/dark themes, folder creation,
rename, clipboard copy/paste and Windows Recycle Bin integration.

The core regression suite passes locally and on Windows/Linux CI. Windows
x64/ARM64 publish builds, including XAML resources, and the x64 application
startup check passed for source commit 407a06e52d16572c8ab7b3ba82c35fb9bd348794.
The x64 UI also passed automated path navigation, filtering, filter reset and
clean-close checks across three launches.
See [the successful workflow](https://github.com/olisuvan/files-uwp/actions/runs/39544883657)
for complete published folders and the `Files-Windows-evaluation` artifact with
an actual running screenshot and raw performance samples.
**Full Windows interaction acceptance has not yet been completed.** See
[the feature/performance assessment](docs/current-assessment.zh-CN.md) and
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
Windows, builds portable x64/ARM64 folders, and checks x64 startup by launching
the published executable. The x64 job also measures core operations and captures
the actual UI with startup/memory samples. These CI measurements are not a
performance guarantee for another computer. See the checks on the modernization
pull request for the remote results of the corresponding commit.

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

## Review the current upgrade

The current source and validation history are in
[PR #1](https://github.com/olisuvan/files-uwp/pull/1).
From an existing checkout:

~~~powershell
git fetch origin codex/winui3-modernization
git switch --track origin/codex/winui3-modernization
~~~

Open Files.Modern.slnx. Download the complete application folder from a successful
workflow run linked to that PR. The original source-only archive captures the
first delivery and predates subsequent Windows publication/startup fixes.

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

