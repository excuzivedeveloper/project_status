# Sync Server

This directory contains the self-hosted synchronization service for Project Status.

## MVP responsibilities

The server does only four things:

1. Store the current project rows.
2. Store shared statuses and their colors.
3. Store the shared device list.
4. Serve a small HTTP API used by the Windows clients.

SQLite is sufficient for the expected workload. The service runs in Docker on a small Ubuntu server.

## API

- `GET /health` — verifies the service can read SQLite.
- `GET /api/state` — returns projects, statuses, and devices in one lightweight snapshot.
- `POST|PUT|DELETE /api/projects...` — project CRUD.
- `POST|PUT|DELETE /api/statuses...` — status CRUD.
- `POST|PUT|DELETE /api/devices...` — device CRUD.

Project timestamps are server-generated UTC ISO 8601 values with microsecond precision so rapid accepted updates remain distinguishable.

Statuses and devices that are currently referenced by a project cannot be deleted. Assign another value (or clear the assignment) first.

## Run with Docker

From this directory:

```bash
docker compose up -d --build
```

By default, Compose publishes the API only on loopback:

```text
127.0.0.1:8080
```

This avoids accidentally exposing the MVP API on the public network. A future Tailscale setup can provide private remote access without changing the application itself.

Check it locally with:

```bash
curl http://127.0.0.1:8080/health
```

SQLite data is stored in the `project-status-data` Docker volume and survives container recreation.

## Configuration

`server/.env.example` documents the supported environment variables. Do not commit a real `.env` file.

- `PROJECT_STATUS_PORT` controls the host port.
- `PROJECT_STATUS_BIND_HOST` controls the host interface used by Compose. Keep the default `127.0.0.1` unless you intentionally understand the exposure.
- `PROJECT_STATUS_DB_PATH` controls the database path inside the container.

## Development checks

From `server/`:

```bash
python -m unittest discover -s tests -v
python -m py_compile app/storage.py app/main.py
```

## Network model

The intended deployment is private access through Tailscale or an equivalent private network. The MVP does not require a public internet-facing API or a full user-account system.

## Conflict behavior

Updates use a simple last-write-wins rule. Clients refresh approximately every 5 seconds.

## Privacy

Runtime databases, real host addresses, credentials, and local deployment configuration must never be committed to the public repository.
