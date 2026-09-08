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

from PySide6.QtCore import Q_ARG, QObject, QMetaObject, QPoint, QPointF, Qt, QUrl
from PySide6.QtGui import QGuiApplication
from PySide6.QtQml import QQmlComponent
from PySide6.QtQuick import QQuickItem, QQuickView
from PySide6.QtTest import QTest
from shiboken6 import getCppPointer, wrapInstance

from rpf_explorer.bridge import ExplorerBridge

app = QGuiApplication([])
with TemporaryDirectory() as directory:
    (Path(directory) / "sample.txt").write_text("sample", encoding="utf-8")
    (Path(directory) / "existing.rpf").write_bytes(b"existing")
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
    assert menu.property("count") == 7
    assert submenu.property("count") == 7
    assert (
        root.findChild(QObject, "createYtdMenuItem").property("text")
        == "Texture dictionary"
    )
    assert (
        root.findChild(QObject, "rpfArchiveSection").property("text")
        == "RPF ARCHIVE"
    )
    assert (
        root.findChild(QObject, "createEmptyRpfMenuItem").property("text")
        == "Empty"
    )
    assert (
        root.findChild(QObject, "createRpfFromFolderMenuItem").property("text")
        == "From folder…"
    )
    assert (
        root.findChild(QObject, "createRpfFromZipMenuItem").property("text")
        == "From ZIP…"
    )

    assert QMetaObject.invokeMethod(
        root,
        "openCreateDialog",
        Q_ARG("QVariant", "empty-rpf"),
        Q_ARG("QVariant", "existing"),
        Q_ARG("QVariant", ""),
    )
    QTest.qWait(30)
    create_dialog = root.findChild(QObject, "explorerCreateDialog")
    assert create_dialog is not None
    assert create_dialog.property("nameError") == "An entry with this name already exists"
    create_button = root.findChild(QObject, "confirmCreateButton")
    assert create_button is not None
    assert not create_button.property("enabled")
    name_field = root.findChild(QObject, "creationNameField")
    assert name_field is not None
    name_field.setProperty("text", "fresh")
    QTest.qWait(20)
    assert create_dialog.property("nameError") == ""
    assert create_button.property("enabled")
    QMetaObject.invokeMethod(create_dialog, "close")
    QTest.qWait(30)

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
        delete_item_object = root.findChild(QObject, "deleteMenuItem")
        assert delete_item_object is not None
        delete_item = wrapInstance(getCppPointer(delete_item_object)[0], QQuickItem)
        delete_center = delete_item.mapToScene(QPointF(
            delete_item.width() / 2,
            delete_item.height() / 2,
        ))
        QTest.mouseClick(
            view,
            Qt.MouseButton.LeftButton,
            Qt.KeyboardModifier.NoModifier,
            QPoint(round(delete_center.x()), round(delete_center.y())),
        )
        QTest.qWait(30)
        delete_dialog = root.findChild(QObject, "deleteEntriesDialog")
        assert delete_dialog is not None
        assert delete_dialog.property("visible")
        QMetaObject.invokeMethod(delete_dialog, "close")
        QTest.qWait(30)
        QTest.keyClick(view, Qt.Key.Key_Delete)
        QTest.qWait(30)
        delete_dialog = root.findChild(QObject, "deleteEntriesDialog")
        assert delete_dialog is not None
        assert delete_dialog.property("visible")
        QMetaObject.invokeMethod(delete_dialog, "close")
        QTest.qWait(30)
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


def test_delete_menu_trashes_a_previously_opened_rpf() -> None:
    project_root = Path(__file__).parents[1]
    ui_path = project_root / "src" / "rpf_explorer" / "ui"
    environment = os.environ.copy()
    environment["QT_QPA_PLATFORM"] = "offscreen"
    environment["QT_QUICK_BACKEND"] = "software"
    environment["PYTHONPATH"] = str(project_root / "src")

    script = f"""
from pathlib import Path
from tempfile import TemporaryDirectory

from PySide6.QtCore import QObject, QPoint, QPointF, Qt, QUrl
from PySide6.QtGui import QGuiApplication
from PySide6.QtQml import QQmlComponent
from PySide6.QtQuick import QQuickItem, QQuickView
from PySide6.QtTest import QTest
from shiboken6 import getCppPointer, wrapInstance

import rpf_explorer.bridge as bridge_module
from fivefury import RpfArchive
from rpf_explorer.bridge import ExplorerBridge


class FakeFile:
    @staticmethod
    def moveToTrash(path):
        Path(path).unlink()
        return True


def click_item(view, item_object):
    item = wrapInstance(getCppPointer(item_object)[0], QQuickItem)
    center = item.mapToScene(QPointF(item.width() / 2, item.height() / 2))
    QTest.mouseClick(
        view,
        Qt.MouseButton.LeftButton,
        Qt.KeyboardModifier.NoModifier,
        QPoint(round(center.x()), round(center.y())),
    )


bridge_module.QFile = FakeFile
app = QGuiApplication([])
with TemporaryDirectory() as directory:
    source = Path(directory) / "sample.rpf"
    RpfArchive.empty(source.name).save(source)
    bridge = ExplorerBridge()
    bridge.provider._game_root = Path(directory)
    bridge.provider._game_target = "gta5_enhanced"
    bridge.provider.open_archive(source)
    bridge.provider.show_game()
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

    QTest.mouseClick(
        view,
        Qt.MouseButton.RightButton,
        Qt.KeyboardModifier.NoModifier,
        QPoint(300, 78),
    )
    QTest.qWait(30)
    delete_menu_item = root.findChild(QObject, "deleteMenuItem")
    assert delete_menu_item is not None
    click_item(view, delete_menu_item)
    QTest.qWait(30)

    confirm_button = root.findChild(QObject, "confirmDeleteButton")
    assert confirm_button is not None
    click_item(view, confirm_button)

    for _ in range(50):
        QTest.qWait(20)
        if not source.exists():
            break

    assert not source.exists()
"""
    subprocess.run(
        [sys.executable, "-c", script],
        check=True,
        capture_output=True,
        text=True,
        env=environment,
    )
