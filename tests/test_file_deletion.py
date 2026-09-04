from __future__ import annotations

from pathlib import Path

import pytest
from PySide6.QtCore import QCoreApplication, QEventLoop, QTimer

from rpf_explorer.backend import RpfProvider, delete_archive_files_at
from rpf_explorer.bridge import ExplorerBridge
from rpf_explorer.tabs import ExplorerTabs


def test_deletes_multiple_files_from_rpf_without_touching_siblings(
    tmp_path: Path,
) -> None:
    from fivefury import RpfArchive

    archive_path = tmp_path / "archive.rpf"
    archive = RpfArchive.empty(archive_path.name)
    archive.file("folder/first.txt", b"first")
    archive.file("folder/second.ydr", b"second")
    archive.file(
        "folder/child.rpf",
        RpfArchive.empty("child.rpf").to_bytes(),
    )
    archive.file("folder/keep.txt", b"keep")
    archive.save(archive_path)

    provider = RpfProvider()
    provider.open_archive(archive_path)
    entries = [
        entry
        for entry in provider.archive_entries("folder")
        if entry.name != "keep.txt"
    ]
    target = provider.deletion_target(entries, "folder")

    assert delete_archive_files_at(target) == 3

    with RpfArchive.from_path(archive_path) as saved:
        assert saved.find_entry("folder/first.txt") is None
        assert saved.find_entry("folder/second.ydr") is None
        assert saved.find_entry("folder/child.rpf") is None
        assert saved.find_entry("folder/keep.txt") is not None


def test_deletes_file_from_nested_rpf(tmp_path: Path) -> None:
    from fivefury import RpfArchive

    nested = RpfArchive.empty("inner.rpf")
    nested.file("delete.txt", b"delete")
    nested.file("keep.txt", b"keep")
    archive_path = tmp_path / "outer.rpf"
    outer = RpfArchive.empty(archive_path.name)
    outer.file("inner.rpf", nested.to_bytes())
    outer.save(archive_path)

    provider = RpfProvider()
    provider.open_archive(archive_path)
    provider.open_nested(provider.archive_entries(".")[0])
    delete_entry = next(
        entry
        for entry in provider.archive_entries(".")
        if entry.name == "delete.txt"
    )

    assert delete_archive_files_at(
        provider.deletion_target([delete_entry])
    ) == 1

    with RpfArchive.from_path(archive_path) as saved:
        assert saved.find_entry("inner.rpf/delete.txt") is None
        assert saved.find_entry("inner.rpf/keep.txt") is not None


def test_deletion_target_rejects_folders(tmp_path: Path) -> None:
    from fivefury import RpfArchive

    archive_path = tmp_path / "archive.rpf"
    archive = RpfArchive.empty(archive_path.name)
    archive.directory("folder")
    archive.save(archive_path)

    provider = RpfProvider()
    provider.open_archive(archive_path)

    with pytest.raises(ValueError, match="Folder deletion is not supported"):
        provider.deletion_target(provider.archive_entries("."))


def test_trashing_an_open_rpf_releases_its_archive_handle(tmp_path: Path) -> None:
    from fivefury import RpfArchive

    archive_path = tmp_path / "archive.rpf"
    RpfArchive.empty(archive_path.name).save(archive_path)
    provider = RpfProvider()
    provider._game_root = tmp_path
    provider._game_target = "gta5_enhanced"
    provider.open_archive(archive_path)
    provider.show_game()
    archive_entry = next(
        entry
        for entry in provider.game_entries(".")
        if entry.name == archive_path.name
    )

    target = provider.deletion_target([archive_entry])

    assert target.loose_paths == (archive_path.resolve(),)
    assert not provider.has_archive


def test_bridge_moves_loose_files_to_recycle_bin_in_background(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    app = QCoreApplication.instance() or QCoreApplication([])
    source = tmp_path / "delete.txt"
    source.write_text("delete", encoding="utf-8")
    moved: list[Path] = []

    class FakeFile:
        @staticmethod
        def moveToTrash(path: str) -> bool:
            candidate = Path(path)
            candidate.unlink()
            moved.append(candidate)
            return True

    monkeypatch.setattr("rpf_explorer.bridge.QFile", FakeFile)
    bridge = ExplorerBridge()
    bridge.provider._game_root = tmp_path
    bridge.provider._game_target = "gta5_enhanced"
    bridge._reset_navigation()
    bridge._refresh()
    bridge.selectEntry(0)

    confirmations: list[bool] = []
    bridge.deleteConfirmationRequested.connect(lambda: confirmations.append(True))
    bridge.requestDeleteSelection()
    assert confirmations == [True]
    assert bridge.deleteSelectedFiles()

    loop = QEventLoop()
    bridge.entryOperationStateChanged.connect(
        lambda: loop.quit() if not bridge.entryOperationBusy else None
    )
    QTimer.singleShot(5000, loop.quit)
    loop.exec()
    app.processEvents()

    assert moved == [source]
    assert not source.exists()
    assert bridge.entriesModel.rowCount() == 0
    assert bridge.status == "Moved 1 file to the Recycle Bin"


def test_tabs_release_archive_handles_and_refresh_shared_folder(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    from fivefury import RpfArchive

    app = QCoreApplication.instance() or QCoreApplication([])
    shared_folder = tmp_path / "hola"
    shared_folder.mkdir()
    first_path = shared_folder / "first.rpf"
    second_path = shared_folder / "second.rpf"
    RpfArchive.empty(first_path.name).save(first_path)
    RpfArchive.empty(second_path.name).save(second_path)

    tabs = ExplorerTabs()
    first = tabs.activeBridge
    first.provider._game_root = tmp_path
    first.provider._game_target = "gta5_enhanced"
    first._refresh()
    first.navigate("hola")

    tabs.newTab()
    second = tabs.activeBridge
    second.provider._game_root = tmp_path
    second.provider._game_target = "gta5_enhanced"
    second.provider.open_archive(first_path)
    second.provider.show_game()
    second._refresh()
    second.navigate("hola")
    assert second.provider.has_archive
    assert not second.inArchive

    moved: list[Path] = []

    class FakeFile:
        @staticmethod
        def moveToTrash(path: str) -> bool:
            assert not second.provider.has_archive
            candidate = Path(path)
            candidate.unlink()
            moved.append(candidate)
            return True

    monkeypatch.setattr("rpf_explorer.bridge.QFile", FakeFile)
    tabs.activateTab(0)
    first.selectAllEntries()
    assert first.deleteSelectedFiles()
    assert second.entryOperationBusy

    loop = QEventLoop()
    first.entryOperationStateChanged.connect(
        lambda: loop.quit() if not first.entryOperationBusy else None
    )
    QTimer.singleShot(5000, loop.quit)
    loop.exec()
    app.processEvents()

    assert set(moved) == {first_path, second_path}
    assert first.entriesModel.rowCount() == 0
    assert second.entriesModel.rowCount() == 0
    assert not second.entryOperationBusy


def test_stale_missing_selection_does_not_block_remaining_deletions(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    app = QCoreApplication.instance() or QCoreApplication([])
    missing = tmp_path / "first.txt"
    remaining = tmp_path / "second.txt"
    missing.write_text("first", encoding="utf-8")
    remaining.write_text("second", encoding="utf-8")

    class FakeFile:
        @staticmethod
        def moveToTrash(path: str) -> bool:
            candidate = Path(path)
            candidate.unlink()
            return True

    monkeypatch.setattr("rpf_explorer.bridge.QFile", FakeFile)
    bridge = ExplorerBridge()
    bridge.provider._game_root = tmp_path
    bridge.provider._game_target = "gta5_enhanced"
    bridge._refresh()
    bridge.selectAllEntries()
    missing.unlink()

    assert bridge.deleteSelectedFiles()
    loop = QEventLoop()
    bridge.entryOperationStateChanged.connect(
        lambda: loop.quit() if not bridge.entryOperationBusy else None
    )
    QTimer.singleShot(5000, loop.quit)
    loop.exec()
    app.processEvents()

    assert not remaining.exists()
    assert bridge.entriesModel.rowCount() == 0
    assert bridge.status == "Moved 1 file to the Recycle Bin"


def test_multi_delete_attempts_every_selected_file_after_a_failure(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    app = QCoreApplication.instance() or QCoreApplication([])
    blocked = tmp_path / "blocked.txt"
    movable = tmp_path / "movable.txt"
    blocked.write_text("blocked", encoding="utf-8")
    movable.write_text("movable", encoding="utf-8")
    attempts: list[str] = []

    class FakeFile:
        @staticmethod
        def moveToTrash(path: str) -> bool:
            candidate = Path(path)
            attempts.append(candidate.name)
            if candidate == blocked:
                return False
            candidate.unlink()
            return True

    monkeypatch.setattr("rpf_explorer.bridge.QFile", FakeFile)
    bridge = ExplorerBridge()
    bridge.provider._game_root = tmp_path
    bridge.provider._game_target = "gta5_enhanced"
    bridge._refresh()
    bridge.selectAllEntries()

    assert bridge.deleteSelectedFiles()
    loop = QEventLoop()
    bridge.entryOperationStateChanged.connect(
        lambda: loop.quit() if not bridge.entryOperationBusy else None
    )
    QTimer.singleShot(5000, loop.quit)
    loop.exec()
    app.processEvents()

    assert attempts == ["blocked.txt", "movable.txt"]
    assert blocked.exists()
    assert not movable.exists()
    assert bridge.entriesModel.rowCount() == 1
    assert "blocked.txt" in bridge.status
