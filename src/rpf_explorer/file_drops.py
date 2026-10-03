"""Shared validation for local file drops into the explorer and editors."""

from pathlib import Path
from PySide6.QtCore import QUrl


def local_drop_paths(urls) -> tuple[Path, ...]:
    paths = []
    for value in urls:
        url = value if isinstance(value, QUrl) else QUrl(str(value))
        if not url.isLocalFile():
            raise ValueError("Only local files can be imported")
        path = Path(url.toLocalFile()).expanduser().resolve(strict=True)
        if not path.is_file():
            raise ValueError(f"Only files can be imported: {path.name}")
        paths.append(path)
    if not paths:
        raise ValueError("Drop one or more files to import")
    return tuple(paths)
