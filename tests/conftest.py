from __future__ import annotations

from pathlib import Path

import pytest
from PySide6.QtCore import QSettings


@pytest.fixture(autouse=True)
def isolated_application_settings(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    settings = QSettings(str(tmp_path / "settings.ini"), QSettings.Format.IniFormat)

    def isolated_settings() -> QSettings:
        return settings

    monkeypatch.setattr("rpf_explorer.bridge.app_settings", isolated_settings)
    monkeypatch.setattr("rpf_explorer.tabs.app_settings", isolated_settings)
