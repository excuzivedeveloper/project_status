# Project Status — MVP Scope

## Purpose

Project Status answers one question quickly:

> What is the current state of each active project, and on which computer is it being handled?

The MVP is intentionally limited to roughly 3–5 active projects for a single user.

## Project row

A project row contains:

1. Project name
2. Current status
3. Selected device
4. One-line note
5. Last-updated date and time

Any change to status, device, or note updates the timestamp.

## Core behavior

- Projects are shown as a compact vertical list.
- Status and device can be changed directly from the row.
- Projects can be added, renamed, and deleted.
- Deleting a project requires confirmation.
- New projects start in a paused/default state, with no device selected and an empty note.
- The server stores only the latest state; no project history is required for MVP.
- Concurrent edits use last-write-wins behavior.
- Clients poll the server every 5 seconds.

## Configurable shared data

The server stores and synchronizes:

- project list
- statuses and status colors
- device list

Users can add, rename, and remove statuses and devices from Settings.

## Local-only client settings

Each Windows computer stores locally:

- server address
- identity/name of the current computer
- window position
- window size
- always-on-top preference

These settings are not committed to Git.

## Windows behavior

- Native Windows desktop application
- System tray icon
- Double-click tray icon: open/show main window
- Right-click tray menu:
  - Open
  - Always on top
  - Settings
  - Exit
- Closing the window hides it to the tray rather than exiting.
- Application starts with Windows and remains in the tray.
- Always-on-top can be enabled or disabled.
- No notifications in MVP.
- Single simple visual theme in MVP.

## First run

Minimal setup:

1. Enter server address.
2. Choose or create the name of this computer.
3. Start using the application.

## Synchronization health

The client shows a small connection state:

- Sync OK
- No connection

No advanced offline conflict resolution is required.

## Deployment

- Server: Ubuntu 24.04 compatible
- Docker deployment
- Private access recommended through Tailscale
- Direct public exposure of the API is not part of the MVP

## Explicitly out of scope

Do not add these to MVP:

- Kanban boards
- task/subtask management
- project history/timeline
- chat
- notifications
- user/team management
- permissions/roles
- attachments
- calendars
- analytics
- complex dashboards
- GitHub automation
- automatic project-state detection
- mobile app
- rich text notes
- multiple themes

The product should remain a tiny status board, not evolve into a general project-management system by default.
