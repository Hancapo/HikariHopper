from __future__ import annotations

import os
import subprocess
import sys
from pathlib import Path


def test_external_file_drop_area_forwards_local_urls() -> None:
    project_root = Path(__file__).parents[1]
    ui_path = project_root / "src" / "rpf_explorer" / "ui"
    environment = os.environ.copy()
    environment["QT_QPA_PLATFORM"] = "offscreen"
    environment["QT_QUICK_BACKEND"] = "software"

    script = f"""
from pathlib import Path
from tempfile import TemporaryDirectory

from PySide6.QtCore import QByteArray, Property, QMimeData, QObject, QPoint, QPointF, Qt, QUrl, Signal, Slot
from PySide6.QtGui import QDragEnterEvent, QDropEvent, QGuiApplication
from PySide6.QtQml import QQmlComponent
from PySide6.QtQuick import QQuickView
from PySide6.QtTest import QTest


class Bridge(QObject):
    entryOperationStateChanged = Signal()

    def __init__(self):
        super().__init__()
        self.received = []

    @Property(str, constant=True)
    def entryDragMimeType(self):
        return "application/x-hikarihopper-entry-drag"

    @Property(bool, notify=entryOperationStateChanged)
    def entryOperationBusy(self):
        return False

    @Property(bool, constant=True)
    def hasWorkspace(self):
        return True

    @Slot("QVariantList", result=bool)
    def importDroppedFiles(self, urls):
        self.received = list(urls)
        return True


app = QGuiApplication([])
with TemporaryDirectory() as directory:
    source = Path(directory) / "sample.txt"
    source.write_text("sample", encoding="utf-8")
    bridge = Bridge()
    view = QQuickView()
    component = QQmlComponent(view.engine())
    component.setData(b'''\
import QtQuick
import "."

Item {{
    required property var bridge
    width: 640
    height: 360

    ExternalFileDropArea {{
        objectName: "externalFileDropArea"
        anchors.fill: parent
        bridge: parent.bridge
    }}
}}
''', QUrl.fromLocalFile({str(ui_path)!r} + "/"))
    root = component.createWithInitialProperties({{"bridge": bridge}})
    assert root is not None, [error.toString() for error in component.errors()]
    view.setContent(QUrl(), component, root)
    view.resize(640, 360)
    view.show()
    QTest.qWait(50)

    mime_data = QMimeData()
    mime_data.setUrls([QUrl.fromLocalFile(str(source))])
    enter = QDragEnterEvent(
        QPoint(320, 180),
        Qt.DropAction.CopyAction,
        mime_data,
        Qt.MouseButton.LeftButton,
        Qt.KeyboardModifier.NoModifier,
    )
    QGuiApplication.sendEvent(view, enter)
    assert enter.isAccepted()

    drop = QDropEvent(
        QPointF(320, 180),
        Qt.DropAction.CopyAction,
        mime_data,
        Qt.MouseButton.LeftButton,
        Qt.KeyboardModifier.NoModifier,
    )
    QGuiApplication.sendEvent(view, drop)
    QTest.qWait(20)

    assert drop.isAccepted()
    assert len(bridge.received) == 1
    assert Path(bridge.received[0].toLocalFile()) == source

    bridge.received.clear()
    internal_mime = QMimeData()
    internal_mime.setUrls([QUrl.fromLocalFile(str(source))])
    internal_mime.setData(bridge.entryDragMimeType, QByteArray(b"1"))
    internal_enter = QDragEnterEvent(
        QPoint(320, 180),
        Qt.DropAction.CopyAction,
        internal_mime,
        Qt.MouseButton.LeftButton,
        Qt.KeyboardModifier.NoModifier,
    )
    QGuiApplication.sendEvent(view, internal_enter)
    assert not internal_enter.isAccepted()
    assert bridge.received == []
"""
    subprocess.run(
        [sys.executable, "-c", script],
        check=True,
        capture_output=True,
        text=True,
        env=environment,
    )
