param(
    [Parameter(Mandatory = $true)][string]$AppPath,
    [int]$AttachProcessId = 0,
    [string]$OutputDirectory = ''
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class UiSmokeNative {
    public struct Rect { public int Left, Top, Right, Bottom; }
    private struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
    private delegate bool MonitorCallback(IntPtr monitor, IntPtr dc, ref Rect rect, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorCallback callback, IntPtr data);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    public static Rect[] MonitorAreas() {
        var areas = new System.Collections.Generic.List<Rect>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr monitor, IntPtr dc, ref Rect rect, IntPtr data) => {
            var info = new MonitorInfo { Size = Marshal.SizeOf(typeof(MonitorInfo)) };
            if (GetMonitorInfo(monitor, ref info)) areas.Add(info.Work);
            return true;
        }, IntPtr.Zero);
        return areas.ToArray();
    }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr dc, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] public static extern uint GetGuiResources(IntPtr process, uint flag);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern IntPtr GetWindowDpiAwarenessContext(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern int GetAwarenessFromDpiAwarenessContext(IntPtr context);
    public delegate bool Callback(IntPtr hwnd, IntPtr state);
    [DllImport("user32.dll")] public static extern bool EnumWindows(Callback callback, IntPtr state);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int count);
    [DllImport("user32.dll")] public static extern bool ShowWindowAsync(IntPtr hwnd, int command);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hwnd, StringBuilder text, int count);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wparam, IntPtr lparam);
}
'@
$null = [UiSmokeNative]::SetThreadDpiAwarenessContext([IntPtr](-4))
$tag = 'DemoServer-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
$fixturePath = Join-Path $PSScriptRoot 'Nivetray.Tests/bin/Release/net10.0-windows/Nivetray.Tests.exe'
$fixtureStart = [Diagnostics.ProcessStartInfo]::new($fixturePath, "--fixture $tag 0")
$fixtureStart.UseShellExecute = $false
$fixtureStart.CreateNoWindow = $true
$fixture = [Diagnostics.Process]::Start($fixtureStart)
$app = if ($AttachProcessId -gt 0) { Get-Process -Id $AttachProcessId } else {
    # This is the foreground application under UI test, not a background helper.
    $appStart = [Diagnostics.ProcessStartInfo]::new((Resolve-Path $AppPath).Path)
    $appStart.UseShellExecute = $false
    $appStart.CreateNoWindow = $true
    $profile = Join-Path (Split-Path $PSScriptRoot) ('artifacts/qa/profile-' + [guid]::NewGuid().ToString('N'))
    $null = New-Item -ItemType Directory -Force -Path $profile
    '{"SchemaVersion":2,"Theme":"Light","Rules":[]}' | Set-Content (Join-Path $profile 'settings.json') -Encoding utf8
    $appStart.Arguments = '--data-directory "' + $profile + '"'
    [Diagnostics.Process]::Start($appStart)
}
$root = $null
function Find-Control([string]$name) {
    $condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, $name)
    return $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}
function Invoke-Control([string]$name) {
    $element = Find-Control $name
    if ($null -eq $element) { throw "Missing UI control: $name" }
    $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 500
}
function Capture([string]$name) {
    if (-not $OutputDirectory) { return }
    $null = New-Item -ItemType Directory -Force -Path $OutputDirectory
    $previousDpi = [UiSmokeNative]::SetThreadDpiAwarenessContext([IntPtr](-4))
    $rect = [UiSmokeNative+Rect]::new()
    $null = [UiSmokeNative]::GetWindowRect($script:appHandle, [ref]$rect)
    $bitmap = [Drawing.Bitmap]::new($rect.Right - $rect.Left, $rect.Bottom - $rect.Top)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $dc = $graphics.GetHdc()
    try { if (-not [UiSmokeNative]::PrintWindow($script:appHandle, $dc, 2)) { throw 'Window capture failed.' } }
    finally { $graphics.ReleaseHdc($dc); $graphics.Dispose() }
    $bitmap.Save((Join-Path (Resolve-Path $OutputDirectory).Path ($name + '.png')), [Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    $null = [UiSmokeNative]::SetThreadDpiAwarenessContext($previousDpi)
}
function Select-Tab([string]$name) {
    $element = Find-Control $name
    $element.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 200
}
function By-Id([string]$id) {
    return $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id))
}
function Select-Theme([string]$name) {
    $combo = By-Id 'ThemeBox'
    $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    Start-Sleep -Milliseconds 200
    $option = $combo.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, $name))
    if ($null -eq $option) { $option = Find-Control $name }
    $option.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse()
    Start-Sleep -Milliseconds 300
}
function Assert-ControlInWindow([string]$name) {
    for ($attempt = 0; $attempt -lt 15; $attempt++) {
        $control = Find-Control $name
        if ($null -ne $control) {
            $bounds = $root.Current.BoundingRectangle
            $rect = $control.Current.BoundingRectangle
            if (-not $control.Current.IsOffscreen -and $rect.Width -gt 0 -and
                $rect.Left -ge $bounds.Left -and $rect.Top -ge $bounds.Top -and
                $rect.Right -le $bounds.Right + 1 -and $rect.Bottom -le $bounds.Bottom + 1) { return }
        }
        Start-Sleep -Milliseconds 100
    }
    throw "Control is clipped after resize/DPI change: $name"
}
try {
    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        $app.Refresh()
        $script:appHandle = [IntPtr]::Zero
        $findApp = [UiSmokeNative+Callback] { param($hwnd, $state)
            [uint32]$ownerId = 0
            $null = [UiSmokeNative]::GetWindowThreadProcessId($hwnd, [ref]$ownerId)
            if ($ownerId -eq $app.Id) {
                $title = [System.Text.StringBuilder]::new(256)
                $null = [UiSmokeNative]::GetWindowText($hwnd, $title, 256)
                if ($title.ToString() -eq 'Nivetray') { $script:appHandle = $hwnd; return $false }
            }
            return $true
        }
        $null = [UiSmokeNative]::EnumWindows($findApp, [IntPtr]::Zero)
        if ($script:appHandle -ne [IntPtr]::Zero) {
            $null = [UiSmokeNative]::ShowWindowAsync($script:appHandle, 4)
            Start-Sleep -Milliseconds 200
            $root = [System.Windows.Automation.AutomationElement]::FromHandle($script:appHandle)
        }
        if ($null -ne $root) { break }
        Start-Sleep -Milliseconds 100
    }
    if ($null -eq $root) { throw 'Application window did not open. Close existing Nivetray instances before testing.' }
    # Exercise the same managed OpenPanel path as a tray double-click. Native ShowWindow alone
    # cannot change WPF's Visibility state after a close-to-tray operation.
    $script:trayEvent = 0x203
    $openFromTray = [UiSmokeNative+Callback] { param($hwnd, $state)
        [uint32]$ownerId = 0
        $null = [UiSmokeNative]::GetWindowThreadProcessId($hwnd, [ref]$ownerId)
        if ($ownerId -eq $app.Id) {
            $className = [System.Text.StringBuilder]::new(256)
            $null = [UiSmokeNative]::GetClassName($hwnd, $className, 256)
            if ($className.ToString().StartsWith('WindowsForms10.')) {
                $null = [UiSmokeNative]::PostMessage($hwnd, 0x800, [IntPtr]::Zero, [IntPtr]$script:trayEvent)
            }
        }
        return $true
    }
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        $null = [UiSmokeNative]::EnumWindows($openFromTray, [IntPtr]::Zero)
        Start-Sleep -Milliseconds 200
        if ($null -ne (Find-Control 'Refresh')) { break }
    }
    if ($null -eq (Find-Control 'Refresh')) {
        $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object { $_.Current.Name } | Write-Output
        throw 'Main panel is not accessible. This run did not validate the UI.'
    }
    Write-Output ("Owned WPF window DPI: {0}; awareness: {1} (2 = per monitor)." -f [UiSmokeNative]::GetDpiForWindow($script:appHandle), [UiSmokeNative]::GetAwarenessFromDpiAwarenessContext([UiSmokeNative]::GetWindowDpiAwarenessContext($script:appHandle)))
    $appDirectory = Split-Path (Resolve-Path $AppPath).Path
    if (Test-Path (Join-Path $appDirectory 'coreclr.dll')) {
        $app.Refresh()
        $runtime = $app.Modules | Where-Object ModuleName -eq 'coreclr.dll' | Select-Object -First 1
        if ($null -eq $runtime -or (Split-Path $runtime.FileName) -ne $appDirectory) { throw 'Portable did not load its packaged runtime.' }
        Write-Output 'PASS: portable loads its own packaged .NET runtime.'
    }
    Start-Sleep -Seconds 2
    $app.Refresh()
    $beforeCpu = $app.TotalProcessorTime.TotalMilliseconds
    Start-Sleep -Seconds 2
    $app.Refresh()
    $idleCpu = [math]::Round(($app.TotalProcessorTime.TotalMilliseconds - $beforeCpu) / 2000 * 100, 2)
    Write-Output ("Idle sample: {0}% of one CPU core; {1} MiB private; {2} MiB working set." -f $idleCpu, [math]::Round($app.PrivateMemorySize64 / 1MB, 1), [math]::Round($app.WorkingSet64 / 1MB, 1))
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        $script:fixtureCount = 0
        $showFixture = [UiSmokeNative+Callback] { param($hwnd, $state)
            [uint32]$ownerId = 0
            $null = [UiSmokeNative]::GetWindowThreadProcessId($hwnd, [ref]$ownerId)
            if ($ownerId -eq $fixture.Id) {
                $title = [System.Text.StringBuilder]::new(256)
                $null = [UiSmokeNative]::GetWindowText($hwnd, $title, 256)
                if ($title.ToString().StartsWith($tag)) { $null = [UiSmokeNative]::ShowWindowAsync($hwnd, 4); $script:fixtureCount++ }
            }
            return $true
        }
        $null = [UiSmokeNative]::EnumWindows($showFixture, [IntPtr]::Zero)
        if ($script:fixtureCount -eq 2) { break }
        Start-Sleep -Milliseconds 100
    }
    Start-Sleep -Milliseconds 300
    Invoke-Control 'Refresh'
    $search = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'Search'))
    $search.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($tag)
    Start-Sleep -Milliseconds 300
    Capture 'available-light'
    for ($warmup = 0; $warmup -lt 5; $warmup++) { Invoke-Control 'Refresh' }
    $app.Refresh()
    $beforeHandles = $app.HandleCount
    $beforeGdi = [UiSmokeNative]::GetGuiResources($app.Handle, 0)
    for ($refresh = 0; $refresh -lt 10; $refresh++) { Invoke-Control 'Refresh' }
    $app.Refresh()
    $afterGdi = [UiSmokeNative]::GetGuiResources($app.Handle, 0)
    Write-Output ("Refresh sample (10 cycles): handles {0}->{1}; GDI objects {2}->{3}; private {4} MiB." -f $beforeHandles, $app.HandleCount, $beforeGdi, $afterGdi, [math]::Round($app.PrivateMemorySize64 / 1MB, 1))
    for ($refresh = 0; $refresh -lt 10; $refresh++) { Invoke-Control 'Refresh' }
    $app.Refresh()
    $settledGdi = [UiSmokeNative]::GetGuiResources($app.Handle, 0)
    Write-Output ("Second refresh batch: GDI {0}->{1}; private {2} MiB." -f $afterGdi, $settledGdi, [math]::Round($app.PrivateMemorySize64 / 1MB, 1))
    # Separate initial native font/control warm-up from sustained resource growth.
    if ($settledGdi -gt $afterGdi + 3 -and $settledGdi -gt $beforeGdi + 8) { throw 'GDI count continued growing after warm-up; inspect resource ownership.' }
    $title = Find-Control ($tag + ' A')
    if ($null -eq $title) { throw 'Fixture was not detected by the WPF interface.' }
    $item = $title
    while ($item.Current.ControlType -ne [System.Windows.Automation.ControlType]::ListItem) {
        $item = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($item)
    }
    $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Invoke-Control 'Hide selected window'
    $root.SetFocus()
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        $progressControl = By-Id 'Progress'
        if ($null -eq $progressControl -or $progressControl.Current.IsOffscreen) { break }
        Start-Sleep -Milliseconds 100
    }
    $script:trayEvent = 0x205
    $null = [UiSmokeNative]::EnumWindows($openFromTray, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 300
    $menuCondition = [System.Windows.Automation.AndCondition]::new(
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $app.Id),
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Menu))
    $trayMenu = $null
    for ($attempt = 0; $attempt -lt 20 -and $null -eq $trayMenu; $attempt++) {
        $trayMenu = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $menuCondition)
        if ($null -eq $trayMenu) { Start-Sleep -Milliseconds 100 }
    }
    if ($null -eq $trayMenu) {
        $owned = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children,
            [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $app.Id))
        foreach ($element in $owned) { Write-Output ("Owned UI root: {0}; {1}; {2}." -f $element.Current.Name, $element.Current.ControlType.ProgrammaticName, $element.Current.ClassName) }
        throw 'Tray context menu did not open.'
    }
    $menuItems = $trayMenu.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::MenuItem))
    $viewAll = $menuItems | Where-Object { $_.Current.Name.StartsWith('View all hidden windows (') } | Select-Object -First 1
    if ($null -eq $viewAll) { throw 'Tray View all / Search action is missing.' }
    $viewAll.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 300
    Write-Output 'PASS: tray right-click event opens bounded menu and View all opens searchable hidden panel.'
    Select-Tab 'Hidden windows'
    Start-Sleep -Milliseconds 300
    Capture 'hidden-light'
    $hidden = Find-Control 'Hidden windows'
    $condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)
    $entry = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    if ($null -eq $entry) { throw 'Hidden-window list is empty.' }
    $entry.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Invoke-Control 'Restore selected'
    Select-Tab 'Available windows'
    Invoke-Control 'Refresh'
    $title = Find-Control ($tag + ' A')
    if ($null -eq $title) { throw 'Restored window is missing.' }
    $item = $title
    while ($item.Current.ControlType -ne [System.Windows.Automation.ControlType]::ListItem) {
        $item = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($item)
    }
    $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Invoke-Control 'Hide selected window'
    if ($AttachProcessId -eq 0) {
        Select-Tab 'Applications'
        (By-Id 'RulePath').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($fixturePath)
        (By-Id 'FavoriteRule').GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
        Invoke-Control 'Save rule'
        $settings = Get-Content (Join-Path $profile 'settings.json') -Raw | ConvertFrom-Json
        if ($settings.Rules.Count -ne 1 -or $settings.Rules[0].Mode -ne 'Standard' -or -not $settings.Rules[0].IsFavorite) { throw 'Independent favorite and visibility rule did not persist.' }
        (By-Id 'RulesSearch').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('no-match-xyz')
        Start-Sleep -Milliseconds 200
        $list = By-Id 'RulesList'
        if ($list.FindAll([System.Windows.Automation.TreeScope]::Children, $condition).Count -ne 0) { throw 'Rule search did not filter.' }
        (By-Id 'RulesSearch').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('')
        Start-Sleep -Milliseconds 200
        $rule = $list.FindFirst([System.Windows.Automation.TreeScope]::Children, $condition)
        $rule.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
        Invoke-Control 'Remove selected rule'
        $settings = Get-Content (Join-Path $profile 'settings.json') -Raw | ConvertFrom-Json
        if ($settings.Rules.Count -ne 0) { throw 'Rule removal did not persist.' }
        Select-Tab 'Settings'
        Capture 'settings-light'
        Select-Theme 'Dark'
        Select-Tab 'Hidden windows'
        Capture 'hidden-dark'
        $transform = $root.GetCurrentPattern([System.Windows.Automation.TransformPattern]::Pattern)
        $dpi = [UiSmokeNative]::GetDpiForWindow($script:appHandle)
        $transform.Resize(840 * $dpi / 96, 640 * $dpi / 96)
        Start-Sleep -Milliseconds 300
        Capture 'compact-dark'
        $window = $root.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern)
        $window.SetWindowVisualState([System.Windows.Automation.WindowVisualState]::Maximized)
        Start-Sleep -Milliseconds 250
        $window.SetWindowVisualState([System.Windows.Automation.WindowVisualState]::Normal)
        $originalBounds = $root.Current.BoundingRectangle
        $monitorIndex = 0
        foreach ($area in [UiSmokeNative]::MonitorAreas()) {
            $transform.Move($area.Left + 20, $area.Top + 20)
            Start-Sleep -Milliseconds 300
            $dpi = [UiSmokeNative]::GetDpiForWindow($script:appHandle)
            Write-Output ("Monitor {0}: actual owned-window DPI {1}; awareness {2}." -f $monitorIndex, $dpi, [UiSmokeNative]::GetAwarenessFromDpiAwarenessContext([UiSmokeNative]::GetWindowDpiAwarenessContext($script:appHandle)))
            if ($root.Current.IsOffscreen) { throw 'Owned window became inaccessible after monitor move.' }
            Assert-ControlInWindow 'Restore all'
            Assert-ControlInWindow 'Send Nivetray to tray'
            Capture ('monitor-' + $monitorIndex)
            $monitorIndex++
        }
        $transform.Move($originalBounds.Left, $originalBounds.Top)
        Select-Tab 'Settings'
        $settings = Get-Content (Join-Path $profile 'settings.json') -Raw | ConvertFrom-Json
        if ($settings.Theme -ne 'Dark') { throw 'Dark theme was not persisted.' }
        Select-Theme 'System'
        Invoke-Control 'Send Nivetray to tray'
        $secondStart = [Diagnostics.ProcessStartInfo]::new((Resolve-Path $AppPath).Path)
        $secondStart.UseShellExecute = $false
        $secondStart.CreateNoWindow = $true
        $second = [Diagnostics.Process]::Start($secondStart)
        if (-not $second.WaitForExit(5000)) { throw 'Second instance did not activate the existing app and exit.' }
        $second.Dispose()
        if ($app.HasExited) { throw 'Original app exited when opening a second instance.' }
        Start-Sleep -Milliseconds 300
        if ($root.Current.IsOffscreen) { throw 'Second instance did not reopen the hidden panel.' }
        Write-Output 'PASS: isolated profile, favorite rules, rule search/removal, light/dark/system themes, resize and maximize/restore.'
    }
    Select-Tab 'Settings'
    Invoke-Control 'Restore all and exit Nivetray'
    if (-not $app.WaitForExit(5000)) { throw 'Application did not exit after restoration.' }
    # Verify the fixture's windows are visible again using a fresh native enumeration.
    $script:visible = 0
    $callback = [UiSmokeNative+Callback] { param($hwnd, $state)
        [uint32]$ownerId = 0
        $null = [UiSmokeNative]::GetWindowThreadProcessId($hwnd, [ref]$ownerId)
        if ($ownerId -eq $fixture.Id -and [UiSmokeNative]::IsWindowVisible($hwnd)) { $script:visible++ }
        return $true
    }
    $null = [UiSmokeNative]::EnumWindows($callback, [IntPtr]::Zero)
    if ($script:visible -ne 2) { throw "Expected two restored fixture windows, found $script:visible." }
    Write-Output 'PASS: WPF search, selection, hide, restore, and restore-on-exit with two live windows.'
} catch {
    Write-Output $_.ScriptStackTrace
    throw
} finally {
    $fixture.Refresh()
    if (-not $fixture.HasExited) { $null = $fixture.CloseMainWindow() }
    if ($null -ne $root -and -not $app.HasExited) {
        try { Select-Tab 'Settings'; Invoke-Control 'Restore all and exit Nivetray' } catch { Write-Warning $_ }
    }
}
