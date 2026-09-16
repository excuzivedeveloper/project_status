# Windows Client

This directory will contain the small Windows desktop client for Project Status.

## Planned shape

Keep the client native and minimal:

- Windows desktop application
- compact vertical list
- system tray integration
- optional always-on-top window
- autostart with Windows
- local settings for server address and window state
- HTTP polling every 5 seconds

A native C# Windows client is the preferred direction because the required tray, window, startup, and installer behavior are all standard Windows features and do not require a browser shell.

## Privacy rule

Do not commit real server addresses, device-specific settings, tokens, or deployment data to this directory.
