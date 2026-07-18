[CmdletBinding()]
param(
    [int]$ProcessId,
    [string]$ExecutablePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not ("ClearVisionStudioUiNext.NativeDpiProbe" -as [type])) {
    Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace ClearVisionStudioUiNext {
    public static class NativeDpiProbe {
        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct Rect {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

        [DllImport("user32.dll")]
        public static extern IntPtr GetWindowDpiAwarenessContext(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool AreDpiAwarenessContextsEqual(IntPtr first, IntPtr second);

        [DllImport("user32.dll")]
        public static extern int GetAwarenessFromDpiAwarenessContext(IntPtr value);

        [DllImport("user32.dll")]
        public static extern uint GetDpiForWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);

        [DllImport("user32.dll")]
        public static extern bool GetClientRect(IntPtr hWnd, out Rect rect);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint access, bool inheritHandle, uint processId);

        [DllImport("kernel32.dll")]
        public static extern bool CloseHandle(IntPtr handle);

        [DllImport("shcore.dll")]
        public static extern int GetProcessDpiAwareness(IntPtr processHandle, out int awareness);

        public static IntPtr FindWindow(uint processId) {
            IntPtr result = IntPtr.Zero;
            EnumWindows((window, _) => {
                uint candidate;
                GetWindowThreadProcessId(window, out candidate);
                if (candidate == processId) {
                    result = window;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            return result;
        }

        public static string ReadTitle(IntPtr window) {
            var value = new StringBuilder(512);
            GetWindowText(window, value, value.Capacity);
            return value.ToString();
        }
    }
}
"@
}

function Resolve-DesktopProcess {
    if ($ProcessId -gt 0) {
        $resolved = Get-CimInstance Win32_Process -Filter "ProcessId = $ProcessId" -ErrorAction Stop
        if ($null -eq $resolved) {
            throw "Desktop process $ProcessId was not found."
        }
        return $resolved
    }

    $expectedPath = if ([string]::IsNullOrWhiteSpace($ExecutablePath)) {
        $null
    } else {
        [System.IO.Path]::GetFullPath($ExecutablePath)
    }
    $candidates = @(Get-CimInstance Win32_Process -Filter "Name = 'ClearVision.Product.Desktop.exe'" -ErrorAction Stop)
    if ($expectedPath) {
        $candidates = @($candidates | Where-Object {
            -not [string]::IsNullOrWhiteSpace($_.ExecutablePath) -and
            [string]::Equals(
                [System.IO.Path]::GetFullPath($_.ExecutablePath),
                $expectedPath,
                [System.StringComparison]::OrdinalIgnoreCase)
        })
    }
    if ($candidates.Count -ne 1) {
        throw "Expected exactly one matching Desktop process, found $($candidates.Count)."
    }
    return $candidates[0]
}

function Get-DescendantProcesses {
    param(
        [Parameter(Mandatory = $true)]
        [int]$RootProcessId
    )

    $allProcesses = @(Get-CimInstance Win32_Process -ErrorAction Stop)
    $pendingParents = [System.Collections.Generic.Queue[int]]::new()
    $pendingParents.Enqueue($RootProcessId)
    $seen = [System.Collections.Generic.HashSet[int]]::new()
    $result = [System.Collections.Generic.List[object]]::new()

    while ($pendingParents.Count -gt 0) {
        $parent = $pendingParents.Dequeue()
        foreach ($candidate in $allProcesses | Where-Object { [int]$_.ParentProcessId -eq $parent }) {
            $candidateId = [int]$candidate.ProcessId
            if (-not $seen.Add($candidateId)) {
                continue
            }
            $pendingParents.Enqueue($candidateId)
            $result.Add([pscustomobject]@{
                processId = $candidateId
                parentProcessId = [int]$candidate.ParentProcessId
                name = [string]$candidate.Name
                executablePath = [string]$candidate.ExecutablePath
                commandLine = [string]$candidate.CommandLine
            })
        }
    }

    return @($result)
}

function Resolve-AwarenessLabel {
    param([IntPtr]$Context)

    $knownContexts = [ordered]@{
        PerMonitorV2 = [IntPtr](-4)
        PerMonitorV1 = [IntPtr](-3)
        SystemAware = [IntPtr](-2)
        Unaware = [IntPtr](-1)
        UnawareGdiScaled = [IntPtr](-5)
    }
    foreach ($entry in $knownContexts.GetEnumerator()) {
        if ([ClearVisionStudioUiNext.NativeDpiProbe]::AreDpiAwarenessContextsEqual(
            $Context,
            [IntPtr]$entry.Value)) {
            return [string]$entry.Key
        }
    }
    return "Unknown"
}

$desktop = Resolve-DesktopProcess
$desktopProcessId = [int]$desktop.ProcessId
$window = [ClearVisionStudioUiNext.NativeDpiProbe]::FindWindow([uint32]$desktopProcessId)
if ($window -eq [IntPtr]::Zero) {
    throw "No top-level window was found for Desktop PID $desktopProcessId."
}

$windowRect = New-Object ClearVisionStudioUiNext.NativeDpiProbe+Rect
$clientRect = New-Object ClearVisionStudioUiNext.NativeDpiProbe+Rect
if (-not [ClearVisionStudioUiNext.NativeDpiProbe]::GetWindowRect($window, [ref]$windowRect)) {
    throw "GetWindowRect failed for Desktop PID $desktopProcessId."
}
if (-not [ClearVisionStudioUiNext.NativeDpiProbe]::GetClientRect($window, [ref]$clientRect)) {
    throw "GetClientRect failed for Desktop PID $desktopProcessId."
}

$context = [ClearVisionStudioUiNext.NativeDpiProbe]::GetWindowDpiAwarenessContext($window)
$awarenessLabel = Resolve-AwarenessLabel -Context $context
$coarseAwareness = $null
$processHandle = [ClearVisionStudioUiNext.NativeDpiProbe]::OpenProcess(0x1000, $false, [uint32]$desktopProcessId)
if ($processHandle -ne [IntPtr]::Zero) {
    try {
        $coarseValue = 0
        $resultCode = [ClearVisionStudioUiNext.NativeDpiProbe]::GetProcessDpiAwareness(
            $processHandle,
            [ref]$coarseValue)
        if ($resultCode -eq 0) {
            $coarseAwareness = @("Unaware", "SystemAware", "PerMonitorAware")[$coarseValue]
        }
    } finally {
        [ClearVisionStudioUiNext.NativeDpiProbe]::CloseHandle($processHandle) | Out-Null
    }
}

$descendants = @(Get-DescendantProcesses -RootProcessId $desktopProcessId)
$nodeDescendants = @($descendants | Where-Object {
    [string]$_.name -match '^node(?:\.exe)?$' -or
    [string]$_.executablePath -match '(?i)[\\/]node(?:\.exe)?$'
})
$managedProcess = Get-Process -Id $desktopProcessId -ErrorAction Stop
$dpi = [int][ClearVisionStudioUiNext.NativeDpiProbe]::GetDpiForWindow($window)

[pscustomobject]@{
    capturedAtUtc = [DateTime]::UtcNow.ToString("O")
    desktop = [pscustomobject]@{
        processId = $desktopProcessId
        parentProcessId = [int]$desktop.ParentProcessId
        name = [string]$desktop.Name
        executablePath = [string]$desktop.ExecutablePath
        commandLine = [string]$desktop.CommandLine
        workingSetBytes = [int64]$managedProcess.WorkingSet64
        privateMemoryBytes = [int64]$managedProcess.PrivateMemorySize64
        virtualMemoryBytes = [int64]$managedProcess.VirtualMemorySize64
        pagedMemoryBytes = [int64]$managedProcess.PagedMemorySize64
        handleCount = [int]$managedProcess.HandleCount
        threadCount = [int]$managedProcess.Threads.Count
    }
    nativeWindow = [pscustomobject]@{
        handle = $window.ToInt64()
        title = [ClearVisionStudioUiNext.NativeDpiProbe]::ReadTitle($window)
        windowRect = [pscustomobject]@{
            left = $windowRect.Left
            top = $windowRect.Top
            width = $windowRect.Right - $windowRect.Left
            height = $windowRect.Bottom - $windowRect.Top
        }
        clientSize = [pscustomobject]@{
            width = $clientRect.Right - $clientRect.Left
            height = $clientRect.Bottom - $clientRect.Top
        }
        dpi = $dpi
        scale = $dpi / 96.0
    }
    awareness = [pscustomobject]@{
        context = $context.ToInt64()
        label = $awarenessLabel
        awarenessClass = [ClearVisionStudioUiNext.NativeDpiProbe]::GetAwarenessFromDpiAwarenessContext($context)
        processAwareness = $coarseAwareness
        isPerMonitorV2 = $awarenessLabel -eq "PerMonitorV2"
    }
    descendants = $descendants
    nodeDescendantCount = $nodeDescendants.Count
} | ConvertTo-Json -Depth 8 -Compress
