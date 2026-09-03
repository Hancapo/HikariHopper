"""Qt models that keep archive data separate from widgets."""

from __future__ import annotations

from dataclasses import dataclass
from typing import Any


@dataclass(frozen=True, slots=True)
class EntryRecord:
    name: str
    path: str
    kind: str
    size: int
    children: int = 0
    native_entry: Any = None
    navigable_children: int = 0

    @property
    def is_directory(self) -> bool:
        return self.kind == "Folder"
