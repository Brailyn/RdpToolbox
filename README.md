# RDP Toolbox

A small Windows utility for launching multi-monitor RDP sessions without manually
clicking through the RDP connection prompt every time.

![RDP Toolbox](screenshot.png)

## Features

- Detects connected monitors and lets you pick which ones the remote session should
  span, with a click-to-select / Ctrl+click-to-add preview. Only monitors that are
  physically side-by-side (adjacent) can be selected together, matching what RDP
  multi-monitor spanning actually supports.
- When exactly one monitor is selected, choose a specific resolution (from a list of
  typical resolutions up to that monitor's native size) instead of spanning the full
  screen - handy for remote clients that can't scroll easily.
- Optional password field: stages the credential in Windows Credential Manager for
  the target server before launching `mstsc.exe`, then removes it again once the
  session closes, so mstsc can sign in without prompting for a password.
- Remembers previously used server addresses (excluding `127.0.0.1`) with a history
  picker that lets you reuse or delete individual entries.
- Remembers your monitor selection, clipboard preference, and auto-click options
  between runs (`settings.ini`).
- Writes a standard `.rdp` file and launches it via `mstsc.exe`.
- **Auto-click the connection prompt**: optionally drives the `mstsc` connection
  prompt with UI Automation so you don't have to click through it manually. Choose
  **All** to accept every checkbox on the prompt, or pick individual resources
  (**WebAuthn**, **Drives**, **Clipboard**, **Printers**) to both enable that
  resource for the session and pre-accept it on the prompt.
- A single **Open Data Folder** button opens the folder holding the settings, server
  history, and generated `.rdp` file, instead of listing each file path.

## Usage

1. Run `RdpToolbox.exe`
2. Enter the server address (or pick one from **Manage History**), username, and
   optionally a password
3. Click monitors in the preview to select which ones to span (Ctrl+click to add an
   adjacent monitor), or use **Select All** / **Clear All**
4. If a single monitor is selected, optionally pick a specific resolution instead of
   full screen
5. Toggle **Enable Clipboard redirection** and the auto-click options as needed
6. **Launch RDP**

Settings, server history, and the generated `.rdp` file are stored under
`%APPDATA%\RdpToolbox\`.

## Building from source

The project targets .NET Framework 4.8 and builds with the MSBuild that ships
with Windows - no Visual Studio required (the .NET Framework 4.8 Developer Pack
is needed for the reference assemblies if you don't already have it):

```
& "C:\WINDOWS\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe" "src\RdpToolbox.csproj" /p:Configuration=Release
```

The built exe is written to `src\bin\Release\RdpToolbox.exe`.

## Credits

The connection-prompt auto-click approach is adapted from
[dbak91/RdpOneClick](https://github.com/dbak91/RdpOneClick), which uses the same
UI Automation technique to drive the `mstsc.exe` connection dialog.
