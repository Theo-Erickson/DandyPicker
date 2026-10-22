# DandyPicker

Run Claude Desktop with more than one account at the same time — e.g. work and
personal side by side. Named for the Claude logo looking like a dandelion.

## How it works

Claude Desktop is an Electron app. Starting it with
`--user-data-dir="<folder>"` gives that instance its own data folder, which
means its own sign-in, chats and settings, and its own single-instance lock, so
it runs alongside the normal one. DandyPicker keeps a list of these profiles
and launches each with the right folder.

- **Open** — start a profile, or bring it to the front if it's already running.
- **Send sign-in link** — see below.
- **Add profile / Rename / Remove** — edit the list. Removing never deletes the
  profile's data folder.
- **Open folder** — the folder Claude really uses for that profile. Claude is a
  packaged (MSIX) app, so Windows may keep the files in the package's private
  copy of AppData rather than the path shown.
- **Desktop shortcut** — a one-click Claude shortcut for that profile.

Profiles are stored in `profiles.json` next to the .exe. An empty `dataDir`
means Claude's default folder.

## Signing a new profile in

Google sign-in finishes in your browser, which hands the result back with a
`claude://` link. Windows always sends those links to the default Claude
window, never to the profile that asked. So:

1. Open the new profile and start signing in from its window.
2. When the browser asks "Open Claude?", cancel.
3. Right-click the page's Open Claude button and copy the link.
4. In DandyPicker, select the profile and click **Send sign-in link**.

## Building

`src\build.cmd` compiles `DandyPicker.exe` with the C# compiler that ships with
Windows (.NET Framework 4.x) — nothing to install. Close DandyPicker first.

`dandelion.ico` is drawn by `tools\make-dandelion-icon.ps1`:

```powershell
.\tools\make-dandelion-icon.ps1 -OutIco dandelion.ico -OutPreview dandelion.png
```

`claude.ico` (used for per-profile desktop shortcuts) is extracted from the
installed Claude app and isn't tracked:

```powershell
Add-Type -AssemblyName System.Drawing
$exe = Join-Path (Get-AppxPackage Claude).InstallLocation 'app\Claude.exe'
$fs = [IO.File]::Create("$PWD\claude.ico"); [Drawing.Icon]::ExtractAssociatedIcon($exe).Save($fs); $fs.Close()
```

## History

The commits before "Set up git" were reconstructed from the chat the tool was
built in, dated to when each version was made. Each milestone is tagged:

| Tag | Version |
|---|---|
| `v0.1-powershell` | PowerShell + WinForms switcher |
| `v0.2-sign-in-link` | Send sign-in link |
| `v0.3-dandelion-icon` | Dandelion icon |
| `v1.0-exe` | Compiled C# app |
| `v1.1-dandypicker` | Renamed to DandyPicker |

To get any version as its own folder:

```powershell
git worktree add ..\DandyPicker-v0.1 v0.1-powershell
```
