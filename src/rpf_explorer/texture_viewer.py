"""Texture dictionary presentation bridge backed by FiveFury and TexFury."""

from __future__ import annotations

from collections.abc import Callable
from dataclasses import dataclass, replace
from enum import Enum
from functools import partial
from pathlib import Path
from threading import RLock
from typing import Any
from uuid import uuid4

from PySide6.QtCore import (
    Property,
    QAbstractListModel,
    QByteArray,
    QModelIndex,
    QObject,
    QRunnable,
    QSize,
    Qt,
    QThreadPool,
    Signal,
    Slot,
)
from PySide6.QtGui import QImage
from PySide6.QtQuick import QQuickImageProvider
from PySide6.QtWidgets import QFileDialog

from .formatting import format_size, format_texture_format

_INVALID_INDEX = QModelIndex()
_CHANNELS = frozenset({"rgba", "r", "g", "b", "a"})
_THUMBNAIL_SIZE = 96
_UNDO_LIMIT = 24
_UNDO_DATA_LIMIT = 512 * 1024 * 1024
_MIP_FILTERS = {
    "box": "BOX",
    "triangle": "TRIANGLE",
    "cubic-bspline": "CUBIC_BSPLINE",
    "catmull-rom": "CATMULL_ROM",
    "mitchell": "MITCHELL",
    "point": "POINT",
}
_EDITABLE_FORMATS = (
    "BC1",
    "BC2",
    "BC3",
    "BC4",
    "BC5",
    "BC7",
    "A8R8G8B8",
)


class TextureImageProvider(QQuickImageProvider):
    """Serve decoded texture images to QML without temporary PNG files."""

    def __init__(self) -> None:
        super().__init__(QQuickImageProvider.ImageType.Image)
        self._images: dict[str, QImage] = {}
        self._lock = RLock()

    def put(self, key: str, image: QImage) -> None:
        with self._lock:
            self._images[key] = image

    def contains(self, key: str) -> bool:
        with self._lock:
            return key in self._images

    def clear_prefix(self, prefix: str) -> None:
        with self._lock:
            self._images = {
                key: image
                for key, image in self._images.items()
                if not key.startswith(prefix)
            }

    def requestImage(
        self,
        image_id: str,
        size: QSize,
        requested_size: QSize,
    ) -> QImage:
        with self._lock:
            image = QImage(self._images.get(image_id, QImage()))
        if image.isNull():
            image = QImage(1, 1, QImage.Format.Format_RGBA8888)
            image.fill(Qt.GlobalColor.transparent)
        size.setWidth(image.width())
        size.setHeight(image.height())
        if (
            requested_size.width() > 0
            and requested_size.height() > 0
            and requested_size != image.size()
        ):
            image = image.scaled(
                requested_size.width(),
                requested_size.height(),
                Qt.AspectRatioMode.KeepAspectRatio,
                Qt.TransformationMode.SmoothTransformation,
            )
        return image


@dataclass(frozen=True, slots=True)
class TextureRecord:
    name: str
    dimensions: str
    format_name: str
    usage: str
    data_size: int
    mip_count: int
    texture: Any


class TextureUndoKind(Enum):
    REPLACE = "replace"
    INSERT = "insert"


@dataclass(frozen=True, slots=True)
class TextureUndoAction:
    label: str
    kind: TextureUndoKind
    row: int
    texture: Any
    previous_revision: int

    @property
    def data_size(self) -> int:
        return len(self.texture.data)


class TextureListModel(QAbstractListModel):
    NAME = Qt.ItemDataRole.UserRole + 1
    DIMENSIONS = Qt.ItemDataRole.UserRole + 2
    FORMAT = Qt.ItemDataRole.UserRole + 3
    USAGE = Qt.ItemDataRole.UserRole + 4
    DATA_SIZE = Qt.ItemDataRole.UserRole + 5
    DATA_SIZE_LABEL = Qt.ItemDataRole.UserRole + 6
    THUMBNAIL_URL = Qt.ItemDataRole.UserRole + 7
    MIP_COUNT = Qt.ItemDataRole.UserRole + 8

    def __init__(self, parent: QObject | None = None) -> None:
        super().__init__(parent)
        self._records: list[TextureRecord] = []
        self._thumbnail_keys: dict[int, str] = {}

    def roleNames(self) -> dict[int, QByteArray]:
        return {
            self.NAME: QByteArray(b"name"),
            self.DIMENSIONS: QByteArray(b"dimensions"),
            self.FORMAT: QByteArray(b"formatName"),
            self.USAGE: QByteArray(b"usage"),
            self.DATA_SIZE: QByteArray(b"dataSize"),
            self.DATA_SIZE_LABEL: QByteArray(b"dataSizeLabel"),
            self.THUMBNAIL_URL: QByteArray(b"thumbnailUrl"),
            self.MIP_COUNT: QByteArray(b"mipCount"),
        }

    def rowCount(self, parent: QModelIndex = _INVALID_INDEX) -> int:
        return 0 if parent.isValid() else len(self._records)

    def data(self, index: QModelIndex, role: int = Qt.ItemDataRole.DisplayRole) -> Any:
        if not index.isValid() or not 0 <= index.row() < len(self._records):
            return None
        record = self._records[index.row()]
        return {
            self.NAME: record.name,
            self.DIMENSIONS: record.dimensions,
            self.FORMAT: format_texture_format(record.format_name),
            self.USAGE: record.usage,
            self.DATA_SIZE: record.data_size,
            self.DATA_SIZE_LABEL: format_size(record.data_size),
            self.THUMBNAIL_URL: self._thumbnail_keys.get(index.row(), ""),
            self.MIP_COUNT: record.mip_count,
        }.get(role)

    def set_textures(self, textures: list[Any]) -> None:
        self.beginResetModel()
        self._records = [self._record(texture) for texture in textures]
        self._thumbnail_keys.clear()
        self.endResetModel()

    def clear(self) -> None:
        self.set_textures([])

    def record_at(self, row: int) -> TextureRecord | None:
        return self._records[row] if 0 <= row < len(self._records) else None

    def set_thumbnail(self, row: int, url: str) -> None:
        if not 0 <= row < len(self._records):
            return
        self._thumbnail_keys[row] = url
        index = self.index(row, 0)
        self.dataChanged.emit(index, index, [self.THUMBNAIL_URL])

    def replace_texture(self, row: int, texture: Any) -> None:
        if not 0 <= row < len(self._records):
            return
        self._records[row] = self._record(texture)
        self._thumbnail_keys.pop(row, None)
        index = self.index(row, 0)
        self.dataChanged.emit(index, index, list(self.roleNames()))

    def remove_texture(self, row: int) -> None:
        if not 0 <= row < len(self._records):
            return
        self.beginRemoveRows(_INVALID_INDEX, row, row)
        self._records.pop(row)
        self._thumbnail_keys.clear()
        self.endRemoveRows()
        self._publish_empty_thumbnails()

    def insert_texture(self, row: int, texture: Any) -> None:
        resolved = max(0, min(int(row), len(self._records)))
        self.beginInsertRows(_INVALID_INDEX, resolved, resolved)
        self._records.insert(resolved, self._record(texture))
        self._thumbnail_keys.clear()
        self.endInsertRows()
        self._publish_empty_thumbnails()

    def _publish_empty_thumbnails(self) -> None:
        if not self._records:
            return
        self.dataChanged.emit(
            self.index(0, 0),
            self.index(len(self._records) - 1, 0),
            [self.THUMBNAIL_URL],
        )

    @staticmethod
    def _record(texture: Any) -> TextureRecord:
        return TextureRecord(
            name=str(texture.name),
            dimensions=f"{texture.width} × {texture.height}",
            format_name=str(texture.format_name),
            usage=str(getattr(texture.usage, "name", texture.usage)),
            data_size=len(texture.data),
            mip_count=int(texture.mip_count),
            texture=texture,
        )


class _TaskSignals(QObject):
    completed = Signal(object)
    failed = Signal(object)


class _DictionaryTask(QRunnable):
    def __init__(self, generation: int, data: bytes) -> None:
        super().__init__()
        self.signals = _TaskSignals()
        self._generation = generation
        self._data = data

    @Slot()
    def run(self) -> None:
        try:
            from fivefury.ytd import read_ytd

            dictionary = read_ytd(self._data)
        except (ImportError, MemoryError, OSError, RuntimeError, ValueError) as error:
            self.signals.failed.emit((self._generation, str(error)))
            return
        self.signals.completed.emit((self._generation, dictionary))


class _ImageTask(QRunnable):
    def __init__(
        self,
        generation: int,
        row: int,
        texture: Any,
        mode: str,
        mip: int,
        thumbnail: bool,
    ) -> None:
        super().__init__()
        self.signals = _TaskSignals()
        self._generation = generation
        self._row = row
        self._texture = texture
        self._mode = mode
        self._mip = mip
        self._thumbnail = thumbnail

    @Slot()
    def run(self) -> None:
        kind = "thumbnail" if self._thumbnail else "preview"
        try:
            image = decode_texture_image(self._texture, self._mode, self._mip)
            if self._thumbnail:
                image = image.scaled(
                    _THUMBNAIL_SIZE,
                    _THUMBNAIL_SIZE,
                    Qt.AspectRatioMode.KeepAspectRatio,
                    Qt.TransformationMode.SmoothTransformation,
                )
        except (ImportError, MemoryError, OSError, RuntimeError, ValueError) as error:
            self.signals.failed.emit(
                (
                    self._generation,
                    self._row,
                    kind,
                    self._mode,
                    self._mip,
                    str(error),
                )
            )
            return
        self.signals.completed.emit(
            (self._generation, self._row, kind, self._mode, self._mip, image)
        )


class _TextureTransformTask(QRunnable):
    def __init__(
        self,
        generation: int,
        row: int,
        source: Any,
        operation: str,
        transform: Callable[[Any], Any],
    ) -> None:
        super().__init__()
        self.signals = _TaskSignals()
        self._generation = generation
        self._row = row
        self._source = source
        self._operation = operation
        self._transform = transform

    @Slot()
    def run(self) -> None:
        try:
            editable = _to_texfury_texture(self._source)
            transformed = self._transform(editable)
            changed = transformed is not editable
            result = (
                _to_fivefury_texture(transformed, self._source)
                if changed
                else self._source
            )
        except (ImportError, MemoryError, OSError, RuntimeError, TypeError, ValueError) as error:
            self.signals.failed.emit(
                (self._generation, self._row, self._operation, str(error))
            )
            return
        self.signals.completed.emit(
            (self._generation, self._row, self._operation, result, changed)
        )


class _SaveTask(QRunnable):
    def __init__(
        self,
        generation: int,
        label: str,
        save_action: Callable[[], None],
        source_update: tuple[str, str] | None,
    ) -> None:
        super().__init__()
        self.signals = _TaskSignals()
        self._generation = generation
        self._label = label
        self._save_action = save_action
        self._source_update = source_update

    @Slot()
    def run(self) -> None:
        try:
            self._save_action()
        except (MemoryError, OSError, RuntimeError, TypeError, ValueError) as error:
            self.signals.failed.emit((self._generation, self._label, str(error)))
            return
        self.signals.completed.emit(
            (self._generation, self._label, self._source_update)
        )


def _to_texfury_texture(texture: Any) -> Any:
    from texfury import BCFormat, Texture

    return Texture.from_raw(
        texture.data,
        texture.width,
        texture.height,
        BCFormat(texture.format.value),
        texture.mip_count,
        list(texture.mip_offsets),
        list(texture.mip_sizes),
        texture.name,
    )


def _to_fivefury_texture(texture: Any, source: Any) -> Any:
    from fivefury.ytd import Texture, TextureFormat

    return Texture.from_raw(
        texture.data,
        texture.width,
        texture.height,
        TextureFormat(texture.format.value),
        texture.mip_count,
        name=source.name,
        usage=source.usage,
        usage_flags=source.usage_flags,
    )


def mip_count_for_dimensions(width: int, height: int, min_mip_size: int) -> int:
    """Return the TexFury chain length for the requested dimensions."""
    return len(mip_dimensions_for_minimum(width, height, min_mip_size))


def mip_dimensions_for_minimum(
    width: int,
    height: int,
    min_mip_size: int,
) -> tuple[tuple[int, int], ...]:
    """Build a mip chain without letting either dimension fall below the minimum."""
    mip_width = int(width)
    mip_height = int(height)
    minimum = max(1, int(min_mip_size))
    if mip_width <= 0 or mip_height <= 0:
        return ()
    dimensions = [(mip_width, mip_height)]
    while True:
        next_width = max(1, mip_width // 2)
        next_height = max(1, mip_height // 2)
        if min(next_width, next_height) < minimum:
            break
        mip_width = next_width
        mip_height = next_height
        dimensions.append((mip_width, mip_height))
    return tuple(dimensions)


def texfury_mip_stop_size(width: int, height: int, min_mip_size: int) -> int:
    """Translate a minimum short edge into TexFury's native mip stop value."""
    dimensions = mip_dimensions_for_minimum(width, height, min_mip_size)
    if not dimensions:
        return max(1, int(min_mip_size))
    return max(dimensions[-1])


def _mip_filter(name: str) -> Any:
    from texfury import MipFilter

    try:
        return MipFilter[_MIP_FILTERS[name.casefold()]]
    except KeyError as error:
        raise ValueError(f"Unsupported mip filter: {name}") from error


def decode_texture_image(texture: Any, mode: str = "rgba", mip: int = 0) -> QImage:
    """Decode one FiveFury texture through TexFury into an owned QImage."""
    if mode not in _CHANNELS:
        raise ValueError(f"Unsupported texture channel: {mode}")
    if not 0 <= mip < texture.mip_count:
        raise ValueError(f"Mip level {mip} is outside the stored texture chain")
    decoder = _to_texfury_texture(texture)
    rgba, width, height = decoder.to_rgba(mip)
    if mode != "rgba":
        channel_index = {"r": 0, "g": 1, "b": 2, "a": 3}[mode]
        values = rgba[channel_index::4]
        pixels = width * height
        channel_rgba = bytearray(pixels * 4)
        channel_rgba[0::4] = values
        channel_rgba[1::4] = values
        channel_rgba[2::4] = values
        channel_rgba[3::4] = b"\xff" * pixels
        rgba = bytes(channel_rgba)
    return QImage(
        rgba,
        width,
        height,
        width * 4,
        QImage.Format.Format_RGBA8888,
    ).copy()


class TextureViewerBridge(QObject):
    stateChanged = Signal()
    selectionChanged = Signal()
    statusChanged = Signal()
    openRequested = Signal()
    sourceSaved = Signal(str)
    saveFinished = Signal(bool)

    def __init__(
        self,
        image_provider: TextureImageProvider,
        parent: QObject | None = None,
    ) -> None:
        super().__init__(parent)
        self._image_provider = image_provider
        self._thread_pool = QThreadPool(self)
        self._thread_pool.setMaxThreadCount(2)
        self._textures_model = TextureListModel(self)
        self._generation = 0
        self._session = ""
        self._dictionary: Any = None
        self._source_name = ""
        self._source_path = ""
        self._source_saver: Callable[[bytes], None] | None = None
        self._source_prepare: Callable[[], None] | None = None
        self._source_root_path = ""
        self._selected_index = -1
        self._channel = "rgba"
        self._mip_level = 0
        self._preview_url = ""
        self._preview_revision = 0
        self._loading = False
        self._preview_loading = False
        self._operation_busy = False
        self._saving = False
        self._undo_stack: list[TextureUndoAction] = []
        self._revision = 0
        self._saved_revision = 0
        self._next_revision = 0
        self._error = ""
        self._status = "No texture dictionary loaded"

    @Property(QObject, constant=True)
    def texturesModel(self) -> QObject:
        return self._textures_model

    @Property(str, notify=stateChanged)
    def sourceName(self) -> str:
        return self._source_name

    @Property(str, notify=stateChanged)
    def sourcePath(self) -> str:
        return self._source_path

    @Property(int, notify=stateChanged)
    def textureCount(self) -> int:
        return self._textures_model.rowCount()

    @Property(bool, notify=stateChanged)
    def loading(self) -> bool:
        return self._loading

    @Property(bool, notify=stateChanged)
    def previewLoading(self) -> bool:
        return self._preview_loading

    @Property(bool, notify=stateChanged)
    def operationBusy(self) -> bool:
        return self._operation_busy

    @Property(bool, notify=stateChanged)
    def saving(self) -> bool:
        return self._saving

    @Property(bool, notify=stateChanged)
    def modified(self) -> bool:
        return self._revision != self._saved_revision

    @Property(bool, notify=stateChanged)
    def canUndo(self) -> bool:
        return bool(self._undo_stack) and not self._operation_busy

    @Property(str, notify=stateChanged)
    def undoLabel(self) -> str:
        if not self._undo_stack:
            return ""
        return self._undo_stack[-1].label

    @Property(bool, notify=stateChanged)
    def canSaveSource(self) -> bool:
        return self._source_saver is not None or (
            bool(self._source_path) and "::" not in self._source_path
        )

    @Property("QVariantList", notify=stateChanged)
    def formatOptions(self) -> list[dict[str, str]]:
        return [
            {
                "label": format_texture_format(name, include_exact=True),
                "value": name,
            }
            for name in _EDITABLE_FORMATS
        ]

    @Property(int, notify=stateChanged)
    def maximumDimension(self) -> int:
        if self._dictionary is None:
            return 0x7FFF
        target = str(getattr(self._dictionary.game, "value", self._dictionary.game))
        return 0xFFFF if target == "gta5_enhanced" else 0x7FFF

    @Property(str, notify=stateChanged)
    def error(self) -> str:
        return self._error

    @Property(str, notify=statusChanged)
    def status(self) -> str:
        return self._status

    @Property(int, notify=selectionChanged)
    def selectedIndex(self) -> int:
        return self._selected_index

    @Property(str, notify=selectionChanged)
    def selectedName(self) -> str:
        record = self._selected_record()
        return record.name if record is not None else ""

    @Property(str, notify=selectionChanged)
    def selectedDimensions(self) -> str:
        record = self._selected_record()
        return record.dimensions if record is not None else ""

    @Property(int, notify=selectionChanged)
    def selectedWidth(self) -> int:
        record = self._selected_record()
        return int(record.texture.width) if record is not None else 0

    @Property(int, notify=selectionChanged)
    def selectedHeight(self) -> int:
        record = self._selected_record()
        return int(record.texture.height) if record is not None else 0

    @Property(int, notify=selectionChanged)
    def previewWidth(self) -> int:
        return max(1, self.selectedWidth >> self._mip_level)

    @Property(int, notify=selectionChanged)
    def previewHeight(self) -> int:
        return max(1, self.selectedHeight >> self._mip_level)

    @Property(str, notify=selectionChanged)
    def selectedFormat(self) -> str:
        record = self._selected_record()
        return (
            format_texture_format(record.format_name, include_exact=True)
            if record is not None
            else ""
        )

    @Property(str, notify=selectionChanged)
    def selectedUsage(self) -> str:
        record = self._selected_record()
        return record.usage if record is not None else ""

    @Property(str, notify=selectionChanged)
    def selectedDataSize(self) -> str:
        record = self._selected_record()
        return format_size(record.data_size) if record is not None else ""

    @Property(int, notify=selectionChanged)
    def mipCount(self) -> int:
        record = self._selected_record()
        return record.mip_count if record is not None else 0

    @Property(int, notify=selectionChanged)
    def mipLevel(self) -> int:
        return self._mip_level

    @Property(str, notify=selectionChanged)
    def channel(self) -> str:
        return self._channel

    @Property(str, notify=selectionChanged)
    def previewUrl(self) -> str:
        return self._preview_url

    def open_dictionary(
        self,
        name: str,
        source_path: str,
        data: bytes,
        *,
        source_saver: Callable[[bytes], None] | None = None,
        source_prepare: Callable[[], None] | None = None,
        source_root_path: str = "",
    ) -> None:
        self._generation += 1
        generation = self._generation
        self._thread_pool.clear()
        if self._session:
            self._image_provider.clear_prefix(f"{self._session}/")
        self._session = uuid4().hex
        self._dictionary = None
        self._source_name = name
        self._source_path = source_path
        self._source_saver = source_saver
        self._source_prepare = source_prepare
        self._source_root_path = source_root_path
        self._selected_index = -1
        self._channel = "rgba"
        self._mip_level = 0
        self._preview_url = ""
        self._preview_revision = 0
        self._loading = True
        self._preview_loading = False
        self._operation_busy = False
        self._saving = False
        self._undo_stack.clear()
        self._revision = 0
        self._saved_revision = 0
        self._next_revision = 0
        self._error = ""
        self._textures_model.clear()
        self._set_status("Reading texture dictionary…")
        self.stateChanged.emit()
        self.selectionChanged.emit()
        self.openRequested.emit()

        task = _DictionaryTask(generation, bytes(data))
        task.signals.completed.connect(self._dictionary_loaded)
        task.signals.failed.connect(self._dictionary_failed)
        self._thread_pool.start(task, 2)

    @Slot(int)
    def selectTexture(self, row: int) -> None:
        if self._textures_model.record_at(row) is None or row == self._selected_index:
            return
        self._selected_index = row
        self._mip_level = 0
        self._preview_url = ""
        self._preview_loading = True
        self.selectionChanged.emit()
        self.stateChanged.emit()
        self._request_preview()

    @Slot(int)
    def setMipLevel(self, level: int) -> None:
        record = self._selected_record()
        if record is None:
            return
        normalized = max(0, min(int(level), record.mip_count - 1))
        if normalized == self._mip_level:
            return
        self._mip_level = normalized
        self._preview_url = ""
        self._preview_loading = True
        self.selectionChanged.emit()
        self.stateChanged.emit()
        self._request_preview()

    @Slot(str)
    def setChannel(self, mode: str) -> None:
        normalized = mode.casefold()
        if normalized not in _CHANNELS or normalized == self._channel:
            return
        self._channel = normalized
        self._preview_url = ""
        self._preview_loading = self._selected_record() is not None
        self.selectionChanged.emit()
        self.stateChanged.emit()
        self._request_preview()

    @Slot(int, int, int, result=int)
    def estimatedMipCount(self, width: int, height: int, min_mip_size: int) -> int:
        return mip_count_for_dimensions(width, height, min_mip_size)

    @Slot(int, int, int, result=str)
    def estimatedSmallestMipDimensions(
        self,
        width: int,
        height: int,
        min_mip_size: int,
    ) -> str:
        dimensions = mip_dimensions_for_minimum(width, height, min_mip_size)
        if not dimensions:
            return "—"
        mip_width, mip_height = dimensions[-1]
        return f"{mip_width} × {mip_height}"

    @Slot(int, int, str, int, bool, result=bool)
    def resizeSelected(
        self,
        width: int,
        height: int,
        filter_name: str,
        min_mip_size: int,
        generate_mipmaps: bool,
    ) -> bool:
        if not self._valid_dimensions(width, height):
            return False
        transform = partial(
            _resize_texture,
            width=int(width),
            height=int(height),
            mip_filter=_mip_filter(filter_name),
            min_mip_size=texfury_mip_stop_size(
                width,
                height,
                min_mip_size,
            ),
            generate_mipmaps=bool(generate_mipmaps),
        )
        return self._start_transform("Resizing", transform)

    @Slot(str, int, result=bool)
    def recalculateSelectedMipmaps(
        self,
        filter_name: str,
        min_mip_size: int,
    ) -> bool:
        record = self._selected_record()
        if record is None:
            return False
        transform = partial(
            _recalculate_mipmaps,
            mip_filter=_mip_filter(filter_name),
            min_mip_size=texfury_mip_stop_size(
                record.texture.width,
                record.texture.height,
                min_mip_size,
            ),
        )
        return self._start_transform("Recalculating mipmaps for", transform)

    @Slot(str, float, str, int, result=bool)
    def changeSelectedFormat(
        self,
        format_name: str,
        quality: float,
        filter_name: str,
        min_mip_size: int,
    ) -> bool:
        from texfury import BCFormat

        normalized = format_name.upper()
        if normalized not in _EDITABLE_FORMATS:
            self._set_status(f"Unsupported texture format: {format_name}")
            return False
        record = self._selected_record()
        if record is None:
            return False
        transform = partial(
            _change_texture_format,
            format=BCFormat[normalized],
            quality=max(0.0, min(1.0, float(quality))),
            mip_filter=_mip_filter(filter_name),
            min_mip_size=texfury_mip_stop_size(
                record.texture.width,
                record.texture.height,
                min_mip_size,
            ),
        )
        return self._start_transform("Changing format for", transform)

    @Slot(int, result=bool)
    def repairSelectedAlphaEdges(self, radius: int) -> bool:
        transform = partial(
            _repair_alpha_edges,
            radius=max(1, min(32, int(radius))),
        )
        return self._start_transform("Repairing alpha edges for", transform)

    @Slot(result=bool)
    def replaceSelectedFromImage(self) -> bool:
        record = self._selected_record()
        if record is None or self._operation_busy:
            return False
        path, _ = QFileDialog.getOpenFileName(
            None,
            "Replace texture from image",
            "",
            "Texture images (*.dds *.png *.jpg *.jpeg *.bmp *.tga *.webp)",
        )
        if not path:
            return False
        transform = partial(
            _replace_texture_from_image,
            path=Path(path),
            width=record.texture.width,
            height=record.texture.height,
            generate_mipmaps=record.texture.mip_count > 1,
            min_mip_size=max(
                1,
                max(record.texture.width, record.texture.height)
                >> max(0, record.texture.mip_count - 1),
            ),
        )
        return self._start_transform("Replacing", transform)

    @Slot(str, result=bool)
    def renameSelected(self, name: str) -> bool:
        record = self._selected_record()
        normalized = name.strip()
        if record is None or self._operation_busy:
            return False
        if not normalized:
            self._set_status("Texture name cannot be empty")
            return False
        duplicate = any(
            row != self._selected_index
            and candidate.name.casefold() == normalized.casefold()
            for row in range(self._textures_model.rowCount())
            if (candidate := self._textures_model.record_at(row)) is not None
        )
        if duplicate:
            self._set_status(f"A texture named {normalized} already exists")
            return False
        renamed = replace(record.texture, name=normalized)
        if not self._replace_dictionary_texture(self._selected_index, renamed):
            return False
        self._push_undo(
            "Rename texture",
            TextureUndoKind.REPLACE,
            self._selected_index,
            record.texture,
        )
        self._textures_model.replace_texture(self._selected_index, renamed)
        self._set_status(f"Renamed {record.name} to {normalized}")
        self.stateChanged.emit()
        self.selectionChanged.emit()
        return True

    @Slot(result=bool)
    def removeSelected(self) -> bool:
        record = self._selected_record()
        if record is None or self._operation_busy:
            return False
        if self._textures_model.rowCount() <= 1:
            self._set_status("A YTD must contain at least one texture")
            return False
        row = self._selected_index
        textures = list(self._dictionary.textures)
        textures.pop(row)
        if not self._replace_dictionary(textures):
            return False
        self._push_undo(
            "Remove texture",
            TextureUndoKind.INSERT,
            row,
            record.texture,
        )
        self._generation += 1
        self._thread_pool.clear()
        self._textures_model.remove_texture(row)
        self._selected_index = min(row, len(textures) - 1)
        self._mip_level = 0
        self._preview_url = ""
        self._preview_loading = True
        self._set_status(f"Removed {record.name}")
        self.stateChanged.emit()
        self.selectionChanged.emit()
        self._request_preview()
        self._request_all_thumbnails()
        return True

    @Slot(result=bool)
    def undo(self) -> bool:
        if not self._undo_stack or self._operation_busy or self._dictionary is None:
            return False
        action = self._undo_stack.pop()
        textures = list(self._dictionary.textures)
        if action.kind is TextureUndoKind.REPLACE:
            if not 0 <= action.row < len(textures):
                return False
            textures[action.row] = action.texture
        elif action.kind is TextureUndoKind.INSERT:
            textures.insert(action.row, action.texture)
        else:
            return False
        if not self._replace_dictionary(textures):
            self._undo_stack.append(action)
            return False

        self._generation += 1
        self._thread_pool.clear()
        if action.kind is TextureUndoKind.REPLACE:
            self._textures_model.replace_texture(action.row, action.texture)
        else:
            self._textures_model.insert_texture(action.row, action.texture)
        self._revision = action.previous_revision
        self._selected_index = action.row
        self._mip_level = 0
        self._preview_url = ""
        self._preview_loading = True
        self._set_status(f"Undid {action.label.casefold()}")
        self.stateChanged.emit()
        self.selectionChanged.emit()
        self._request_preview()
        if action.kind is TextureUndoKind.REPLACE:
            self._start_image_task(
                action.row,
                action.texture,
                "rgba",
                thumbnail=True,
            )
        else:
            self._request_all_thumbnails()
        return True

    @Slot(result=bool)
    def saveYtd(self) -> bool:
        if self._dictionary is None or self._operation_busy:
            return False
        if self._source_saver is not None:
            if self._source_prepare is not None:
                self._source_prepare()
            action = partial(
                _save_dictionary_to_archive,
                self._dictionary,
                self._source_saver,
            )
            return self._start_save(self._source_name, action)
        if not self.canSaveSource:
            return False
        destination = Path(self._source_path)
        return self._start_save(
            destination.name,
            partial(_save_dictionary_to_path, self._dictionary, destination),
        )

    @Slot(result=bool)
    def saveYtdAs(self) -> bool:
        if self._dictionary is None or self._operation_busy:
            return False
        suggested = self._source_name or "textures.ytd"
        path, _ = QFileDialog.getSaveFileName(
            None,
            "Save texture dictionary",
            suggested,
            "Texture Dictionary (*.ytd)",
        )
        if not path:
            return False
        destination = Path(path)
        if destination.suffix.casefold() != ".ytd":
            destination = destination.with_suffix(".ytd")
        return self._start_save(
            destination.name,
            partial(_save_dictionary_to_path, self._dictionary, destination),
            source_update=(str(destination), destination.name),
        )

    @Slot()
    def extractSelected(self) -> None:
        record = self._selected_record()
        if record is None:
            return
        suggested = f"{record.name}.dds"
        path, _ = QFileDialog.getSaveFileName(
            None,
            "Extract texture",
            suggested,
            "DirectDraw Surface (*.dds)",
        )
        if not path:
            return
        destination = Path(path)
        if destination.suffix.casefold() != ".dds":
            destination = destination.with_suffix(".dds")
        try:
            record.texture.save_dds(destination)
        except (OSError, ValueError, RuntimeError) as error:
            self._set_status(f"Could not extract texture: {error}")
            return
        self._set_status(f"Extracted {record.name}")

    @Slot()
    def extractAll(self) -> None:
        if self._dictionary is None:
            return
        path = QFileDialog.getExistingDirectory(None, "Extract all textures")
        if not path:
            return
        try:
            extracted = self._dictionary.extract(path)
        except (OSError, ValueError, RuntimeError) as error:
            self._set_status(f"Could not extract textures: {error}")
            return
        self._set_status(f"Extracted {len(extracted)} textures")

    def shutdown(self) -> None:
        self._generation += 1
        self._thread_pool.clear()
        if self._session:
            self._image_provider.clear_prefix(f"{self._session}/")

    @Slot(object)
    def _dictionary_loaded(self, payload: object) -> None:
        generation, dictionary = payload
        if generation != self._generation:
            return
        self._dictionary = dictionary
        self._textures_model.set_textures(list(dictionary.textures))
        self._loading = False
        self._error = ""
        if self._textures_model.rowCount() == 0:
            self._set_status("Texture dictionary is empty")
            self.stateChanged.emit()
            return
        self._selected_index = 0
        self._preview_loading = True
        self._set_status(f"Loaded {self._textures_model.rowCount()} textures")
        self.stateChanged.emit()
        self.selectionChanged.emit()
        self._request_preview()
        for row in range(self._textures_model.rowCount()):
            record = self._textures_model.record_at(row)
            if record is not None:
                self._start_image_task(row, record.texture, "rgba", thumbnail=True)

    @Slot(object)
    def _dictionary_failed(self, payload: object) -> None:
        generation, message = payload
        if generation != self._generation:
            return
        self._loading = False
        self._preview_loading = False
        self._error = str(message)
        self._set_status(f"Could not read texture dictionary: {message}")
        self.stateChanged.emit()

    def _request_preview(self) -> None:
        record = self._selected_record()
        if record is None:
            return
        self._set_status(f"Decoding {record.name} · mip {self._mip_level}…")
        self._start_image_task(
            self._selected_index,
            record.texture,
            self._channel,
            self._mip_level,
            thumbnail=False,
        )

    def _start_image_task(
        self,
        row: int,
        texture: Any,
        mode: str,
        mip: int = 0,
        *,
        thumbnail: bool,
    ) -> None:
        task = _ImageTask(self._generation, row, texture, mode, mip, thumbnail)
        task.signals.completed.connect(self._image_loaded)
        task.signals.failed.connect(self._image_failed)
        self._thread_pool.start(task, 0 if thumbnail else 2)

    @Slot(object)
    def _image_loaded(self, payload: object) -> None:
        generation, row, kind, mode, mip, image = payload
        if generation != self._generation:
            return
        if kind == "thumbnail":
            key = f"{self._session}/thumbnail/{row}"
            self._image_provider.put(key, image)
            self._textures_model.set_thumbnail(row, f"image://textureviewer/{key}")
            return
        if (
            row != self._selected_index
            or mode != self._channel
            or mip != self._mip_level
        ):
            return
        self._preview_revision += 1
        self._image_provider.clear_prefix(f"{self._session}/preview/")
        key = (
            f"{self._session}/preview/{row}/{mode}/{mip}/"
            f"{self._preview_revision}"
        )
        self._image_provider.put(key, image)
        self._preview_url = f"image://textureviewer/{key}"
        self._preview_loading = False
        self._set_status(f"Previewing {self.selectedName} · mip {mip}")
        self.stateChanged.emit()
        self.selectionChanged.emit()

    @Slot(object)
    def _image_failed(self, payload: object) -> None:
        generation, row, kind, mode, mip, message = payload
        if generation != self._generation or kind == "thumbnail":
            return
        if (
            row != self._selected_index
            or mode != self._channel
            or mip != self._mip_level
        ):
            return
        self._preview_loading = False
        self._set_status(f"Could not decode {self.selectedName}: {message}")
        self.stateChanged.emit()

    def _start_transform(
        self,
        operation: str,
        transform: Callable[[Any], Any],
    ) -> bool:
        record = self._selected_record()
        if record is None or self._operation_busy:
            return False
        self._operation_busy = True
        self._set_status(f"{operation} {record.name}…")
        self.stateChanged.emit()
        task = _TextureTransformTask(
            self._generation,
            self._selected_index,
            record.texture,
            operation,
            transform,
        )
        task.signals.completed.connect(self._transform_completed)
        task.signals.failed.connect(self._transform_failed)
        self._thread_pool.start(task, 3)
        return True

    @Slot(object)
    def _transform_completed(self, payload: object) -> None:
        generation, row, operation, texture, changed = payload
        if generation != self._generation:
            return
        self._operation_busy = False
        record = self._textures_model.record_at(row)
        if record is None:
            self.stateChanged.emit()
            return
        if not changed:
            self._set_status(f"No change was needed for {record.name}")
            self.stateChanged.emit()
            return
        if not self._replace_dictionary_texture(row, texture):
            self.stateChanged.emit()
            return
        undo_labels = {
            "Resizing": "Resize texture",
            "Recalculating mipmaps for": "Recalculate mipmaps",
            "Changing format for": "Change texture format",
            "Repairing alpha edges for": "Repair alpha edges",
            "Replacing": "Replace texture",
        }
        self._push_undo(
            undo_labels.get(operation, "Texture change"),
            TextureUndoKind.REPLACE,
            row,
            record.texture,
        )
        self._generation += 1
        self._thread_pool.clear()
        self._textures_model.replace_texture(row, texture)
        if row == self._selected_index:
            self._mip_level = 0
            self._preview_url = ""
            self._preview_loading = True
            self.selectionChanged.emit()
            self._request_preview()
        self._start_image_task(row, texture, "rgba", thumbnail=True)
        self._set_status(f"Finished {operation.casefold()} {record.name}")
        self.stateChanged.emit()

    @Slot(object)
    def _transform_failed(self, payload: object) -> None:
        generation, _row, operation, message = payload
        if generation != self._generation:
            return
        self._operation_busy = False
        self._set_status(f"{operation} failed: {message}")
        self.stateChanged.emit()

    def _replace_dictionary_texture(self, row: int, texture: Any) -> bool:
        if self._dictionary is None or not 0 <= row < len(self._dictionary.textures):
            return False
        textures = list(self._dictionary.textures)
        textures[row] = texture
        return self._replace_dictionary(textures)

    def _replace_dictionary(self, textures: list[Any]) -> bool:
        from fivefury.ytd import Ytd

        if self._dictionary is None:
            return False
        candidate = Ytd(textures, game=self._dictionary.game)
        try:
            candidate.validate().raise_for_errors()
        except ValueError as error:
            self._set_status(f"Texture change is invalid: {error}")
            return False
        self._dictionary = candidate
        return True

    def _push_undo(
        self,
        label: str,
        kind: TextureUndoKind,
        row: int,
        texture: Any,
    ) -> None:
        self._undo_stack.append(
            TextureUndoAction(
                label=label,
                kind=kind,
                row=row,
                texture=texture,
                previous_revision=self._revision,
            )
        )
        self._next_revision += 1
        self._revision = self._next_revision
        retained = sum(action.data_size for action in self._undo_stack)
        while len(self._undo_stack) > 1 and (
            len(self._undo_stack) > _UNDO_LIMIT or retained > _UNDO_DATA_LIMIT
        ):
            retained -= self._undo_stack.pop(0).data_size

    def _request_all_thumbnails(self) -> None:
        for row in range(self._textures_model.rowCount()):
            record = self._textures_model.record_at(row)
            if record is not None:
                self._start_image_task(row, record.texture, "rgba", thumbnail=True)

    def _valid_dimensions(self, width: int, height: int) -> bool:
        if width <= 0 or height <= 0:
            self._set_status("Texture dimensions must be positive")
            return False
        if width > self.maximumDimension or height > self.maximumDimension:
            self._set_status(
                f"Texture dimensions cannot exceed {self.maximumDimension}"
            )
            return False
        return True

    def _start_save(
        self,
        label: str,
        save_action: Callable[[], None],
        *,
        source_update: tuple[str, str] | None = None,
    ) -> bool:
        if self._dictionary is None or self._operation_busy:
            return False
        self._operation_busy = True
        self._saving = True
        self._set_status(f"Saving {label}…")
        self.stateChanged.emit()
        task = _SaveTask(
            self._generation,
            label,
            save_action,
            source_update,
        )
        task.signals.completed.connect(self._save_completed)
        task.signals.failed.connect(self._save_failed)
        self._thread_pool.start(task, 4)
        return True

    @Slot(object)
    def _save_completed(self, payload: object) -> None:
        generation, label, source_update = payload
        if generation != self._generation:
            return
        self._operation_busy = False
        self._saving = False
        if source_update is not None:
            self._source_path, self._source_name = source_update
            self._source_saver = None
            self._source_prepare = None
            self._source_root_path = ""
        self._saved_revision = self._revision
        self._set_status(f"Saved {label}")
        self.stateChanged.emit()
        self.saveFinished.emit(True)
        if source_update is None and self._source_root_path:
            self.sourceSaved.emit(self._source_root_path)

    @Slot(object)
    def _save_failed(self, payload: object) -> None:
        generation, label, message = payload
        if generation != self._generation:
            return
        self._operation_busy = False
        self._saving = False
        self._set_status(f"Could not save {label}: {message}")
        self.stateChanged.emit()
        self.saveFinished.emit(False)

    def _selected_record(self) -> TextureRecord | None:
        return self._textures_model.record_at(self._selected_index)

    def _set_status(self, value: str) -> None:
        if value == self._status:
            return
        self._status = value
        self.statusChanged.emit()


__all__ = [
    "TextureImageProvider",
    "TextureListModel",
    "TextureViewerBridge",
    "decode_texture_image",
    "mip_count_for_dimensions",
    "mip_dimensions_for_minimum",
    "texfury_mip_stop_size",
]


def _resize_texture(
    texture: Any,
    *,
    width: int,
    height: int,
    mip_filter: Any,
    min_mip_size: int,
    generate_mipmaps: bool,
) -> Any:
    return texture.resize(
        width,
        height,
        generate_mipmaps=generate_mipmaps,
        min_mip_size=min_mip_size,
        mip_filter=mip_filter,
    )


def _recalculate_mipmaps(
    texture: Any,
    *,
    mip_filter: Any,
    min_mip_size: int,
) -> Any:
    return texture.to_format(
        texture.format,
        generate_mipmaps=True,
        min_mip_size=min_mip_size,
        mip_filter=mip_filter,
    )


def _change_texture_format(
    texture: Any,
    *,
    format: Any,
    quality: float,
    mip_filter: Any,
    min_mip_size: int,
) -> Any:
    return texture.to_format(
        format,
        quality=quality,
        generate_mipmaps=True,
        min_mip_size=min_mip_size,
        mip_filter=mip_filter,
    )


def _repair_alpha_edges(texture: Any, *, radius: int) -> Any:
    return texture.repair_alpha_edges(radius=radius)


def _replace_texture_from_image(
    texture: Any,
    *,
    path: Path,
    width: int,
    height: int,
    generate_mipmaps: bool,
    min_mip_size: int,
) -> Any:
    from texfury import Texture

    if path.suffix.casefold() == ".dds":
        replacement = Texture.from_dds(path, name=texture.name)
        if replacement.width != width or replacement.height != height:
            replacement = replacement.resize(
                width,
                height,
                generate_mipmaps=generate_mipmaps,
                min_mip_size=min_mip_size,
            )
        if replacement.format != texture.format:
            replacement = replacement.to_format(
                texture.format,
                generate_mipmaps=generate_mipmaps,
                min_mip_size=min_mip_size,
            )
        return replacement
    return Texture.from_image(
        path,
        format=texture.format,
        generate_mipmaps=generate_mipmaps,
        min_mip_size=min_mip_size,
        resize=(width, height),
        allow_upscale=True,
        resize_to_pot=False,
        name=texture.name,
    )


def _save_dictionary_to_path(dictionary: Any, destination: Path) -> None:
    dictionary.save(destination)


def _save_dictionary_to_archive(
    dictionary: Any,
    source_saver: Callable[[bytes], None],
) -> None:
    source_saver(dictionary.to_bytes())
