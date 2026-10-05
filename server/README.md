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
All `/api/*` requests require `Authorization: Bearer <token>` matching `PROJECT_STATUS_API_TOKEN`. `/health` remains unauthenticated for local health checks.

## Run with Docker

```bash
cd server
cp .env.example .env
# Replace the example token with a cryptographically random secret of at least 32 bytes.
docker compose up -d --build
```

Keep `PROJECT_STATUS_BIND_ADDRESS=127.0.0.1` and set `PROJECT_STATUS_PORT=18080` when that is the host's allocated backend port. Terminate HTTPS at a reverse proxy and forward to `127.0.0.1:18080`; do not publish the backend directly. Never commit the real token or `.env`.

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
PROJECT_STATUS_DB_PATH=./project_status.db PROJECT_STATUS_API_TOKEN=local-development-secret uvicorn app.main:app --reload --port 8080
```

Storage tests use only the Python standard library:

```bash
cd server
python -m unittest discover -s tests -v
```

## Privacy

Never commit runtime databases, real server or Tailscale addresses, tokens, credentials, or personal project data. The repository contains code and safe example configuration only.
