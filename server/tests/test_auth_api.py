import os
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

from fastapi.testclient import TestClient

import app.main as main


class AuthApiTests(unittest.TestCase):
    def test_startup_rejects_missing_empty_and_placeholder_token(self):
        for token in (None, "", "   ", main.TOKEN_PLACEHOLDER):
            with self.subTest(token=token):
                env = {} if token is None else {"PROJECT_STATUS_API_TOKEN": token}
                with patch.dict(os.environ, env, clear=True):
                    with self.assertRaisesRegex(RuntimeError, "PROJECT_STATUS_API_TOKEN"):
                        with TestClient(main.app):
                            pass

    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        os.environ["PROJECT_STATUS_DB_PATH"] = str(Path(self.tmp.name) / "api.db")
        os.environ["PROJECT_STATUS_API_TOKEN"] = "test-token"
        main.get_storage.cache_clear()
        self.client = TestClient(main.app)

    def tearDown(self):
        main.get_storage.cache_clear()
        self.tmp.cleanup()

    def test_api_requires_token_and_health_does_not(self):
        self.assertEqual(self.client.get("/health").status_code, 200)
        self.assertEqual(self.client.get("/api/state").status_code, 401)
        self.assertEqual(self.client.get("/api/state", headers={"Authorization": "Bearer wrong-token"}).status_code, 401)

        headers = {"Authorization": "Bearer test-token"}
        state = self.client.get("/api/state", headers=headers)
        self.assertEqual(state.status_code, 200)
        self.assertEqual(set(state.json()), {"projects", "statuses", "devices"})
        self.assertEqual(self.client.post("/api/projects", json={"name": "Example"}).status_code, 401)
        self.assertEqual(self.client.post("/api/projects", headers=headers, json={"name": "Example"}).status_code, 201)

    def test_valid_token_allows_startup_and_health(self):
        with TestClient(main.app) as client:
            self.assertEqual(client.get("/health").status_code, 200)
