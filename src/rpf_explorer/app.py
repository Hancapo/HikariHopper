"""Application bootstrap for the QML presentation."""

from __future__ import annotations

import os
import sys
from pathlib import Path
from typing import Any

from PySide6.QtCore import QObject, QTimer, QUrl
from PySide6.QtGui import QFontDatabase
from PySide6.QtQml import QQmlApplicationEngine
from PySide6.QtWidgets import QApplication

from .settings import (
    APPLICATION_NAME,
    ORGANIZATION_NAME,
    app_settings,
    configured_game_root_to_open,
    migrate_legacy_settings,
)


class _StartupSequence(QObject):
    """Advance startup only after the splash has reached the screen."""

    def __init__(
        self,
        app: QApplication,
        engine: QQmlApplicationEngine,
        splash: Any | None,
        ui_dir: Path,
    ) -> None:
        super().__init__(app)
        self._app = app
        self._engine = engine
        self._splash = splash
        self._ui_dir = ui_dir
        self._tabs: Any = None
        self._started = False

    def start(self) -> None:
        if self._splash is None:
            QTimer.singleShot(0, self._begin)
            return
        self._splash.frameSwapped.connect(self._begin)
        QTimer.singleShot(150, self._begin)

    def _set_phase(self, message: str, phase: str) -> None:
        if self._splash is None:
            return
        self._splash.setProperty("startupMessage", message)
        self._splash.setProperty("startupPhase", phase)

    def _begin(self) -> None:
        if self._started:
            return
        self._started = True
        if self._splash is not None:
            try:
                self._splash.frameSwapped.disconnect(self._begin)
            except (RuntimeError, TypeError):
                pass
        self._set_phase("Loading explorer components…", "CORE")
        QTimer.singleShot(0, self._create_session)

    def _create_session(self) -> None:
        try:
            from .tabs import ExplorerTabs
            from .texture_viewer import TextureImageProvider

            image_provider = TextureImageProvider()
            self._engine.addImageProvider("textureviewer", image_provider)
            self._tabs = ExplorerTabs(image_provider=image_provider)
        except (ImportError, RuntimeError) as error:
            self._fail(error)
            return
        saved_game = configured_game_root_to_open(app_settings())
        message = (
            "Loading FiveFury runtime and GTA V keys…"
            if saved_game and Path(saved_game).is_dir()
            else "Checking saved GTA V installation…"
        )
        self._set_phase(message, "GAME")
        QTimer.singleShot(0, self._restore_workspace)

    def _restore_workspace(self) -> None:
        self._tabs.restore_last_game()
        if self._tabs.activeBridge.hasWorkspace:
            message = f"Loaded {self._tabs.activeBridge.gameName}"
        else:
            message = "No saved GTA V installation"
        self._set_phase(message, "GAME")
        QTimer.singleShot(0, self._prepare_interface)

    def _prepare_interface(self) -> None:
        self._set_phase("Building explorer interface…", "UI")
        QTimer.singleShot(0, self._load_interface)

    def _load_interface(self) -> None:
        try:
            self._engine.setInitialProperties({"tabs": self._tabs})
            self._engine.load(QUrl.fromLocalFile(str(self._ui_dir / "main.qml")))
            expected_roots = 2 if self._splash is not None else 1
            if len(self._engine.rootObjects()) < expected_roots:
                raise RuntimeError("QML engine could not load the interface")
        except RuntimeError as error:
            self._fail(error)
            return
        main_window = self._engine.rootObjects()[-1]
        self._set_phase("Opening workspace…", "WINDOW")
        QTimer.singleShot(0, lambda: self._finish(main_window))

    def _finish(self, main_window: Any) -> None:
        if self._splash is not None:
            self._splash.close()
        main_window.requestActivate()

    def _fail(self, error: Exception) -> None:
        print(f"HikariHopper startup failed: {error}", file=sys.stderr)
        self._set_phase(f"Startup failed · {error}", "ERROR")
        delay = 1800 if self._splash is not None else 0
        QTimer.singleShot(delay, lambda: self._app.exit(1))


def startup_splash_required() -> bool:
    return bool(configured_game_root_to_open(app_settings()))


def main() -> None:
    os.environ.setdefault("QT_QPA_PLATFORM", "windows:darkmode=2")
    app = QApplication(sys.argv)
    app.setApplicationName(APPLICATION_NAME)
    app.setOrganizationName(ORGANIZATION_NAME)
    migrate_legacy_settings()
    ui_dir = Path(__file__).with_name("ui")
    font_dir = ui_dir / "fonts"
    for font_name in (
        "Archivo-Variable.ttf",
        "SpaceMono-Regular.ttf",
        "SpaceMono-Bold.ttf",
    ):
        if QFontDatabase.addApplicationFont(str(font_dir / font_name)) < 0:
            raise SystemExit(f"Could not load bundled font: {font_name}")
    qss_path = ui_dir / "style.qss"
    app.setStyleSheet(qss_path.read_text(encoding="utf-8"))

    engine = QQmlApplicationEngine()
    splash = None
    if startup_splash_required():
        engine.load(QUrl.fromLocalFile(str(ui_dir / "SplashScreen.qml")))
        if not engine.rootObjects():
            raise SystemExit("QML engine could not load the splash screen")
        splash = engine.rootObjects()[0]
    startup = _StartupSequence(app, engine, splash, ui_dir)
    startup.start()
    raise SystemExit(app.exec())
