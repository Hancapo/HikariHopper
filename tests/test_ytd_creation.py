from __future__ import annotations

from pathlib import Path

import pytest
from PySide6.QtCore import QCoreApplication, QEventLoop, QTimer

from fivefury import GameTarget, RpfArchive
from fivefury.ytd import read_ytd

from rpf_explorer.backend import (
    EntryCreationTarget,
    RpfProvider,
    create_ytd_at,
    normalize_ytd_name,
)
from rpf_explorer.bridge import ExplorerBridge


@pytest.mark.parametrize("game", list(GameTarget))
def test_creates_valid_ytd_for_each_gta_v_edition(
    tmp_path: Path,
    game: GameTarget,
) -> None:
    target = EntryCreationTarget(directory=tmp_path, game=game.value)

    assert create_ytd_at(target, "textures") == "textures.ytd"

    created = read_ytd((tmp_path / "textures.ytd").read_bytes())
    assert created.names() == ["texture"]
    texture = created.textures[0]
    assert (texture.width, texture.height, texture.mip_count) == (4, 4, 1)


def test_creates_ytd_inside_an_rpf_with_the_active_game_target(
    tmp_path: Path,
) -> None:
    archive_path = tmp_path / "assets.rpf"
    archive = RpfArchive.empty(archive_path.name)
    archive.directory("textures")
    archive.save(archive_path)

    provider = RpfProvider()
    provider._game_target = GameTarget.GTA5_ENHANCED.value
    provider.open_archive(archive_path)
    target = provider.creation_target("textures")

    assert target.game == GameTarget.GTA5_ENHANCED.value
    assert create_ytd_at(target, "created.ytd") == "created.ytd"

    with RpfArchive.from_path(archive_path) as saved:
        entry = saved.find_entry("textures/created.ytd")
        assert entry is not None
        assert read_ytd(entry.read_standalone()).names() == ["texture"]


def test_ytd_creation_uses_name_validation_and_runs_in_background(
    tmp_path: Path,
) -> None:
    app = QCoreApplication.instance() or QCoreApplication([])
    bridge = ExplorerBridge()
    bridge.provider._game_root = tmp_path
    bridge.provider._game_target = GameTarget.GTA5.value
    bridge._refresh()

    assert normalize_ytd_name("textures") == "textures.ytd"
    with pytest.raises(ValueError, match="before .ytd"):
        normalize_ytd_name(".ytd")

    assert bridge.createYtd("textures")
    assert bridge.entryOperationBusy

    loop = QEventLoop()
    bridge.entryOperationStateChanged.connect(
        lambda: loop.quit() if not bridge.entryOperationBusy else None
    )
    QTimer.singleShot(5000, loop.quit)
    loop.exec()
    app.processEvents()

    assert not bridge.entryOperationBusy
    assert bridge.entriesModel.rowCount() == 1
    entry = bridge.entriesModel.entry_at(0)
    assert entry is not None
    assert entry.name == "textures.ytd"
    assert entry.kind == "Texture Dictionary"

    assert bridge.creationNameError("textures", "ytd") == (
        "An entry with this name already exists"
    )
