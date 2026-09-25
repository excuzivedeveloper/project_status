# Project Status sync server

Small self-hosted HTTP API for Project Status. It stores only the latest shared state in SQLite.

## Scope

The server stores:

- projects: name, current status, current device, one-line note, hidden flag, updated timestamp;
- configurable statuses and colors;
- configurable devices.

There is no project history, user system, notifications, analytics, GitHub integration, or offline conflict engine. Project updates are last-write-wins.

## API

- `GET /health`
- `GET /api/state` — one snapshot containing projects, statuses and devices
- `POST|PUT|DELETE /api/projects`
- `POST /api/projects/{id}/hide` — set `is_hidden=true` (hide is not delete)
- `POST /api/projects/{id}/unhide` — set `is_hidden=false`
- `POST|PUT|DELETE /api/statuses`
- `POST|PUT|DELETE /api/devices`

A status or device cannot be deleted while a project references it. This keeps the shared state valid.

## Run with Docker

```bash
cd server
cp .env.example .env
docker compose up -d --build
```

The default bind address is `127.0.0.1`, so the API is not exposed to the network by default.
For private remote access, install Tailscale on the server and clients, then set `PROJECT_STATUS_BIND_ADDRESS` in the local `.env` to the server's Tailscale IP. Do not commit that `.env` file.

Check health locally:

```bash
curl http://127.0.0.1:8080/health
```

## Local development

Python 3.12 is the target runtime.

```bash
cd server
python -m venv .venv
# activate the venv, then:
pip install -r requirements.txt
PROJECT_STATUS_DB_PATH=./project_status.db uvicorn app.main:app --reload --port 8080
```

Storage tests use only the Python standard library:

```bash
cd server
python -m unittest discover -s tests -v
```

## Privacy

Never commit runtime databases, real server or Tailscale addresses, tokens, credentials, or personal project data. The repository contains code and safe example configuration only.
