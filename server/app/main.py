from __future__ import annotations

import os
import re
from functools import lru_cache

from fastapi import FastAPI, HTTPException, Response, status
from pydantic import BaseModel, Field, field_validator

from .storage import ConflictError, InUseError, NotFoundError, Storage

app = FastAPI(title="Project Status API", version="0.1.0")
COLOR_RE = re.compile(r"^#[0-9A-Fa-f]{6}$")


@lru_cache(maxsize=1)
def get_storage() -> Storage:
    return Storage(os.getenv("PROJECT_STATUS_DB_PATH", "/data/project_status.db"))


class NamedModel(BaseModel):
    name: str = Field(min_length=1, max_length=100)

    @field_validator("name")
    @classmethod
    def strip_name(cls, value: str) -> str:
        value = value.strip()
        if not value:
            raise ValueError("name cannot be blank")
        return value


class StatusInput(NamedModel):
    color: str

    @field_validator("color")
    @classmethod
    def validate_color(cls, value: str) -> str:
        if not COLOR_RE.fullmatch(value):
            raise ValueError("color must be #RRGGBB")
        return value.upper()


class DeviceInput(NamedModel):
    pass


class ProjectInput(NamedModel):
    status_id: int | None = None
    device_id: int | None = None
    note: str = Field(default="", max_length=200)

    @field_validator("note")
    @classmethod
    def normalize_note(cls, value: str) -> str:
        value = value.strip()
        if "\n" in value or "\r" in value:
            raise ValueError("note must be a single line")
        return value


def call_storage(func, *args, **kwargs):
    try:
        return func(*args, **kwargs)
    except NotFoundError as exc:
        raise HTTPException(status_code=404, detail=str(exc)) from exc
    except (ConflictError, InUseError) as exc:
        raise HTTPException(status_code=409, detail=str(exc)) from exc


@app.get("/health")
def health() -> dict[str, str]:
    get_storage().state()
    return {"status": "ok"}


@app.get("/api/state")
def state_snapshot() -> dict:
    return get_storage().state()


@app.post("/api/statuses", status_code=status.HTTP_201_CREATED)
def create_status(payload: StatusInput) -> dict:
    return call_storage(get_storage().create_status, payload.name, payload.color)


@app.put("/api/statuses/{status_id}")
def update_status(status_id: int, payload: StatusInput) -> dict:
    return call_storage(
        get_storage().update_status, status_id, name=payload.name, color=payload.color
    )


@app.delete("/api/statuses/{status_id}", status_code=status.HTTP_204_NO_CONTENT)
def delete_status(status_id: int) -> Response:
    call_storage(get_storage().delete_status, status_id)
    return Response(status_code=status.HTTP_204_NO_CONTENT)


@app.post("/api/devices", status_code=status.HTTP_201_CREATED)
def create_device(payload: DeviceInput) -> dict:
    return call_storage(get_storage().create_device, payload.name)


@app.put("/api/devices/{device_id}")
def update_device(device_id: int, payload: DeviceInput) -> dict:
    return call_storage(get_storage().update_device, device_id, name=payload.name)


@app.delete("/api/devices/{device_id}", status_code=status.HTTP_204_NO_CONTENT)
def delete_device(device_id: int) -> Response:
    call_storage(get_storage().delete_device, device_id)
    return Response(status_code=status.HTTP_204_NO_CONTENT)


@app.post("/api/projects", status_code=status.HTTP_201_CREATED)
def create_project(payload: ProjectInput) -> dict:
    return call_storage(
        get_storage().create_project,
        payload.name,
        status_id=payload.status_id,
        device_id=payload.device_id,
        note=payload.note,
    )


@app.put("/api/projects/{project_id}")
def update_project(project_id: int, payload: ProjectInput) -> dict:
    return call_storage(
        get_storage().update_project,
        project_id,
        name=payload.name,
        status_id=payload.status_id,
        device_id=payload.device_id,
        note=payload.note,
    )


@app.delete("/api/projects/{project_id}", status_code=status.HTTP_204_NO_CONTENT)
def delete_project(project_id: int) -> Response:
    call_storage(get_storage().delete_project, project_id)
    return Response(status_code=status.HTTP_204_NO_CONTENT)
