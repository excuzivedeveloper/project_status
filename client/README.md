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
- first-run server address + computer name setup;
- shared status/device management in Settings;
- local window position, size and pin preference;
- no notifications, history, Kanban, accounts or task management.

The client automatically creates the configured local computer in the shared device list if it does not already exist.

## Technology

- C# / .NET 8
- Windows Forms
- built-in `HttpClient` and `System.Text.Json`
- no third-party NuGet packages

Local settings are stored under the current user's local application data directory and are never committed to Git.

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

An installer and GitHub Releases updater are intentionally deferred to a later PR.

## Privacy

Do not commit real server/Tailscale addresses, runtime settings, tokens, device-specific configuration or project data. The repository contains code and the application icon only.
