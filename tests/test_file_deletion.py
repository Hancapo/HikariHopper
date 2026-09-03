from __future__ import annotations

from pathlib import Path

import pytest
from PySide6.QtCore import QCoreApplication, QEventLoop, QTimer

from rpf_explorer.backend import RpfProvider, delete_archive_files_at
from rpf_explorer.bridge import ExplorerBridge


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


def test_cannot_trash_an_rpf_that_is_still_open(tmp_path: Path) -> None:
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

    with pytest.raises(ValueError, match="Close the RPF archive"):
        provider.deletion_target([archive_entry])


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
        def moveToTrash(path: str) -> tuple[bool, str]:
            candidate = Path(path)
            candidate.unlink()
            moved.append(candidate)
            return True, "Recycle Bin/delete.txt"

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
