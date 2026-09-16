import tempfile
import unittest
from pathlib import Path

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

    def test_update_project_uses_last_write(self):
        status_a = self.storage.create_status("Coder", "#11AA11")
        status_b = self.storage.create_status("Review", "#AA11AA")
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

    def test_missing_foreign_key_is_rejected(self):
        with self.assertRaises(NotFoundError):
            self.storage.create_project("Example", status_id=999)


if __name__ == "__main__":
    unittest.main()
