# Windows Client

Native Windows client for Project Status.

## MVP behavior

The client stays deliberately small:

- compact table with one row per project;
- edit project name, status, device and one-line note directly;
- add and delete projects;
- polling every 5 seconds;
- `Sync: OK` / `No connection` indicator;
- system tray icon;
- double-click tray icon to open;
- tray menu: Open / Always on top / Settings / Exit;
- closing the window hides it to the tray;
- optional always-on-top mode;
- current-user Windows autostart;
- first-run HTTPS server address, API token, and computer name setup;
- shared status/device management in Settings;
- local window position, size and pin preference;
- one startup check for a newer GitHub Release;
- no project-change notifications, history, Kanban, accounts or task management.

The client automatically creates the configured local computer in the shared device list if it does not already exist.

## Technology

- C# / .NET 8
- Windows Forms
- built-in `HttpClient` and `System.Text.Json`
- no third-party NuGet packages

The server address is stored in `%LOCALAPPDATA%\ProjectStatus\settings.json`. The API token is protected with Windows DPAPI `CurrentUser` before it is saved there; plaintext is never written to the file. Existing settings load unchanged and can receive a token in Settings. A host entered without a scheme defaults to HTTPS; explicit HTTP remains available for local development. Tailscale is no longer required for client transport.

## Build

On a machine with the .NET 8 SDK:

```powershell
dotnet build .\client\ProjectStatus.Client\ProjectStatus.Client.csproj -c Release
```

## Publish a self-contained Windows executable

For 64-bit Windows:

```powershell
dotnet publish .\client\ProjectStatus.Client\ProjectStatus.Client.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true
```

## Installer

The Windows installer is defined in `client/installer/project_status.iss` and built with Inno Setup.
It is a per-user install under `%LOCALAPPDATA%\Programs\Project Status`, so installation does not require administrator privileges.
The installer creates a Start Menu shortcut and offers an optional desktop shortcut.

The normal pull-request build compiles the installer as a CI artifact.

## Releases and update check

Pushing a tag in the form `vMAJOR.MINOR.PATCH` runs `.github/workflows/windows-release.yml`.
The workflow publishes a self-contained `win-x64` client, builds `ProjectStatus-Setup-vMAJOR.MINOR.PATCH.exe`, writes a SHA-256 checksum file, and creates or updates the matching GitHub Release.

At application startup, the client performs one best-effort request to the public GitHub `releases/latest` endpoint. If a newer stable version exists, the tray menu shows `Update available v...`. Clicking it opens the installer asset in the default browser. There is no silent download, background installer, or automatic restart.

## Privacy

Do not commit real server/Tailscale addresses, runtime settings, tokens, device-specific configuration or project data. The repository contains code and the application icon only.
