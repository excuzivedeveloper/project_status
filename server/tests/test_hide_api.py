import tempfile
import unittest
from pathlib import Path

from fastapi.testclient import TestClient


class HideApiTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.db_path = str(Path(self.tmp.name) / "api.db")

    def tearDown(self):
        self.tmp.cleanup()

    def _client(self) -> TestClient:
        import os

        os.environ["PROJECT_STATUS_DB_PATH"] = self.db_path
        import app.main as main

        main.get_storage.cache_clear()
        client = TestClient(main.app)
        self.addCleanup(main.get_storage.cache_clear)
        return client

    def test_state_exposes_is_hidden_and_hide_unhide_round_trip(self):
        client = self._client()

        created = client.post("/api/projects", json={"name": "Example"})
        self.assertEqual(created.status_code, 201, created.text)
        self.assertIs(created.json()["is_hidden"], False)

        state = client.get("/api/state")
        self.assertEqual(state.status_code, 200)
        self.assertIs(state.json()["projects"][0]["is_hidden"], False)

        hide = client.post("/api/projects/1/hide")
        self.assertEqual(hide.status_code, 200, hide.text)
        self.assertIs(hide.json()["is_hidden"], True)
        self.assertEqual(hide.json()["name"], "Example")

        # A plain rename payload without is_hidden must not unhide.
        rename = client.put(
            "/api/projects/1",
            json={"name": "Renamed", "status_id": None, "device_id": None, "note": ""},
        )
        self.assertEqual(rename.status_code, 200, rename.text)
        self.assertEqual(rename.json()["name"], "Renamed")
        self.assertIs(rename.json()["is_hidden"], True)

        unhide = client.post("/api/projects/1/unhide")
        self.assertEqual(unhide.status_code, 200, unhide.text)
        self.assertIs(unhide.json()["is_hidden"], False)

        # Explicit flag in the update payload is honoured.
        explicit = client.put(
            "/api/projects/1",
            json={
                "name": "Renamed",
                "status_id": None,
                "device_id": None,
                "note": "",
                "is_hidden": True,
            },
        )
        self.assertEqual(explicit.status_code, 200, explicit.text)
        self.assertIs(explicit.json()["is_hidden"], True)

    def test_hide_unknown_project_is_404(self):
        client = self._client()
        response = client.post("/api/projects/999/hide")
        self.assertEqual(response.status_code, 404)


if __name__ == "__main__":
    unittest.main()
