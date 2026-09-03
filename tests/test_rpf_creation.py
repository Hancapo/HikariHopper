from __future__ import annotations

from pathlib import Path
from zipfile import ZipFile

import pytest
from PySide6.QtCore import QCoreApplication, QEventLoop, QTimer

from rpf_explorer.backend import (
    EntryCreationTarget,
    RpfProvider,
    create_empty_rpf_at,
    create_folder_at,
    create_rpf_from_folder_at,
    create_rpf_from_zip_at,
)
from rpf_explorer.bridge import ExplorerBridge


def test_creates_folders_and_rpfs_in_loose_game_directory(tmp_path: Path) -> None:
    from fivefury import RpfArchive

    source_folder = tmp_path / "source"
    source_folder.mkdir()
    (source_folder / "from-folder.txt").write_text("folder", encoding="utf-8")
    source_zip = tmp_path / "source.zip"
    with ZipFile(source_zip, "w") as archive:
        archive.writestr("from-zip.txt", "zip")

    destination = tmp_path / "game"
    destination.mkdir()
    target = EntryCreationTarget(directory=destination)

    assert create_folder_at(target, "stream") == "stream"
    assert create_empty_rpf_at(target, "empty") == "empty.rpf"
    assert (
        create_rpf_from_folder_at(target, "folder_source", source_folder)
        == "folder_source.rpf"
    )
    assert (
        create_rpf_from_zip_at(target, "zip_source.rpf", source_zip)
        == "zip_source.rpf"
    )

    assert (destination / "stream").is_dir()
    with RpfArchive.from_path(destination / "empty.rpf") as archive:
        assert list(archive.iter_entries()) == []
    with RpfArchive.from_path(destination / "folder_source.rpf") as archive:
        assert archive.find_entry("from-folder.txt") is not None
    with RpfArchive.from_path(destination / "zip_source.rpf") as archive:
        assert archive.find_entry("from-zip.txt") is not None


def test_creates_entries_inside_an_open_rpf(tmp_path: Path) -> None:
    from fivefury import RpfArchive

    archive_path = tmp_path / "outer.rpf"
    outer = RpfArchive.empty(archive_path.name)
    outer.directory("mods")
    outer.save(archive_path)

    provider = RpfProvider()
    provider.open_archive(archive_path)
    target = provider.creation_target("mods")

    assert create_folder_at(target, "stream") == "stream"
    assert create_empty_rpf_at(target, "inner.rpf") == "inner.rpf"

    with RpfArchive.from_path(archive_path) as saved:
        assert saved.find_entry("mods/stream") is not None
        nested = saved.find_entry("mods/inner.rpf")
        assert nested is not None
        with RpfArchive.from_bytes(
            nested.read_standalone(),
            name="inner.rpf",
        ) as child:
            assert list(child.iter_entries()) == []


def test_creates_rpf_inside_a_nested_archive(tmp_path: Path) -> None:
    from fivefury import RpfArchive

    archive_path = tmp_path / "outer.rpf"
    outer = RpfArchive.empty(archive_path.name)
    outer.file("inner.rpf", RpfArchive.empty("inner.rpf").to_bytes())
    outer.save(archive_path)

    provider = RpfProvider()
    provider.open_archive(archive_path)
    inner_entry = provider.archive_entries(".")[0]
    provider.open_nested(inner_entry)

    assert create_empty_rpf_at(
        provider.creation_target("."),
        "deepest.rpf",
    ) == "deepest.rpf"

    with RpfArchive.from_path(archive_path) as saved:
        deepest = saved.find_entry("inner.rpf/deepest.rpf")
        assert deepest is not None
        with RpfArchive.from_bytes(
            deepest.read_standalone(),
            name="deepest.rpf",
        ) as child:
            assert list(child.iter_entries()) == []


def test_creation_rejects_invalid_and_duplicate_names(tmp_path: Path) -> None:
    target = EntryCreationTarget(directory=tmp_path)
    create_folder_at(target, "existing")

    with pytest.raises(FileExistsError, match="already exists"):
        create_folder_at(target, "EXISTING")
    with pytest.raises(ValueError, match="valid Windows file name"):
        create_empty_rpf_at(target, "bad/name")
    with pytest.raises(ValueError, match="valid Windows file name"):
        create_folder_at(target, "CON")


def test_bridge_creates_entries_without_blocking_the_ui_thread(tmp_path: Path) -> None:
    app = QCoreApplication.instance() or QCoreApplication([])
    bridge = ExplorerBridge()
    bridge.provider._game_root = tmp_path
    bridge.provider._game_target = "gta5_enhanced"

    assert bridge.createEmptyRpf("background")
    assert bridge.entryOperationBusy

    loop = QEventLoop()
    bridge.entryOperationStateChanged.connect(
        lambda: loop.quit() if not bridge.entryOperationBusy else None
    )
    QTimer.singleShot(5000, loop.quit)
    loop.exec()
    app.processEvents()

    assert not bridge.entryOperationBusy
    assert (tmp_path / "background.rpf").is_file()
    assert bridge.entriesModel.rowCount() == 1
    assert bridge.entriesModel.entry_at(0).name == "background.rpf"
