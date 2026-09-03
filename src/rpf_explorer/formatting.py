"""Shared display formatting operators for the explorer UI."""

from __future__ import annotations

from datetime import datetime
from pathlib import Path

_TEXTURE_FORMAT_LABELS = {
    "BC1": "DXT1",
    "BC2": "DXT3",
    "BC3": "DXT5",
    "BC4": "ATI1",
    "BC5": "ATI2",
    "BC6H": "BPTC HDR",
    "BC7": "BPTC RGBA",
    "A8R8G8B8": "ARGB 32-bit",
    "R8G8B8A8": "RGBA 32-bit",
    "B5G6R5": "RGB 565",
    "B5G5R5A1": "RGB5A1",
    "R10G10B10A2": "RGB10A2",
    "R16_FLOAT": "R16F",
    "R16G16_FLOAT": "RG16F",
    "R16G16B16A16_FLOAT": "RGBA16F",
    "R32_FLOAT": "R32F",
    "R32G32B32A32_FLOAT": "RGBA32F",
}


def format_size(value: int) -> str:
    """Return a compact, stable file-size label."""
    if value < 1024:
        return f"{value:,} B"
    units = ("KB", "MB", "GB", "TB")
    scaled = float(value)
    for unit in units:
        scaled /= 1024.0
        if scaled < 1024.0 or unit == units[-1]:
            return f"{scaled:.1f} {unit}"
    return f"{value:,} B"


def format_timestamp(value: datetime | None) -> str:
    if value is None:
        return "—"
    return value.astimezone().strftime("%d/%m/%Y  %H:%M")


def format_texture_format(value: str, *, include_exact: bool = False) -> str:
    """Use familiar texture names while retaining exact storage terminology."""
    exact = str(value).upper()
    friendly = _TEXTURE_FORMAT_LABELS.get(exact, exact)
    if include_exact and friendly != exact:
        return f"{friendly} ({exact})"
    return friendly


def display_path(value: str | Path) -> str:
    """Use a compact path for the tab and the status bar."""
    path = Path(value)
    return path.stem or path.name or "Workspace"
