param(
    [Parameter(Mandatory = $true)]
    [string]$PublishDirectory
)

$ErrorActionPreference = 'Stop'
$directory = (Resolve-Path -LiteralPath $PublishDirectory).Path
$executable = Join-Path $directory 'Files.WinUI.exe'
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw "Published application is missing: $executable"
}

$application = $null
try {
    $application = Start-Process -FilePath $executable -WorkingDirectory $directory -PassThru
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    $windowCreated = $false
    while ([DateTime]::UtcNow -lt $deadline) {
        $application.Refresh()
        if ($application.HasExited) {
            throw "Application exited before creating a window. Exit code: $($application.ExitCode)"
        }
        if ($application.MainWindowHandle -ne [IntPtr]::Zero) {
            $windowCreated = $true
            break
        }
        Start-Sleep -Milliseconds 500
    }
    if (-not $windowCreated) {
        throw 'Application did not create a top-level window within 30 seconds.'
    }
    # Let Loaded and the initial directory read finish; catches immediate startup failures.
    Start-Sleep -Seconds 5
    $application.Refresh()
    if ($application.HasExited) {
        throw "Application exited during initial loading. Exit code: $($application.ExitCode)"
    }
    Write-Host "Startup smoke check passed: $($application.MainWindowTitle)"
}
finally {
    if ($null -ne $application) {
        $application.Refresh()
        if (-not $application.HasExited) {
            $null = $application.CloseMainWindow()
            if (-not $application.WaitForExit(5000)) {
                $application.Kill()
                $application.WaitForExit()
            }
        }
        $application.Dispose()
    }
}
