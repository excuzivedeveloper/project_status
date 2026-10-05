# Project Status

A tiny self-hosted project status tracker for Windows.

The goal is intentionally small: keep a short list of active projects visible, show the current status and device for each project, and synchronize the latest state between Windows computers through a small server API.

## MVP

Each project has only:

- name
- status
- device
- one-line note
- last-updated timestamp

The Windows client will provide:

- compact vertical project list
- click-to-change status and device
- add, rename, and delete projects
- configurable statuses and devices
- system tray integration
- optional "always on top" mode
- autostart with Windows
- saved window position/size per computer
- sync status indicator
- 5-second polling sync

The server will provide:

- a very small API
- SQLite storage
- Docker deployment
- last-write-wins updates

## Intended deployment

The client connects through an HTTPS reverse proxy with a shared Bearer API token. The FastAPI backend should bind only to localhost. Tailscale is optional for administration, not required for client transport.

## Repository layout

```text
client/   Windows desktop client
server/   Self-hosted sync API
docs/     Small project scope and decisions
```

## Privacy

This public repository must not contain personal deployment data.

Do not commit:

- real server IP addresses or hostnames
- access tokens or API keys
- Tailscale addresses or credentials
- local application configuration
- real project/status data from a deployment
- generated databases

Use local configuration files and environment variables instead. Safe example files may contain placeholders only.

## Scope rule

Project Status is deliberately not a task manager, issue tracker, Kanban board, or project-management suite. New features should be added only when they directly support the small status-board workflow.

## Status

Windows client and sync server are implemented.
