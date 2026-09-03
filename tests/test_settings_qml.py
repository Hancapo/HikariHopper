from __future__ import annotations

import os
import subprocess
import sys
from pathlib import Path


def test_settings_window_loads_with_game_path_controller() -> None:
    project_root = Path(__file__).parents[1]
    settings_window = project_root / "src" / "rpf_explorer" / "ui" / "SettingsWindow.qml"
    environment = os.environ.copy()
    environment["QT_QPA_PLATFORM"] = "offscreen"
    environment["QT_QUICK_BACKEND"] = "software"

    script = f"""
from PySide6.QtCore import Property, QObject, QUrl, Slot
from PySide6.QtGui import QGuiApplication
from PySide6.QtQml import QQmlComponent, QQmlEngine

class GamePaths(QObject):
    @Property(str, constant=True)
    def legacyPath(self): return r"C:\\Games\\GTAV Legacy"
    @Property(str, constant=True)
    def enhancedPath(self): return ""
    @Property(bool, constant=True)
    def legacyPathValid(self): return True
    @Property(bool, constant=True)
    def enhancedPathValid(self): return False
    @Slot(str)
    def setLegacyPath(self, _path): pass
    @Slot(str)
    def setEnhancedPath(self, _path): pass
    @Slot()
    def browseLegacyPath(self): pass
    @Slot()
    def browseEnhancedPath(self): pass

app = QGuiApplication([])
engine = QQmlEngine()
component = QQmlComponent(engine, QUrl.fromLocalFile({str(settings_window)!r}))
controller = GamePaths()
root = component.createWithInitialProperties({{"gamePaths": controller}})
assert root is not None, [error.toString() for error in component.errors()]
assert root.property("width") == 1080
assert root.property("height") == 688
root.deleteLater()
app.processEvents()
"""
    result = subprocess.run(
        [sys.executable, "-c", script],
        check=False,
        capture_output=True,
        text=True,
        env=environment,
    )
    assert result.returncode == 0, result.stderr


def test_future_games_follow_pc_release_order() -> None:
    project_root = Path(__file__).parents[1]
    source = (
        project_root / "src" / "rpf_explorer" / "ui" / "SettingsWindow.qml"
    ).read_text(encoding="utf-8")
    expected = [
        "Grand Theft Auto III",
        "Grand Theft Auto: Vice City",
        "Grand Theft Auto: San Andreas",
        "Bully: Scholarship Edition",
        "Grand Theft Auto IV",
        "Red Dead Redemption 2",
        "Red Dead Redemption",
    ]

    positions = [source.index(title) for title in expected]

    assert positions == sorted(positions)
    assert "Grand Theft Auto VI" not in source
