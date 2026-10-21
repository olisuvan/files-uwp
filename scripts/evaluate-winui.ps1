param(
    [Parameter(Mandatory = $true)][string]$PublishDirectory,
    [Parameter(Mandatory = $true)][string]$OutputDirectory
)

# Run in Windows PowerShell 5.1 with -STA (UI Automation and screen capture).
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms, System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class EvaluationWindow {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window, out RECT rectangle);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
}
'@
$null = [EvaluationWindow]::SetProcessDPIAware()
$directory = (Resolve-Path -LiteralPath $PublishDirectory).Path
$output = [IO.Path]::GetFullPath($OutputDirectory)
$null = [IO.Directory]::CreateDirectory($output)
$fixture = Join-Path $env:TEMP ('Files-Demo-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$null = [IO.Directory]::CreateDirectory($fixture)
foreach ($name in @('Design', 'Documents', 'Projects')) {
    $null = [IO.Directory]::CreateDirectory((Join-Path $fixture $name))
}
[IO.File]::WriteAllText((Join-Path $fixture 'Budget.csv'), "Item,Amount`r`nDesign,1200`r`nDevelopment,3600")
[IO.File]::WriteAllText((Join-Path $fixture 'Project-plan.md'), '# WinUI 3 modernization')
[IO.File]::WriteAllText((Join-Path $fixture 'Release-notes.txt'), 'Navigation, safe copy, rename and recycle bin.')
[IO.File]::WriteAllText((Join-Path $fixture 'Sample-data.json'), '{"status":"ready"}')
[IO.File]::WriteAllText((Join-Path $fixture 'Welcome.md'), '# Welcome to Files')

function Find-Control($root, [string]$id) {
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id)
    return $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

function Wait-Until([scriptblock]$condition, [string]$message) {
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    while ([DateTime]::UtcNow -lt $deadline) {
        if (& $condition) { return }
        Start-Sleep -Milliseconds 100
    }
    throw $message
}

function Get-VisibleNames($root) {
    $list = Find-Control $root 'FileList'
    if ($null -eq $list) { return @() }
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Text)
    return @($list.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition) |
        ForEach-Object { $_.Current.Name })
}

$samples = @()
$application = $null
try {
    for ($iteration = 0; $iteration -lt 3; $iteration++) {
        $watch = [Diagnostics.Stopwatch]::StartNew()
        $application = Start-Process -FilePath (Join-Path $directory 'Files.WinUI.exe') -WorkingDirectory $directory -PassThru
        Wait-Until {
            $application.Refresh()
            if ($application.HasExited) { throw "App exited: $($application.ExitCode)" }
            $application.MainWindowHandle -ne [IntPtr]::Zero
        } 'No top-level window.'
        $windowMs = $watch.Elapsed.TotalMilliseconds
        $root = [System.Windows.Automation.AutomationElement]::FromHandle($application.MainWindowHandle)
        Wait-Until {
            $address = Find-Control $root 'Address'
            if ($null -eq $address) { return $false }
            $value = $address.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
            return -not [string]::IsNullOrWhiteSpace($value.Current.Value)
        } 'Initial folder did not load.'
        $initialFolderMs = $watch.Elapsed.TotalMilliseconds

        $null = [EvaluationWindow]::ShowWindow($application.MainWindowHandle, 9)
        $area = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
        $width = [Math]::Min(1120, $area.Width)
        $height = [Math]::Min(760, $area.Height)
        $null = [EvaluationWindow]::SetWindowPos($application.MainWindowHandle, [IntPtr]::Zero, $area.X, $area.Y, $width, $height, 0x0040)
        $null = [EvaluationWindow]::SetForegroundWindow($application.MainWindowHandle)
        $address = Find-Control $root 'Address'
        $address.SetFocus()
        $value = $address.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
        $value.SetValue($fixture)
        $navigation = [Diagnostics.Stopwatch]::StartNew()
        [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
        Wait-Until { (Get-VisibleNames $root) -contains 'Budget.csv' } 'Fixture folder did not render.'
        $navigation.Stop()

        # A real UI filter round-trip, including binding/layout and UIA polling overhead.
        $search = Find-Control $root 'SearchBox'
        $searchValue = $search.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
        $filterWatch = [Diagnostics.Stopwatch]::StartNew()
        $searchValue.SetValue('Budget')
        Wait-Until {
            $names = Get-VisibleNames $root
            return ($names -contains 'Budget.csv') -and ($names -notcontains 'Design')
        } 'UI filter did not apply.'
        $filterWatch.Stop()
        $searchValue.SetValue('')
        Wait-Until { (Get-VisibleNames $root) -contains 'Design' } 'Clearing the filter did not restore folders.'
        Start-Sleep -Seconds 2
        $application.Refresh()
        if ($application.HasExited) { throw 'App exited before capture.' }
        $samples += [ordered]@{
            Iteration = $iteration + 1
            WindowCreatedMs = $windowMs
            InitialFolderCommittedMs = $initialFolderMs
            FixtureNavigationObservedMs = $navigation.Elapsed.TotalMilliseconds
            SmallFolderFilterObservedMs = $filterWatch.Elapsed.TotalMilliseconds
            IdleWorkingSetMiB = $application.WorkingSet64 / 1MB
            IdlePrivateBytesMiB = $application.PrivateMemorySize64 / 1MB
            TotalCpuMsSinceLaunch = $application.TotalProcessorTime.TotalMilliseconds
            WindowTitle = $application.MainWindowTitle
        }
        if ($iteration -eq 2) {
            $null = [EvaluationWindow]::SetForegroundWindow($application.MainWindowHandle)
            $address.SetFocus()
            Start-Sleep -Milliseconds 500
            $rectangle = New-Object EvaluationWindow+RECT
            if (-not [EvaluationWindow]::GetWindowRect($application.MainWindowHandle, [ref]$rectangle)) {
                throw 'Cannot read window bounds.'
            }
            $bitmap = New-Object Drawing.Bitmap(($rectangle.Right - $rectangle.Left), ($rectangle.Bottom - $rectangle.Top))
            $graphics = [Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.CopyFromScreen($rectangle.Left, $rectangle.Top, 0, 0, $bitmap.Size)
                $bitmap.Save((Join-Path $output 'Files-WinUI3-running.png'), [Drawing.Imaging.ImageFormat]::Png)
            }
            finally { $graphics.Dispose(); $bitmap.Dispose() }
        }
        $null = $application.CloseMainWindow()
        if (-not $application.WaitForExit(5000)) { throw 'Window did not close cleanly.' }
        $application.Dispose()
        $application = $null
    }
    [ordered]@{
        Schema = 1
        SourceCommit = $env:GITHUB_SHA
        MeasuredAtUtc = [DateTime]::UtcNow.ToString('o')
        OS = [Environment]::OSVersion.VersionString
        LogicalProcessors = [Environment]::ProcessorCount
        RunnerImage = $env:ImageVersion
        Method = '3 sequential launches on one Windows CI VM, no OS-cache purge; 100ms polling plus UIA overhead; initial folder committed is not first paint; fixture has 8 entries; idle memory after filter reset and 2s pause; screenshot captures actual application window.'
        Checks = @('window started', 'initial folder committed', 'address navigation to fixture', 'filter to one file', 'clear filter restores folders', 'clean window close')
        Samples = $samples
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $output 'ui-performance.json') -Encoding UTF8
    Get-Content -LiteralPath (Join-Path $output 'ui-performance.json')
}
finally {
    if ($null -ne $application) {
        $application.Refresh()
        if (-not $application.HasExited) { $application.Kill(); $application.WaitForExit() }
        $application.Dispose()
    }
    Remove-Item -LiteralPath $fixture -Recurse -Force
}
