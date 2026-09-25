import tempfile
import unittest
from datetime import datetime, timezone
from pathlib import Path
from unittest.mock import patch

from app.storage import ConflictError, InUseError, NotFoundError, Storage


class StorageTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.storage = Storage(str(Path(self.tmp.name) / "project_status.db"))

    def tearDown(self):
        self.tmp.cleanup()

    def test_project_state_round_trip(self):
        status = self.storage.create_status("Review", "#3366FF")
        device = self.storage.create_device("Laptop")
        project = self.storage.create_project(
            "Example", status_id=status["id"], device_id=device["id"], note="PR #1"
        )

        self.assertEqual(project["status_name"], "Review")
        self.assertEqual(project["device_name"], "Laptop")
        self.assertEqual(project["note"], "PR #1")
        self.assertTrue(project["updated_at"].endswith("+00:00"))

        state = self.storage.state()
        self.assertEqual(len(state["projects"]), 1)
        self.assertEqual(len(state["statuses"]), 1)
        self.assertEqual(len(state["devices"]), 1)

    def test_names_are_case_insensitive_unique(self):
        self.storage.create_status("Review", "#3366FF")
        with self.assertRaises(ConflictError):
            self.storage.create_status("review", "#123456")

    def test_device_and_project_names_are_case_insensitive_unique(self):
        self.storage.create_device("Laptop")
        with self.assertRaises(ConflictError):
            self.storage.create_device("laptop")

        self.storage.create_project("Example")
        with self.assertRaises(ConflictError):
            self.storage.create_project("example")

    def test_status_and_device_updates(self):
        status = self.storage.create_status("Review", "#3366FF")
        device = self.storage.create_device("Laptop")

        updated_status = self.storage.update_status(
            status["id"], name="Coder", color="#11AA11"
        )
        updated_device = self.storage.update_device(device["id"], name="Desktop")

        self.assertEqual(updated_status["name"], "Coder")
        self.assertEqual(updated_status["color"], "#11AA11")
        self.assertEqual(updated_device["name"], "Desktop")

    def test_used_status_and_device_cannot_be_deleted(self):
        status = self.storage.create_status("Review", "#3366FF")
        device = self.storage.create_device("Laptop")
        self.storage.create_project(
            "Example", status_id=status["id"], device_id=device["id"]
        )

        with self.assertRaises(InUseError):
            self.storage.delete_status(status["id"])
        with self.assertRaises(InUseError):
            self.storage.delete_device(device["id"])

    def test_update_project_uses_last_write_and_changes_timestamp(self):
        status_a = self.storage.create_status("Coder", "#11AA11")
        status_b = self.storage.create_status("Review", "#AA11AA")
        first_time = datetime(2026, 9, 16, 12, 0, 0, 100, tzinfo=timezone.utc)
        second_time = datetime(2026, 9, 16, 12, 0, 0, 200, tzinfo=timezone.utc)

        with patch("app.storage.datetime") as mocked_datetime:
            mocked_datetime.now.side_effect = [first_time, second_time]
            project = self.storage.create_project("Example", status_id=status_a["id"])
            updated = self.storage.update_project(
                project["id"],
                name="Example",
                status_id=status_b["id"],
                device_id=None,
                note="ready",
            )

        self.assertEqual(updated["status_id"], status_b["id"])
        self.assertEqual(updated["note"], "ready")
        self.assertNotEqual(project["updated_at"], updated["updated_at"])
        self.assertEqual(project["updated_at"], "2026-09-16T12:00:00.000100+00:00")
        self.assertEqual(updated["updated_at"], "2026-09-16T12:00:00.000200+00:00")

    def test_delete_project(self):
        project = self.storage.create_project("Example")
        self.storage.delete_project(project["id"])

        with self.assertRaises(NotFoundError):
            self.storage.get_project(project["id"])
        with self.assertRaises(NotFoundError):
            self.storage.delete_project(project["id"])

    def test_missing_foreign_key_is_rejected(self):
        with self.assertRaises(NotFoundError):
            self.storage.create_project("Example", status_id=999)

    def test_new_project_is_visible_by_default(self):
        project = self.storage.create_project("Example")

        self.assertIs(project["is_hidden"], False)

        state = self.storage.state()
        self.assertIs(state["projects"][0]["is_hidden"], False)

        fetched = self.storage.get_project(project["id"])
        self.assertIs(fetched["is_hidden"], False)

    def test_hide_and_unhide_flip_only_the_flag(self):
        status = self.storage.create_status("Review", "#3366FF")
        device = self.storage.create_device("Laptop")
        project = self.storage.create_project(
            "Example", status_id=status["id"], device_id=device["id"], note="PR #1"
        )

        hidden = self.storage.set_project_hidden(project["id"], True)
        self.assertIs(hidden["is_hidden"], True)
        self.assertEqual(hidden["name"], "Example")
        self.assertEqual(hidden["status_id"], status["id"])
        self.assertEqual(hidden["device_id"], device["id"])
        self.assertEqual(hidden["note"], "PR #1")

        state = self.storage.state()
        by_id = {item["id"]: item for item in state["projects"]}
        self.assertIs(by_id[project["id"]]["is_hidden"], True)

        visible = self.storage.set_project_hidden(project["id"], False)
        self.assertIs(visible["is_hidden"], False)
        self.assertEqual(visible["name"], "Example")
        self.assertEqual(visible["status_id"], status["id"])
        self.assertEqual(visible["device_id"], device["id"])
        self.assertEqual(visible["note"], "PR #1")

    def test_rename_without_flag_preserves_hidden(self):
        project = self.storage.create_project("Example")
        self.storage.set_project_hidden(project["id"], True)

        # Old clients send only name/status/device/note: the flag must survive.
        renamed = self.storage.update_project(
            project["id"],
            name="Renamed",
            status_id=None,
            device_id=None,
            note="",
        )

        self.assertEqual(renamed["name"], "Renamed")
        self.assertIs(renamed["is_hidden"], True)

    def test_update_with_explicit_flag_changes_visibility(self):
        project = self.storage.create_project("Example")

        hidden = self.storage.update_project(
            project["id"],
            name="Example",
            status_id=None,
            device_id=None,
            note="",
            is_hidden=True,
        )
        self.assertIs(hidden["is_hidden"], True)

        visible = self.storage.update_project(
            project["id"],
            name="Example",
            status_id=None,
            device_id=None,
            note="",
            is_hidden=False,
        )
        self.assertIs(visible["is_hidden"], False)

    def test_hidden_project_can_be_renamed_and_deleted(self):
        project = self.storage.create_project("Example")
        self.storage.set_project_hidden(project["id"], True)

        renamed = self.storage.update_project(
            project["id"],
            name="New name",
            status_id=None,
            device_id=None,
            note="kept",
            is_hidden=True,
        )
        self.assertEqual(renamed["name"], "New name")
        self.assertIs(renamed["is_hidden"], True)

        self.storage.delete_project(project["id"])
        with self.assertRaises(NotFoundError):
            self.storage.get_project(project["id"])

    def test_migration_keeps_existing_projects_visible(self):
        import sqlite3

        legacy_path = str(Path(self.tmp.name) / "legacy.db")
        conn = sqlite3.connect(legacy_path)
        conn.executescript(
            """
            CREATE TABLE statuses (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL COLLATE NOCASE UNIQUE,
                color TEXT NOT NULL,
                created_at TEXT NOT NULL
            );
            CREATE TABLE devices (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL COLLATE NOCASE UNIQUE,
                created_at TEXT NOT NULL
            );
            CREATE TABLE projects (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL COLLATE NOCASE UNIQUE,
                status_id INTEGER NULL REFERENCES statuses(id) ON DELETE RESTRICT,
                device_id INTEGER NULL REFERENCES devices(id) ON DELETE RESTRICT,
                note TEXT NOT NULL DEFAULT '',
                updated_at TEXT NOT NULL
            );
            INSERT INTO projects(name, note, updated_at) VALUES ('Legacy', '', '2026-01-01T00:00:00+00:00');
            """
        )
        conn.commit()
        conn.close()

        storage = Storage(legacy_path)
        state = storage.state()
        self.assertEqual(len(state["projects"]), 1)
        self.assertIs(state["projects"][0]["is_hidden"], False)


if __name__ == "__main__":
    unittest.main()
