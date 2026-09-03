from __future__ import annotations

import os
import subprocess
import sys
from pathlib import Path


def test_idle_scrollbar_thumb_is_centered_in_its_gutter() -> None:
    project_root = Path(__file__).parents[1]
    scrollbar_path = (
        project_root / "src" / "rpf_explorer" / "ui" / "QuietScrollBar.qml"
    )
    environment = os.environ.copy()
    environment["QT_QPA_PLATFORM"] = "offscreen"
    environment["QT_QUICK_BACKEND"] = "software"

    script = f"""
from PySide6.QtCore import QUrl
from PySide6.QtGui import QGuiApplication
from PySide6.QtQml import QQmlComponent, QQmlEngine
from PySide6.QtQuick import QQuickItem
from shiboken6 import getCppPointer, wrapInstance

app = QGuiApplication([])
engine = QQmlEngine()
component = QQmlComponent(engine, QUrl.fromLocalFile({str(scrollbar_path)!r}))
root = component.create()
assert root is not None, [error.toString() for error in component.errors()]
scrollbar = wrapInstance(getCppPointer(root)[0], QQuickItem)
scrollbar.setWidth(12)
scrollbar.setHeight(200)
scrollbar.setProperty("size", 0.25)
app.processEvents()

visible_rectangles = [
    nested
    for child in scrollbar.childItems()
    for nested in child.childItems()
    if nested.metaObject().className() == "QQuickRectangle" and nested.isVisible()
]
assert len(visible_rectangles) == 1
thumb = visible_rectangles[0]
assert thumb.width() == 4
assert thumb.x() == (scrollbar.width() - thumb.width()) / 2
"""
    result = subprocess.run(
        [sys.executable, "-c", script],
        check=False,
        capture_output=True,
        text=True,
        env=environment,
    )
    assert result.returncode == 0, result.stderr
