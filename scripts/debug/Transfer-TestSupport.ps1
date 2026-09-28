$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Desktop-TestSupport.ps1')
if (-not ('TunnelDeck.Verification.TransferNative' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;
namespace TunnelDeck.Verification {
    public static class TransferNative {
        private delegate bool Callback(IntPtr window, IntPtr parameter);
        [DllImport("user32.dll")] private static extern bool EnumWindows(Callback callback, IntPtr parameter);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder text, int count);
        public static IntPtr PickerFor(IntPtr owner) {
            IntPtr result = IntPtr.Zero;
            EnumWindows((window, _) => {
                if (!IsWindowVisible(window)) return true;
                var name = new StringBuilder(256); GetClassName(window, name, name.Capacity);
                if (name.ToString() != "#32770") return true;
                IntPtr parent = window;
                for (int depth = 0; depth < 8 && parent != IntPtr.Zero; depth++) {
                    parent = GetWindow(parent, 4);
                    if (parent == owner) { result = window; return false; }
                }
                return true;
            }, IntPtr.Zero);
            return result;
        }
    }
}
'@
}

function Transfer-Ui {
    $raw = & winapp ui @args -a $appProcess.Id --json
    $code = $LASTEXITCODE
    $result = $raw | ConvertFrom-Json
    if ($code -ne 0 -and -not ($args[0] -eq 'search' -and $result.matchCount -eq 0)) { throw "Transfer UI command failed [$($args -join ' ')]: $raw" }
    return $result
}
function Transfer-Wait([string] $selector) { $null = Transfer-Ui wait-for $selector --timeout 15000 }
function Transfer-Invoke([string] $selector) {
    $null = Transfer-Ui wait-for $selector --property IsEnabled --value True --timeout 5000
    $null = Transfer-Ui invoke $selector
}
function Transfer-Value([string] $selector) { (Transfer-Ui get-value $selector).text }
function Transfer-Enabled([string] $selector) { (Transfer-Ui get-property $selector --property IsEnabled).properties.IsEnabled -eq 'True' }
function Transfer-Capture([string] $name) {
    Start-Sleep -Milliseconds 200
    $null = Transfer-Ui screenshot --output (Join-Path $outputPath ($name + '.png'))
}
function Transfer-Visible([string] $selector) {
    $element = (Transfer-Ui search $selector).matches | Where-Object automationId -eq $selector | Select-Object -First 1
    if (-not $element -or $element.isOffscreen -or $element.width -le 0 -or $element.height -le 0) { throw "Control is not visible: $selector" }
    return $element
}
function Transfer-Picker {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(15)
    do {
        $picker = [TunnelDeck.Verification.TransferNative]::PickerFor($appProcess.MainWindowHandle)
        if ($picker -ne [IntPtr]::Zero) { return $picker }
        Start-Sleep -Milliseconds 50
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw 'The owned system file picker did not appear.'
}
function Transfer-PickPath([string] $path) {
    $picker = Transfer-Picker
    # Storage pickers run in a broker process. Target the verified owned HWND, not the app PID.
    $fileNameSelector = $null
    foreach ($id in @('1148', '1001')) {
        $found = & winapp ui search $id -w $picker --json | ConvertFrom-Json
        $edit = @($found.matches | Where-Object { $_.type -in @('Edit', 'TextBox') -and $_.isEnabled -and -not $_.isOffscreen })
        if ($edit.Count -eq 1) { $fileNameSelector = $edit[0].selector; break }
    }
    if (-not $fileNameSelector) { throw 'The owned picker has no uniquely identified file-name editor.' }
    # Common Open and Save dialogs use different IDs; 1001 can also identify the address toolbar.
    $null = & winapp ui focus $fileNameSelector -w $picker --json
    $null = & winapp ui set-value $fileNameSelector $path -w $picker --json
    if ($LASTEXITCODE -ne 0) { throw 'The file name could not be entered.' }
    # UIA SetValue alone does not update the shell dialog's internal path. A real edit commits it.
    [TunnelDeck.Verification.ForwardNative]::Press($picker, 0x23)
    [TunnelDeck.Verification.ForwardNative]::Press($picker, 0x20)
    [TunnelDeck.Verification.ForwardNative]::Press($picker, 0x08)
    [TunnelDeck.Verification.ForwardNative]::Press($picker, 0x0d)
    Transfer-WaitPickerClosed
}
function Transfer-CancelPicker {
    $picker = Transfer-Picker
    # Shell folder rows also use numeric AutomationIds (including 2). Match the
    # owned dialog's actual button instead of accidentally opening the second folder.
    $found = & winapp ui search 2 -w $picker --json | ConvertFrom-Json
    $cancel = @($found.matches | Where-Object { $_.automationId -eq '2' -and $_.type -eq 'Button' -and $_.isEnabled -and -not $_.isOffscreen })
    if ($cancel.Count -ne 1) { throw 'The owned file picker has no unique Cancel button.' }
    $null = & winapp ui invoke $cancel[0].selector -w $picker --json
    if ($LASTEXITCODE -ne 0) { throw 'The file picker could not be cancelled.' }
    Transfer-WaitPickerClosed
}
function Transfer-WaitPickerClosed {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(15)
    while ([TunnelDeck.Verification.TransferNative]::PickerFor($appProcess.MainWindowHandle) -ne [IntPtr]::Zero) {
        if ([DateTimeOffset]::UtcNow -ge $deadline) { throw 'The file picker stayed open after the action.' }
        Start-Sleep -Milliseconds 50
    }
}
function Transfer-ReadState {
    for ($attempt = 0; $attempt -lt 25; $attempt++) {
        try {
            $stream = [IO.FileStream]::new($statePath, [IO.FileMode]::Open, [IO.FileAccess]::Read, ([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
            $reader = [IO.StreamReader]::new($stream)
            try { return $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
        }
        catch [IO.IOException] { Start-Sleep -Milliseconds 20 }
    }
    throw 'The transfer test configuration remained unavailable.'
}
function Transfer-Preview([string] $path, [switch] $Ssh) {
    Transfer-Invoke AllServers
    Transfer-Wait ServersPage
    Transfer-Invoke MoreButton
    Transfer-Invoke ImportResources
    Transfer-Wait ImportPage
    if ($Ssh) { Transfer-Invoke ImportFormat; Transfer-Invoke ImportSshFormat }
    $null = Transfer-Ui set-value ImportFilePath $path
    $null = Transfer-Ui wait-for ImportProgress --gone --timeout 15000
}
function Transfer-Close {
    if ($appProcess -and -not $appProcess.HasExited) {
        $picker = [TunnelDeck.Verification.TransferNative]::PickerFor($appProcess.MainWindowHandle)
        if ($picker -ne [IntPtr]::Zero) { Transfer-CancelPicker }
        $appProcess.Refresh()
        $closeSent = $appProcess.CloseMainWindow()
        for ($attempt = 0; -not $closeSent -and -not $appProcess.HasExited -and $attempt -lt 20; $attempt++) {
            Start-Sleep -Milliseconds 50
            $appProcess.Refresh()
            $closeSent = $appProcess.CloseMainWindow()
        }
        if (-not $appProcess.WaitForExit(15000) -or $appProcess.ExitCode -ne 0) { throw 'The transfer test instance did not close normally.' }
    }
}
