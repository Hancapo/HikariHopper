from __future__ import annotations

import os
import subprocess
import sys
from pathlib import Path


def test_split_handle_keeps_one_pixel_layout_with_extended_hit_area() -> None:
    project_root = Path(__file__).parents[1]
    handle_path = project_root / "src" / "rpf_explorer" / "ui" / "SplitViewHandle.qml"
    environment = os.environ.copy()
    environment["QT_QPA_PLATFORM"] = "offscreen"
    environment["QT_QUICK_BACKEND"] = "software"

    script = f"""
from PySide6.QtCore import QPointF, QUrl
from PySide6.QtGui import QGuiApplication
from PySide6.QtQml import QQmlComponent, QQmlEngine
from PySide6.QtQuick import QQuickItem
from shiboken6 import getCppPointer, wrapInstance

app = QGuiApplication([])
engine = QQmlEngine()
component = QQmlComponent(engine, QUrl.fromLocalFile({str(handle_path)!r}))
root = component.create()
assert root is not None, [error.toString() for error in component.errors()]
handle = wrapInstance(getCppPointer(root)[0], QQuickItem)
handle.setWidth(1)
handle.setHeight(100)

assert handle.implicitWidth() == 1
assert handle.contains(QPointF(-3, 50))
assert handle.contains(QPointF(4, 50))
assert not handle.contains(QPointF(-4, 50))
assert not handle.contains(QPointF(5, 50))
"""
    subprocess.run(
        [sys.executable, "-c", script],
        check=True,
        capture_output=True,
        text=True,
        env=environment,
    )
