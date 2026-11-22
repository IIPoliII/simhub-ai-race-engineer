"""Lecture de l'instantané de télémétrie exposé par le plugin SimHub."""
import json
from pathlib import Path

import httpx


class Telemetry:
    def __init__(self, url: str, file: str | None = None):
        self.url = url
        self.file = Path(file) if file else None

    def snapshot(self) -> dict | None:
        if self.file:
            return json.loads(self.file.read_text(encoding="utf-8"))
        try:
            r = httpx.get(self.url, timeout=0.5)
            r.raise_for_status()
            return r.json()
        except (httpx.HTTPError, ValueError):
            return None
