from __future__ import annotations

import struct
from pathlib import Path

import pytest
from PySide6.QtCore import QCoreApplication, QEventLoop, QTimer, QUrl

from rpf_explorer.backend import (
    EntryCreationTarget,
    RpfProvider,
    import_files_at,
)
from rpf_explorer.bridge import ExplorerBridge


def test_imports_files_into_a_loose_directory_without_overwriting(
    tmp_path: Path,
) -> None:
    source = tmp_path / "source"
    source.mkdir()
    first = source / "first.txt"
    second = source / "second.bin"
    first.write_text("first", encoding="utf-8")
    second.write_bytes(b"second")
    destination = tmp_path / "game"
    destination.mkdir()

    names = import_files_at(
        EntryCreationTarget(directory=destination),
        (first, second),
    )

    assert names == ("first.txt", "second.bin")
    assert (destination / "first.txt").read_text(encoding="utf-8") == "first"
    assert (destination / "second.bin").read_bytes() == b"second"

    with pytest.raises(FileExistsError, match="already exists"):
        import_files_at(EntryCreationTarget(directory=destination), (first,))
    assert (destination / "first.txt").read_text(encoding="utf-8") == "first"


def test_imports_streamed_file_kinds_into_an_rpf_folder(tmp_path: Path) -> None:
    from fivefury import RpfArchive
    from fivefury.rpf import RSC7_MAGIC
    from fivefury.rpf.entries import (
        RpfBinaryFileEntry,
        RpfResourceFileEntry,
    )

    source = tmp_path / "source"
    source.mkdir()
    raw_path = source / "plain.txt"
    raw_path.write_bytes(b"plain")
    resource_path = source / "model.ydr"
    resource_data = struct.pack("<IIII", RSC7_MAGIC, 0, 0, 0) + b"resource"
    resource_path.write_bytes(resource_data)
    nested_path = source / "nested.rpf"
    RpfArchive.empty(nested_path.name).save(nested_path)

    archive_path = tmp_path / "outer.rpf"
    outer = RpfArchive.empty(archive_path.name)
    outer.directory("mods")
    outer.save(archive_path)
    provider = RpfProvider()
    provider.open_archive(archive_path)

    names = import_files_at(
        provider.creation_target("mods"),
        (raw_path, resource_path, nested_path),
    )

    assert names == ("plain.txt", "model.ydr", "nested.rpf")
    with RpfArchive.from_path(archive_path) as saved:
        raw = saved.find_entry("mods/plain.txt")
        resource = saved.find_entry("mods/model.ydr")
        nested = saved.find_entry("mods/nested.rpf")
        assert isinstance(raw, RpfBinaryFileEntry)
        assert raw.read_standalone() == b"plain"
        assert isinstance(resource, RpfResourceFileEntry)
        assert resource.read_standalone() == resource_data
        assert isinstance(nested, RpfBinaryFileEntry)
        with RpfArchive.from_bytes(
            nested.read_standalone(),
            name=nested.name,
        ) as child:
            assert list(child.iter_entries()) == []


def test_rpf_import_rejects_name_collisions_before_writing(tmp_path: Path) -> None:
    from fivefury import RpfArchive

    archive_path = tmp_path / "outer.rpf"
    outer = RpfArchive.empty(archive_path.name)
    outer.file("existing.txt", b"original")
    outer.save(archive_path)
    source = tmp_path / "existing.txt"
    source.write_bytes(b"replacement")
    provider = RpfProvider()
    provider.open_archive(archive_path)
    before = archive_path.read_bytes()

    with pytest.raises(FileExistsError, match="already exists"):
        import_files_at(provider.creation_target("."), (source,))

    assert archive_path.read_bytes() == before


def test_bridge_imports_dropped_file_urls_in_background(tmp_path: Path) -> None:
    app = QCoreApplication.instance() or QCoreApplication([])
    game = tmp_path / "game"
    game.mkdir()
    source = tmp_path / "source"
    source.mkdir()
    first = source / "first.txt"
    second = source / "second.txt"
    first.write_text("first", encoding="utf-8")
    second.write_text("second", encoding="utf-8")
    bridge = ExplorerBridge()
    bridge.provider._game_root = game
    bridge.provider._game_target = "gta5_enhanced"
    bridge._reset_navigation()
    bridge._refresh()

    assert bridge.importDroppedFiles(
        [QUrl.fromLocalFile(str(first)), QUrl.fromLocalFile(str(second))]
    )
    assert bridge.entryOperationBusy

    loop = QEventLoop()
    bridge.entryOperationStateChanged.connect(
        lambda: loop.quit() if not bridge.entryOperationBusy else None
    )
    QTimer.singleShot(5000, loop.quit)
    loop.exec()
    app.processEvents()

    assert not bridge.entryOperationBusy
    assert (game / "first.txt").read_text(encoding="utf-8") == "first"
    assert (game / "second.txt").read_text(encoding="utf-8") == "second"
    assert bridge.selectionCount == 2
    assert bridge.status == "Imported 2 files"


def test_bridge_rejects_dropped_directories(tmp_path: Path) -> None:
    bridge = ExplorerBridge()
    bridge.provider._game_root = tmp_path
    bridge.provider._game_target = "gta5_enhanced"
    directory = tmp_path / "folder"
    directory.mkdir()

    assert not bridge.importDroppedFiles([QUrl.fromLocalFile(str(directory))])
    assert bridge.status == "Could not import files: Only files can be imported: folder"
