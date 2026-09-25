from __future__ import annotations

import sqlite3
from contextlib import contextmanager
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Iterator


class NotFoundError(Exception):
    pass


class ConflictError(Exception):
    pass


class InUseError(Exception):
    pass


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat(timespec="microseconds")


class Storage:
    def __init__(self, db_path: str):
        self.db_path = db_path
        Path(db_path).parent.mkdir(parents=True, exist_ok=True)
        self.init_schema()

    @contextmanager
    def connect(self) -> Iterator[sqlite3.Connection]:
        conn = sqlite3.connect(self.db_path, timeout=5)
        conn.row_factory = sqlite3.Row
        conn.execute("PRAGMA foreign_keys = ON")
        conn.execute("PRAGMA busy_timeout = 5000")
        try:
            yield conn
            conn.commit()
        except Exception:
            conn.rollback()
            raise
        finally:
            conn.close()

    def init_schema(self) -> None:
        with self.connect() as conn:
            conn.execute("PRAGMA journal_mode = WAL")
            conn.executescript(
                """
                CREATE TABLE IF NOT EXISTS statuses (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT NOT NULL COLLATE NOCASE UNIQUE,
                    color TEXT NOT NULL,
                    created_at TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS devices (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT NOT NULL COLLATE NOCASE UNIQUE,
                    created_at TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS projects (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT NOT NULL COLLATE NOCASE UNIQUE,
                    status_id INTEGER NULL REFERENCES statuses(id) ON DELETE RESTRICT,
                    device_id INTEGER NULL REFERENCES devices(id) ON DELETE RESTRICT,
                    note TEXT NOT NULL DEFAULT '',
                    is_hidden INTEGER NOT NULL DEFAULT 0,
                    updated_at TEXT NOT NULL
                );
                """
            )
            self._ensure_project_hidden_column(conn)

    @staticmethod
    def _row(row: sqlite3.Row | None) -> dict[str, Any] | None:
        return dict(row) if row is not None else None

    @staticmethod
    def _project_row(row: sqlite3.Row | None) -> dict[str, Any] | None:
        if row is None:
            return None
        data = dict(row)
        # SQLite has no native bool: the column stores 0/1, the API exposes true/false.
        # Missing key (pre-migration select) means visible.
        data["is_hidden"] = bool(data.get("is_hidden", 0))
        return data

    def _ensure_project_hidden_column(self, conn: sqlite3.Connection) -> None:
        # Migration for databases created before is_hidden existed: existing
        # projects stay visible (default 0).
        columns = {row["name"] for row in conn.execute("PRAGMA table_info(projects)")}
        if "is_hidden" not in columns:
            conn.execute(
                "ALTER TABLE projects ADD COLUMN is_hidden INTEGER NOT NULL DEFAULT 0"
            )

    def state(self) -> dict[str, list[dict[str, Any]]]:
        with self.connect() as conn:
            self._ensure_project_hidden_column(conn)
            statuses = [dict(row) for row in conn.execute(
                "SELECT id, name, color FROM statuses ORDER BY name COLLATE NOCASE"
            )]
            devices = [dict(row) for row in conn.execute(
                "SELECT id, name FROM devices ORDER BY name COLLATE NOCASE"
            )]
            projects = [dict(row) for row in conn.execute(
                """
                SELECT p.id, p.name, p.status_id, s.name AS status_name, s.color AS status_color,
                       p.device_id, d.name AS device_name, p.note, p.is_hidden, p.updated_at
                FROM projects p
                LEFT JOIN statuses s ON s.id = p.status_id
                LEFT JOIN devices d ON d.id = p.device_id
                ORDER BY p.name COLLATE NOCASE
                """
            )]
            for project in projects:
                project["is_hidden"] = bool(project.get("is_hidden", 0))
        return {"projects": projects, "statuses": statuses, "devices": devices}

    def create_status(self, name: str, color: str) -> dict[str, Any]:
        try:
            with self.connect() as conn:
                cur = conn.execute(
                    "INSERT INTO statuses(name, color, created_at) VALUES (?, ?, ?)",
                    (name, color, utc_now()),
                )
                row = conn.execute(
                    "SELECT id, name, color FROM statuses WHERE id = ?", (cur.lastrowid,)
                ).fetchone()
        except sqlite3.IntegrityError as exc:
            raise ConflictError("Status name already exists") from exc
        return self._row(row) or {}

    def update_status(self, status_id: int, *, name: str, color: str) -> dict[str, Any]:
        try:
            with self.connect() as conn:
                cur = conn.execute(
                    "UPDATE statuses SET name = ?, color = ? WHERE id = ?",
                    (name, color, status_id),
                )
                if cur.rowcount == 0:
                    raise NotFoundError("Status not found")
                row = conn.execute(
                    "SELECT id, name, color FROM statuses WHERE id = ?", (status_id,)
                ).fetchone()
        except sqlite3.IntegrityError as exc:
            raise ConflictError("Status name already exists") from exc
        return self._row(row) or {}

    def delete_status(self, status_id: int) -> None:
        with self.connect() as conn:
            if conn.execute(
                "SELECT 1 FROM projects WHERE status_id = ? LIMIT 1", (status_id,)
            ).fetchone():
                raise InUseError("Status is used by a project")
            cur = conn.execute("DELETE FROM statuses WHERE id = ?", (status_id,))
            if cur.rowcount == 0:
                raise NotFoundError("Status not found")

    def create_device(self, name: str) -> dict[str, Any]:
        try:
            with self.connect() as conn:
                cur = conn.execute(
                    "INSERT INTO devices(name, created_at) VALUES (?, ?)",
                    (name, utc_now()),
                )
                row = conn.execute(
                    "SELECT id, name FROM devices WHERE id = ?", (cur.lastrowid,)
                ).fetchone()
        except sqlite3.IntegrityError as exc:
            raise ConflictError("Device name already exists") from exc
        return self._row(row) or {}

    def update_device(self, device_id: int, *, name: str) -> dict[str, Any]:
        try:
            with self.connect() as conn:
                cur = conn.execute(
                    "UPDATE devices SET name = ? WHERE id = ?", (name, device_id)
                )
                if cur.rowcount == 0:
                    raise NotFoundError("Device not found")
                row = conn.execute(
                    "SELECT id, name FROM devices WHERE id = ?", (device_id,)
                ).fetchone()
        except sqlite3.IntegrityError as exc:
            raise ConflictError("Device name already exists") from exc
        return self._row(row) or {}

    def delete_device(self, device_id: int) -> None:
        with self.connect() as conn:
            if conn.execute(
                "SELECT 1 FROM projects WHERE device_id = ? LIMIT 1", (device_id,)
            ).fetchone():
                raise InUseError("Device is used by a project")
            cur = conn.execute("DELETE FROM devices WHERE id = ?", (device_id,))
            if cur.rowcount == 0:
                raise NotFoundError("Device not found")

    def _validate_fk(self, conn: sqlite3.Connection, table: str, item_id: int | None, label: str) -> None:
        if item_id is None:
            return
        if conn.execute(f"SELECT 1 FROM {table} WHERE id = ?", (item_id,)).fetchone() is None:
            raise NotFoundError(f"{label} not found")

    def create_project(
        self,
        name: str,
        *,
        status_id: int | None = None,
        device_id: int | None = None,
        note: str = "",
        is_hidden: bool = False,
    ) -> dict[str, Any]:
        try:
            with self.connect() as conn:
                self._ensure_project_hidden_column(conn)
                self._validate_fk(conn, "statuses", status_id, "Status")
                self._validate_fk(conn, "devices", device_id, "Device")
                cur = conn.execute(
                    """
                    INSERT INTO projects(name, status_id, device_id, note, is_hidden, updated_at)
                    VALUES (?, ?, ?, ?, ?, ?)
                    """,
                    (name, status_id, device_id, note, 1 if is_hidden else 0, utc_now()),
                )
                project_id = int(cur.lastrowid)
        except sqlite3.IntegrityError as exc:
            raise ConflictError("Project name already exists") from exc
        return self.get_project(project_id)

    def get_project(self, project_id: int) -> dict[str, Any]:
        with self.connect() as conn:
            self._ensure_project_hidden_column(conn)
            row = conn.execute(
                """
                SELECT p.id, p.name, p.status_id, s.name AS status_name, s.color AS status_color,
                       p.device_id, d.name AS device_name, p.note, p.is_hidden, p.updated_at
                FROM projects p
                LEFT JOIN statuses s ON s.id = p.status_id
                LEFT JOIN devices d ON d.id = p.device_id
                WHERE p.id = ?
                """,
                (project_id,),
            ).fetchone()
        result = self._project_row(row)
        if result is None:
            raise NotFoundError("Project not found")
        return result

    def update_project(
        self,
        project_id: int,
        *,
        name: str,
        status_id: int | None,
        device_id: int | None,
        note: str,
        is_hidden: bool | None = None,
    ) -> dict[str, Any]:
        try:
            with self.connect() as conn:
                self._ensure_project_hidden_column(conn)
                self._validate_fk(conn, "statuses", status_id, "Status")
                self._validate_fk(conn, "devices", device_id, "Device")
                if is_hidden is None:
                    # Old clients send name/status/device/note only. The flag is
                    # left out of SET entirely so the write is atomic: a concurrent
                    # /hide or /unhide between read and write cannot be overwritten
                    # by a stale value.
                    cur = conn.execute(
                        """
                        UPDATE projects
                        SET name = ?, status_id = ?, device_id = ?, note = ?, updated_at = ?
                        WHERE id = ?
                        """,
                        (name, status_id, device_id, note, utc_now(), project_id),
                    )
                else:
                    cur = conn.execute(
                        """
                        UPDATE projects
                        SET name = ?, status_id = ?, device_id = ?, note = ?, is_hidden = ?, updated_at = ?
                        WHERE id = ?
                        """,
                        (
                            name,
                            status_id,
                            device_id,
                            note,
                            1 if is_hidden else 0,
                            utc_now(),
                            project_id,
                        ),
                    )
                if cur.rowcount == 0:
                    raise NotFoundError("Project not found")
        except sqlite3.IntegrityError as exc:
            raise ConflictError("Project name already exists") from exc
        return self.get_project(project_id)

    def set_project_hidden(self, project_id: int, is_hidden: bool) -> dict[str, Any]:
        # Hide is not delete: only the flag flips, name/status/device/note stay.
        with self.connect() as conn:
            self._ensure_project_hidden_column(conn)
            cur = conn.execute(
                "UPDATE projects SET is_hidden = ?, updated_at = ? WHERE id = ?",
                (1 if is_hidden else 0, utc_now(), project_id),
            )
            if cur.rowcount == 0:
                raise NotFoundError("Project not found")
        return self.get_project(project_id)

    def delete_project(self, project_id: int) -> None:
        with self.connect() as conn:
            cur = conn.execute("DELETE FROM projects WHERE id = ?", (project_id,))
            if cur.rowcount == 0:
                raise NotFoundError("Project not found")
