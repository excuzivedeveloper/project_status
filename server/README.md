# Sync Server

This directory will contain the self-hosted synchronization service for Project Status.

## MVP responsibilities

The server should do only four things:

1. Store the current project rows.
2. Store shared statuses and their colors.
3. Store the shared device list.
4. Serve a small HTTP API used by the Windows clients.

SQLite is sufficient for the expected workload. The service will run in Docker on a small Ubuntu server.

## Network model

The intended deployment is private access through Tailscale or an equivalent private network. The MVP does not require a public internet-facing API or a full user-account system.

## Conflict behavior

Updates use a simple last-write-wins rule. Clients refresh approximately every 5 seconds.

## Privacy

Runtime databases, real host addresses, credentials, and local deployment configuration must never be committed to the public repository.
