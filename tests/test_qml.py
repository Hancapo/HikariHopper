from __future__ import annotations

import os
import subprocess
import sys
from pathlib import Path


def test_split_handle_aligns_layout_hit_area_and_cursor_feedback() -> None:
    project_root = Path(__file__).parents[1]
    ui_path = project_root / "src" / "rpf_explorer" / "ui"
    environment = os.environ.copy()
    environment["QT_QPA_PLATFORM"] = "offscreen"
    environment["QT_QUICK_BACKEND"] = "software"

    script = f"""
from PySide6.QtCore import QObject, QPoint, QPointF, Qt, QUrl
from PySide6.QtGui import QGuiApplication
from PySide6.QtQml import QQmlComponent
from PySide6.QtQuick import QQuickItem, QQuickView
from PySide6.QtTest import QTest
from shiboken6 import getCppPointer, wrapInstance

app = QGuiApplication([])
view = QQuickView()
component = QQmlComponent(view.engine())
component.setData(b'''\
import QtQuick
import QtQuick.Controls.Basic
import "."

SplitView {{
    width: 500
    height: 180
    handle: SplitViewHandle {{ objectName: "splitHandle" }}

    Rectangle {{
        objectName: "leftPane"
        SplitView.preferredWidth: 247
    }}

    Rectangle {{ SplitView.fillWidth: true }}
}}
''', QUrl.fromLocalFile({str(ui_path)!r} + "/"))
root = component.create()
assert root is not None, [error.toString() for error in component.errors()]
view.setContent(QUrl(), component, root)
view.setWidth(500)
view.setHeight(180)
view.show()
QTest.qWait(50)

handle_object = root.findChild(QObject, "splitHandle")
left_object = root.findChild(QObject, "leftPane")
assert handle_object is not None
assert left_object is not None
handle = wrapInstance(getCppPointer(handle_object)[0], QQuickItem)
left_pane = wrapInstance(getCppPointer(left_object)[0], QQuickItem)
handle_x = round(handle.mapToScene(QPointF(0, 0)).x())

assert handle.implicitWidth() == 1
assert handle.width() == 1
assert handle.contains(QPointF(-3, 50))
assert handle.contains(QPointF(4, 50))
assert not handle.contains(QPointF(-4, 50))
assert not handle.contains(QPointF(5, 50))

for x in range(handle_x - 3, handle_x + 5):
    QTest.mouseMove(view, QPoint(x, 90))
    assert view.cursor().shape() == Qt.CursorShape.SplitHCursor

for x in (handle_x - 4, handle_x + 5):
    QTest.mouseMove(view, QPoint(x, 90))
    assert view.cursor().shape() == Qt.CursorShape.ArrowCursor

initial_width = left_pane.width()
QTest.mousePress(
    view,
    Qt.MouseButton.LeftButton,
    Qt.KeyboardModifier.NoModifier,
    QPoint(handle_x - 3, 90),
)
QTest.mouseMove(view, QPoint(handle_x + 20, 90), 20)
QTest.mouseRelease(
    view,
    Qt.MouseButton.LeftButton,
    Qt.KeyboardModifier.NoModifier,
    QPoint(handle_x + 20, 90),
)
assert left_pane.width() > initial_width
"""
    subprocess.run(
        [sys.executable, "-c", script],
        check=True,
        capture_output=True,
        text=True,
        env=environment,
    )


def test_explorer_context_menu_opens_in_list_and_grid_views() -> None:
    project_root = Path(__file__).parents[1]
    ui_path = project_root / "src" / "rpf_explorer" / "ui"
    environment = os.environ.copy()
    environment["QT_QPA_PLATFORM"] = "offscreen"
    environment["QT_QUICK_BACKEND"] = "software"
    environment["PYTHONPATH"] = str(project_root / "src")

    script = f"""
from pathlib import Path
from tempfile import TemporaryDirectory

from PySide6.QtCore import QObject, QMetaObject, QPoint, Qt, QUrl
from PySide6.QtGui import QGuiApplication
from PySide6.QtQml import QQmlComponent
from PySide6.QtQuick import QQuickView
from PySide6.QtTest import QTest

from rpf_explorer.bridge import ExplorerBridge

app = QGuiApplication([])
with TemporaryDirectory() as directory:
    (Path(directory) / "sample.txt").write_text("sample", encoding="utf-8")
    bridge = ExplorerBridge()
    bridge.provider._game_root = Path(directory)
    bridge.provider._game_target = "gta5_enhanced"
    bridge._reset_navigation()
    bridge._refresh()

    view = QQuickView()
    view.setResizeMode(QQuickView.ResizeMode.SizeRootObjectToView)
    component = QQmlComponent(
        view.engine(),
        QUrl.fromLocalFile({str(ui_path / "WorkspaceView.qml")!r}),
    )
    root = component.createWithInitialProperties({{
        "bridge": bridge,
        "entryModel": bridge.entriesModel,
        "treeModel": bridge.treeModel,
    }})
    assert root is not None, [error.toString() for error in component.errors()]
    view.setContent(QUrl(), component, root)
    view.resize(1000, 600)
    view.show()
    QTest.qWait(80)

    menu = root.findChild(QObject, "explorerContextMenu")
    submenu = root.findChild(QObject, "newEntryMenu")
    assert menu is not None
    assert submenu is not None
    assert menu.property("count") == 5
    assert submenu.property("count") == 4

    for mode in ("list", "grid"):
        bridge.setViewMode(mode)
        QTest.qWait(30)
        QTest.mouseClick(
            view,
            Qt.MouseButton.RightButton,
            Qt.KeyboardModifier.NoModifier,
            QPoint(300, 78 if mode == "list" else 100),
        )
        QTest.qWait(30)
        assert menu.property("visible")
        assert bridge.selectionCount == 1
        QMetaObject.invokeMethod(menu, "close")
        QTest.mouseClick(
            view,
            Qt.MouseButton.RightButton,
            Qt.KeyboardModifier.NoModifier,
            QPoint(700, 180),
        )
        QTest.qWait(30)
        assert menu.property("visible")
        assert bridge.selectionCount == 0
        QMetaObject.invokeMethod(menu, "close")
        QTest.keyClick(view, Qt.Key.Key_Menu)
        QTest.qWait(30)
        assert menu.property("visible")
        QMetaObject.invokeMethod(menu, "close")
"""
    subprocess.run(
        [sys.executable, "-c", script],
        check=True,
        capture_output=True,
        text=True,
        env=environment,
    )
