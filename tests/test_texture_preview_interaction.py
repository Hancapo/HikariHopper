from __future__ import annotations

import os
import subprocess
import sys
from pathlib import Path


def test_texture_preview_checkerboard_cursor_zoom_and_pan() -> None:
    project_root = Path(__file__).parents[1]
    preview_path = project_root / "src" / "rpf_explorer" / "ui" / "TexturePreview.qml"
    environment = os.environ.copy()
    environment["QT_QPA_PLATFORM"] = "offscreen"
    environment["QT_QUICK_BACKEND"] = "software"

    script = f"""
from PySide6.QtCore import (
    Property, QCoreApplication, QObject, QPoint, QPointF, Qt, QUrl, Signal, Slot
)
from PySide6.QtGui import QFontDatabase, QGuiApplication, QWheelEvent
from PySide6.QtQml import QQmlComponent
from PySide6.QtQuick import QQuickItem, QQuickView
from PySide6.QtTest import QTest
from shiboken6 import getCppPointer, wrapInstance

class Bridge(QObject):
    selectionChanged = Signal()
    stateChanged = Signal()
    statusChanged = Signal()

    @Property(int, notify=selectionChanged)
    def selectedIndex(self): return 0
    @Property(int, notify=selectionChanged)
    def previewWidth(self): return 512
    @Property(int, notify=selectionChanged)
    def previewHeight(self): return 384
    @Property(str, notify=selectionChanged)
    def previewUrl(self): return ""
    @Property(bool, notify=stateChanged)
    def previewLoading(self): return False
    @Property(str, notify=statusChanged)
    def status(self): return ""
    @Property(str, notify=stateChanged)
    def error(self): return ""
    @Property(str, notify=selectionChanged)
    def channel(self): return "rgba"
    @Property(int, notify=selectionChanged)
    def mipLevel(self): return 0
    @Property(int, notify=selectionChanged)
    def mipCount(self): return 1
    @Slot(str)
    def setChannel(self, _value): pass
    @Slot(int)
    def setMipLevel(self, _value): pass

app = QGuiApplication([])
for font_name in ("Archivo-Variable.ttf", "SpaceMono-Regular.ttf"):
    QFontDatabase.addApplicationFont(
        {str(preview_path.parent / "fonts")!r} + "/" + font_name
    )
view = QQuickView()
view.setResizeMode(QQuickView.ResizeMode.SizeRootObjectToView)
view.resize(900, 640)
warnings = []
view.engine().warnings.connect(
    lambda errors: warnings.extend(error.toString() for error in errors)
)
component = QQmlComponent(view.engine(), QUrl.fromLocalFile({str(preview_path)!r}))
bridge = Bridge()
root = component.createWithInitialProperties({{"bridge": bridge}})
assert root is not None, [error.toString() for error in component.errors()]
view.setContent(QUrl(), component, root)
view.show()
QTest.qWait(80)
assert not warnings, warnings

viewport_object = root.findChild(QObject, "textureViewport")
frame_object = root.findChild(QObject, "textureImageFrame")
checkerboard_object = root.findChild(QObject, "textureCheckerboard")
assert viewport_object is not None and frame_object is not None
assert checkerboard_object is not None
viewport = wrapInstance(getCppPointer(viewport_object)[0], QQuickItem)
frame = wrapInstance(getCppPointer(frame_object)[0], QQuickItem)
checkerboard = wrapInstance(getCppPointer(checkerboard_object)[0], QQuickItem)
assert checkerboard.width() == viewport.width()
assert checkerboard.height() == viewport.height()
checker_origin = checkerboard.mapToScene(QPointF(0, 0)).toPoint()
rendered = view.grabWindow()
cell_00 = rendered.pixelColor(checker_origin + QPoint(8, 8))
cell_10 = rendered.pixelColor(checker_origin + QPoint(24, 8))
cell_01 = rendered.pixelColor(checker_origin + QPoint(8, 24))
cell_11 = rendered.pixelColor(checker_origin + QPoint(24, 24))
assert cell_00 != cell_10 and cell_00 != cell_01
assert cell_00 == cell_11

initial_origin = frame.mapToItem(viewport, QPointF(0, 0))
drag_from = viewport.mapToScene(QPointF(450, 300)).toPoint()
drag_to = viewport.mapToScene(QPointF(520, 350)).toPoint()
QTest.mousePress(
    view, Qt.MouseButton.LeftButton, Qt.KeyboardModifier.NoModifier, drag_from
)
for drag_point in (
    viewport.mapToScene(QPointF(470, 315)).toPoint(),
    viewport.mapToScene(QPointF(500, 335)).toPoint(),
    drag_to,
):
    QTest.mouseMove(view, drag_point, 20)
QTest.mouseRelease(
    view, Qt.MouseButton.LeftButton, Qt.KeyboardModifier.NoModifier, drag_to
)
QTest.qWait(40)
dragged_origin = frame.mapToItem(viewport, QPointF(0, 0))
assert dragged_origin.x() - initial_origin.x() > 60, (
    initial_origin, dragged_origin, viewport.property("panX"), viewport.property("panY")
)
assert dragged_origin.y() - initial_origin.y() > 40, (
    initial_origin, dragged_origin, viewport.property("panX"), viewport.property("panY")
)
assert viewport.property("panX") > 60
assert viewport.property("panY") > 40

keyboard_pan_x = viewport.property("panX")
QTest.keyClick(view, Qt.Key.Key_Right)
assert viewport.property("panX") == keyboard_pan_x + 48
QTest.keyClick(view, Qt.Key.Key_F)
QTest.qWait(20)
assert viewport.property("panX") == 0
assert viewport.property("panY") == 0

focus = QPointF(650, 190)
scene_focus = viewport.mapToScene(focus)

def zoom_one_step():
    event = QWheelEvent(
        scene_focus,
        view.mapToGlobal(scene_focus.toPoint()),
        QPoint(),
        QPoint(0, 120),
        Qt.MouseButton.NoButton,
        Qt.KeyboardModifier.NoModifier,
        Qt.ScrollPhase.ScrollUpdate,
        False,
    )
    QCoreApplication.sendEvent(view, event)
    QTest.qWait(20)

old_scale = root.property("displayScale")
old_origin = frame.mapToItem(viewport, QPointF(0, 0))
source_point = QPointF(
    (focus.x() - old_origin.x()) / old_scale,
    (focus.y() - old_origin.y()) / old_scale,
)
zoom_one_step()
new_scale = root.property("displayScale")
new_origin = frame.mapToItem(viewport, QPointF(0, 0))
anchored_point = QPointF(
    new_origin.x() + source_point.x() * new_scale,
    new_origin.y() + source_point.y() * new_scale,
)
assert new_scale > old_scale
assert abs(anchored_point.x() - focus.x()) < 2, (
    anchored_point, focus, old_scale, new_scale, viewport.property("panX")
)
assert abs(anchored_point.y() - focus.y()) < 2, (
    anchored_point, focus, old_scale, new_scale, viewport.property("panY")
)
"""
    result = subprocess.run(
        [sys.executable, "-c", script],
        check=False,
        capture_output=True,
        text=True,
        env=environment,
    )
    assert result.returncode == 0, result.stderr
