# Claude Profile Switcher
# Runs several Claude Desktop instances side by side, each with its own data
# folder (and therefore its own signed-in account), via Electron's
# --user-data-dir switch. Profiles live in profiles.json next to this script.
# Removing a profile here never deletes its data folder.

Add-Type -AssemblyName System.Windows.Forms, System.Drawing, Microsoft.VisualBasic
[System.Windows.Forms.Application]::EnableVisualStyles()

$Here         = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProfilesFile = Join-Path $Here 'profiles.json'
$IconFile     = Join-Path $Here 'claude.ico'
$ClaudeAlias  = Join-Path $env:LOCALAPPDATA 'Microsoft\WindowsApps\claude-desktop.exe'
$DefaultDir   = Join-Path $env:APPDATA 'Claude'

# ---------- profiles ----------

function Load-Profiles {
    if (Test-Path $ProfilesFile) {
        # Assign first: in PowerShell 5.1, piping ConvertFrom-Json into @() nests the array.
        try { $json = Get-Content $ProfilesFile -Raw | ConvertFrom-Json; return @($json) } catch {}
    }
    # dataDir "" = Claude's default folder (what the normal Start menu entry uses)
    return @(
        [pscustomobject]@{ name = 'Main (default)'; dataDir = '' },
        [pscustomobject]@{ name = 'Personal';       dataDir = (Join-Path $env:APPDATA 'Claude-Personal') }
    )
}

function Save-Profiles {
    ConvertTo-Json -InputObject @($script:Profiles) -Depth 3 | Set-Content $ProfilesFile -Encoding UTF8
}

function Resolve-DataDir($p) { if ($p.dataDir) { $p.dataDir } else { $DefaultDir } }

# ---------- running-instance detection ----------

# Returns a set of lower-cased data dirs whose Claude Desktop main process is alive.
function Get-RunningDirs {
    $set = New-Object 'System.Collections.Generic.HashSet[string]'
    $procs = Get-CimInstance Win32_Process -Filter "Name='Claude.exe' OR Name='claude-desktop.exe'" -ErrorAction SilentlyContinue
    foreach ($pr in $procs) {
        $cmd = [string]$pr.CommandLine
        if (-not $cmd -or $cmd -match '--type=') { continue }                  # Chromium child process
        if ([string]$pr.ExecutablePath -notmatch '\\WindowsApps\\') { continue } # Claude Code CLI etc.
        if ($cmd -match '--user-data-dir=(?:"([^"]+)"|(\S+))') {
            $dir = if ($Matches[1]) { $Matches[1] } else { $Matches[2] }
        } else {
            $dir = $DefaultDir
        }
        [void]$set.Add($dir.TrimEnd('\').ToLowerInvariant())
    }
    return ,$set
}

# ---------- actions ----------

function Start-ClaudeProfile($p) {
    if (-not (Test-Path $ClaudeAlias)) {
        [System.Windows.Forms.MessageBox]::Show("Claude Desktop not found at`n$ClaudeAlias", 'Claude Profile Switcher', 'OK', 'Error') | Out-Null
        return
    }
    # Launching a profile that is already running just brings its window forward.
    if ($p.dataDir) {
        New-Item -ItemType Directory -Force $p.dataDir | Out-Null
        Start-Process $ClaudeAlias -ArgumentList "--user-data-dir=`"$($p.dataDir)`""
    } else {
        Start-Process $ClaudeAlias
    }
}

function New-ProfileShortcut($p) {
    $desktop = [Environment]::GetFolderPath('Desktop')
    $safe = ($p.name -replace '[\\/:*?"<>|]', '').Trim()
    $lnk = (New-Object -ComObject WScript.Shell).CreateShortcut((Join-Path $desktop "Claude ($safe).lnk"))
    $lnk.TargetPath = $ClaudeAlias
    if ($p.dataDir) { $lnk.Arguments = "--user-data-dir=`"$($p.dataDir)`"" }
    $lnk.WorkingDirectory = $env:USERPROFILE
    if (Test-Path $IconFile) { $lnk.IconLocation = $IconFile }
    $lnk.Save()
    [System.Windows.Forms.MessageBox]::Show("Shortcut created on your desktop:`nClaude ($safe)", 'Claude Profile Switcher') | Out-Null
}

# ---------- UI ----------

$script:Profiles = @(Load-Profiles)

$form = New-Object System.Windows.Forms.Form
$form.Text = 'Claude Profile Switcher'
$form.Size = New-Object System.Drawing.Size(620, 360)
$form.MinimumSize = New-Object System.Drawing.Size(520, 300)
$form.StartPosition = 'CenterScreen'
$form.Font = New-Object System.Drawing.Font('Segoe UI', 9.5)
if (Test-Path $IconFile) { $form.Icon = New-Object System.Drawing.Icon($IconFile) }

$list = New-Object System.Windows.Forms.ListView
$list.View = 'Details'
$list.FullRowSelect = $true
$list.MultiSelect = $false
$list.HideSelection = $false
$list.Location = New-Object System.Drawing.Point(12, 12)
$list.Size = New-Object System.Drawing.Size(450, 296)
$list.Anchor = 'Top, Bottom, Left, Right'
[void]$list.Columns.Add('Profile', 140)
[void]$list.Columns.Add('Status', 90)
[void]$list.Columns.Add('Data folder', 210)
$form.Controls.Add($list)

function Refresh-List {
    $running = Get-RunningDirs
    $sel = if ($list.SelectedIndices.Count) { $list.SelectedIndices[0] } else { 0 }
    $list.BeginUpdate()
    $list.Items.Clear()
    foreach ($p in $script:Profiles) {
        $dir = Resolve-DataDir $p
        $isOn = $running.Contains($dir.TrimEnd('\').ToLowerInvariant())
        $item = New-Object System.Windows.Forms.ListViewItem($p.name)
        [void]$item.SubItems.Add($(if ($isOn) { 'Running' } else { '' }))
        [void]$item.SubItems.Add($dir)
        if ($isOn) { $item.ForeColor = [System.Drawing.Color]::FromArgb(30, 120, 60) }
        [void]$list.Items.Add($item)
    }
    $list.EndUpdate()
    if ($list.Items.Count) {
        $list.Items[[Math]::Min($sel, $list.Items.Count - 1)].Selected = $true
    }
}

function Get-Selected {
    if ($list.SelectedIndices.Count) { return $script:Profiles[$list.SelectedIndices[0]] }
    return $null
}

$y = 12
function Add-Button($text, $onClick, [switch]$Primary) {
    $b = New-Object System.Windows.Forms.Button
    $b.Text = $text
    $b.Location = New-Object System.Drawing.Point(474, $script:y)
    $b.Size = New-Object System.Drawing.Size(118, $(if ($Primary) { 40 } else { 30 }))
    $b.Anchor = 'Top, Right'
    if ($Primary) { $b.Font = New-Object System.Drawing.Font('Segoe UI', 10, [System.Drawing.FontStyle]::Bold) }
    $b.Add_Click($onClick)
    $form.Controls.Add($b)
    $script:y += $b.Height + 6
    return $b
}

$btnOpen = Add-Button 'Open' { $p = Get-Selected; if ($p) { Start-ClaudeProfile $p; Start-Sleep -Milliseconds 800; Refresh-List } } -Primary
$form.AcceptButton = $btnOpen
$script:y += 8

[void](Add-Button 'Add profile...' {
    $name = [Microsoft.VisualBasic.Interaction]::InputBox('Name for the new profile (e.g. Work):', 'Add profile', 'Work')
    if (-not $name.Trim()) { return }
    $folder = 'Claude-' + (($name -replace '[^A-Za-z0-9_-]', '').Trim())
    if ($folder -eq 'Claude-') { $folder = 'Claude-Profile' + ($script:Profiles.Count + 1) }
    $dir = Join-Path $env:APPDATA $folder
    $script:Profiles = @($script:Profiles) + [pscustomobject]@{ name = $name.Trim(); dataDir = $dir }
    Save-Profiles; Refresh-List
    $list.Items[$list.Items.Count - 1].Selected = $true
})

[void](Add-Button 'Rename...' {
    $p = Get-Selected; if (-not $p) { return }
    $name = [Microsoft.VisualBasic.Interaction]::InputBox('New name:', 'Rename profile', $p.name)
    if ($name.Trim()) { $p.name = $name.Trim(); Save-Profiles; Refresh-List }
})

[void](Add-Button 'Remove' {
    $p = Get-Selected; if (-not $p) { return }
    if (-not $p.dataDir) { [System.Windows.Forms.MessageBox]::Show('The default profile can''t be removed.', 'Claude Profile Switcher') | Out-Null; return }
    $ok = [System.Windows.Forms.MessageBox]::Show("Remove '$($p.name)' from this list?`n`nIts data folder is kept:`n$($p.dataDir)", 'Remove profile', 'YesNo', 'Question')
    if ($ok -eq 'Yes') { $script:Profiles = @($script:Profiles | Where-Object { $_ -ne $p }); Save-Profiles; Refresh-List }
})

[void](Add-Button 'Open folder' {
    $p = Get-Selected; if (-not $p) { return }
    $dir = Resolve-DataDir $p
    if (Test-Path $dir) { Start-Process explorer.exe $dir } else { [System.Windows.Forms.MessageBox]::Show("Not created yet - open the profile once first.", 'Claude Profile Switcher') | Out-Null }
})

[void](Add-Button 'Desktop shortcut' { $p = Get-Selected; if ($p) { New-ProfileShortcut $p } })

$list.Add_DoubleClick({ $btnOpen.PerformClick() })

$timer = New-Object System.Windows.Forms.Timer
$timer.Interval = 3000
$timer.Add_Tick({ Refresh-List })
$form.Add_Shown({ Refresh-List; $timer.Start() })
$form.Add_FormClosed({ $timer.Stop() })

if (-not (Test-Path $ProfilesFile)) { Save-Profiles }
[void]$form.ShowDialog()
