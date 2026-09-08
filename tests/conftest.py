from __future__ import annotations

from pathlib import Path

import pytest
from PySide6.QtCore import QCoreApplication, QSettings


@pytest.fixture(autouse=True)
def isolated_application_settings(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    settings = QSettings(str(tmp_path / "settings.ini"), QSettings.Format.IniFormat)

    def isolated_settings() -> QSettings:
        return settings

    monkeypatch.setattr("rpf_explorer.bridge.app_settings", isolated_settings)
    monkeypatch.setattr("rpf_explorer.settings.app_settings", isolated_settings)
    monkeypatch.setattr("rpf_explorer.tabs.app_settings", isolated_settings)


@pytest.fixture
def texture_dictionary():
    from fivefury.ytd import Texture, TextureFormat, Ytd

    def make(*names):
        return Ytd([
            Texture.from_raw(bytes([0, 0, 255, 255]) * 4, 2, 2,
                             TextureFormat.A8R8G8B8, 1, name=name)
            for name in names
        ])

    return make


@pytest.fixture
def texture_sessions():
    from rpf_explorer.tabs import ExplorerTabs
    from rpf_explorer.texture_viewer import TextureImageProvider

    app = QCoreApplication.instance() or QCoreApplication([])
    tabs = ExplorerTabs(image_provider=TextureImageProvider())
    yield tabs
    for bridge in tabs._tabs:
        assert bridge._entry_operation_pool.waitForDone(5000)
        assert bridge.textureViewer._thread_pool.waitForDone(5000)
        bridge.textureViewer.shutdown()
        bridge.provider.close()
    app.processEvents()


@pytest.fixture
def settle_texture_sessions(texture_sessions):
    def settle():
        # Loading a dictionary schedules previews; continuations can load another.
        for _ in range(4):
            for bridge in texture_sessions._tabs:
                assert bridge._entry_operation_pool.waitForDone(5000)
                assert bridge.textureViewer._thread_pool.waitForDone(5000)
            QCoreApplication.processEvents()

    return settle
