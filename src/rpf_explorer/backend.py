"""FiveFury boundary for GTA V installations and RPF archives."""

from __future__ import annotations

from collections.abc import Callable
from dataclasses import dataclass, field
from pathlib import Path, PurePosixPath
from typing import Any
from zipfile import BadZipFile

from .models import EntryRecord

_GAME_FILE_KIND_LABELS = {
    "YDD": "Drawable Dictionary",
    "YDR": "Drawable",
    "YFT": "Fragment",
    "YMAP": "Map Data",
    "YMF": "Map Manifest",
    "YMT": "Metadata Container",
    "YTD": "Texture Dictionary",
    "YTYP": "Archetype Definitions",
    "YBN": "Collision Bounds",
    "YCD": "Clip Dictionary",
    "YED": "Expression Dictionary",
    "YPT": "Particle Dictionary",
    "YND": "Node Dictionary",
    "YNV": "Navigation Mesh",
    "REL": "Audio Metadata",
    "YWR": "Waypoint Recording",
    "YVR": "Vehicle Recording",
    "GXT2": "Text Table",
    "GTXD": "Texture Parent Data",
    "AWC": "Audio Container",
    "VEHICLES": "Vehicle Definitions",
    "CAR_COLS": "Vehicle Colors",
    "CAR_MOD_COLS": "Vehicle Mod Colors",
    "CAR_VARIATIONS": "Vehicle Variations",
    "VEHICLE_LAYOUTS": "Vehicle Layouts",
    "PEDS": "Ped Definitions",
    "HEIGHTMAP": "Height Map",
    "WATER": "Water Data",
    "CUT": "Cutscene",
    "CDR": "PS3 Drawable",
    "HANDLING": "Vehicle Handling",
    "GTA5_CACHE": "GTA V Cache",
    "EXPRESSION_SETS": "Expression Sets",
    "RPF": "RPF Package",
}

_GENERAL_FILE_KIND_LABELS = {
    ".exe": "Application",
    ".dll": "Application Extension",
    ".asi": "ASI Plugin",
    ".meta": "Metadata",
    ".xml": "XML Document",
    ".json": "JSON Document",
    ".txt": "Text Document",
    ".dat": "Data File",
}

_WINDOWS_RESERVED_NAMES = {
    "aux",
    "con",
    "nul",
    "prn",
    *(f"com{index}" for index in range(1, 10)),
    *(f"lpt{index}" for index in range(1, 10)),
}


@dataclass(frozen=True, slots=True)
class WorkspaceInfo:
    name: str
    source_path: str
    version: str
    platform: str


@dataclass(frozen=True, slots=True)
class ArchiveEntryTarget:
    root_path: Path
    entry_path: str
    crypto: Any
    source_archive: Any = field(repr=False, compare=False)

    def prepare(self) -> None:
        """Release source handles before Windows atomically replaces the RPF."""
        self.source_archive.close()

    def save(self, data: bytes) -> None:
        """Replace one entry and atomically save its root RPF archive."""
        from fivefury import RpfArchive
        from fivefury.rpf.entries import RpfFileEntry

        self.prepare()
        with RpfArchive.from_path(
            self.root_path,
            crypto=self.crypto,
            load_nested=False,
        ) as root:
            entry = root.find_entry(self.entry_path)
            if not isinstance(entry, RpfFileEntry) or entry._archive is None:
                raise ValueError(
                    f"Could not find archive entry: {self.entry_path}"
                )
            entry._archive.file(entry.path, data)
            root.save(self.root_path)


@dataclass(frozen=True, slots=True)
class EntryCreationTarget:
    directory: Path | None = None
    archive_path: Path | None = None
    archive_prefix: str = ""
    internal_directory: str = "."
    crypto: Any = field(default=None, repr=False, compare=False)

    @property
    def in_archive(self) -> bool:
        return self.archive_path is not None


@dataclass(frozen=True, slots=True)
class EntryDeletionTarget:
    location: EntryCreationTarget
    loose_paths: tuple[Path, ...] = ()
    archive_paths: tuple[str, ...] = ()

    @property
    def in_archive(self) -> bool:
        return self.location.in_archive


def create_folder_at(target: EntryCreationTarget, name: str) -> str:
    normalized = normalize_entry_name(name)
    if target.in_archive:
        _mutate_target_archive(
            target,
            normalized,
            lambda archive: archive.directory(
                _archive_child_path(target.internal_directory, normalized)
            ),
        )
    else:
        destination = _loose_destination(target, normalized)
        destination.mkdir()
    return normalized


def create_empty_rpf_at(target: EntryCreationTarget, name: str) -> str:
    from fivefury import RpfArchive

    normalized = normalize_rpf_name(name)
    _store_rpf(target, normalized, RpfArchive.empty(normalized, crypto=target.crypto))
    return normalized


def create_rpf_from_folder_at(
    target: EntryCreationTarget,
    name: str,
    source: str | Path,
) -> str:
    from fivefury import RpfArchive

    source_path = Path(source).expanduser().resolve(strict=True)
    if not source_path.is_dir():
        raise ValueError(f"RPF source folder does not exist: {source_path}")
    normalized = normalize_rpf_name(name)
    archive = RpfArchive.from_folder(source_path, name=normalized)
    _store_rpf(target, normalized, archive)
    return normalized


def create_rpf_from_zip_at(
    target: EntryCreationTarget,
    name: str,
    source: str | Path,
) -> str:
    from fivefury import RpfArchive

    source_path = Path(source).expanduser().resolve(strict=True)
    if not source_path.is_file():
        raise ValueError(f"RPF source ZIP does not exist: {source_path}")
    normalized = normalize_rpf_name(name)
    try:
        archive = RpfArchive.from_zip(source_path, name=normalized)
    except BadZipFile as error:
        raise ValueError(f"Not a valid ZIP archive: {source_path.name}") from error
    _store_rpf(target, normalized, archive)
    return normalized


def delete_archive_files_at(target: EntryDeletionTarget) -> int:
    from fivefury.rpf.entries import RpfFileEntry

    if not target.in_archive or not target.archive_paths:
        raise ValueError("No RPF files were selected for deletion")

    def remove_files(archive: Any) -> None:
        entries: list[RpfFileEntry] = []
        for path in target.archive_paths:
            entry = _archive_file_at(archive, path)
            if not isinstance(entry, RpfFileEntry) or entry.parent is None:
                raise ValueError(f"Could not find archive file: {path}")
            entries.append(entry)
        for entry in entries:
            entry.parent.remove_file(entry)

    _save_target_archive(target.location, remove_files)
    return len(target.archive_paths)


def normalize_entry_name(name: str) -> str:
    normalized = name.strip()
    invalid = '<>:"/\\|?*'
    if (
        not normalized
        or normalized in {".", ".."}
        or normalized.endswith((" ", "."))
        or normalized.partition(".")[0].casefold() in _WINDOWS_RESERVED_NAMES
        or any(character in invalid or ord(character) < 32 for character in normalized)
    ):
        raise ValueError("Enter a valid Windows file name")
    return normalized


def normalize_rpf_name(name: str) -> str:
    normalized = normalize_entry_name(name)
    if normalized.casefold().endswith(".rpf"):
        return normalized
    return f"{normalized}.rpf"


def _archive_child_path(directory: str, name: str) -> str:
    return name if directory == "." else f"{directory}/{name}"


def _normalized_directory(path: str) -> str:
    return "." if not path or path == "/" else path.strip("/")


def _loose_destination(target: EntryCreationTarget, name: str) -> Path:
    if target.directory is None:
        raise ValueError("No writable explorer location is open")
    destination = target.directory / name
    if destination.exists():
        raise FileExistsError(f"An entry named {name} already exists")
    return destination


def _store_rpf(
    target: EntryCreationTarget,
    name: str,
    archive: Any,
) -> None:
    if not target.in_archive:
        archive.save(_loose_destination(target, name))
        return
    data = archive.to_bytes()
    _mutate_target_archive(
        target,
        name,
        lambda owner: owner.file(
            _archive_child_path(target.internal_directory, name),
            data,
        ),
    )


def _mutate_target_archive(
    target: EntryCreationTarget,
    name: str,
    mutation: Callable[[Any], None],
) -> None:
    from fivefury.rpf.entries import RpfDirectoryEntry

    def create_entry(archive: Any) -> None:
        directory = _archive_directory_at(archive, target.internal_directory)
        if not isinstance(directory, RpfDirectoryEntry):
            raise ValueError(
                f"Could not find archive folder: {target.internal_directory}"
            )
        if any(
            entry.name.casefold() == name.casefold()
            for entry in (*directory.directories, *directory.files)
        ):
            raise FileExistsError(f"An entry named {name} already exists")
        mutation(archive)

    _save_target_archive(target, create_entry)


def _save_target_archive(
    target: EntryCreationTarget,
    mutation: Callable[[Any], None],
) -> None:
    from fivefury import RpfArchive
    from fivefury.rpf.entries import RpfFileEntry

    if target.archive_path is None:
        raise ValueError("No writable RPF archive is open")
    with RpfArchive.from_path(
        target.archive_path,
        crypto=target.crypto,
        load_nested=False,
    ) as root:
        archive = root
        if target.archive_prefix:
            entry = root.find_entry(target.archive_prefix)
            if not isinstance(entry, RpfFileEntry) or entry._archive is None:
                raise ValueError(
                    f"Could not find nested archive: {target.archive_prefix}"
                )
            archive = entry._archive.load_nested_archive(
                entry,
                recursive=False,
                strict=True,
            )
            if archive is None:
                raise ValueError(
                    f"Could not open nested archive: {target.archive_prefix}"
                )
        mutation(archive)
        root.save(target.archive_path)


def _archive_directory_at(archive: Any, path: str) -> Any:
    normalized = _normalized_directory(path)
    directory = archive.root
    if normalized == ".":
        return directory
    for segment in normalized.split("/"):
        directory = directory.find_directory(segment)
        if directory is None:
            return None
    return directory


def _archive_file_at(archive: Any, path: str) -> Any:
    normalized = _normalized_directory(path)
    parent_path, separator, name = normalized.rpartition("/")
    directory = _archive_directory_at(
        archive,
        parent_path if separator else ".",
    )
    return directory.find_file(name if separator else normalized) if directory else None


class RpfProvider:
    """Owns the selected game root and lazily opened FiveFury archives."""

    def __init__(self) -> None:
        self._game_root: Path | None = None
        self._game_target = ""
        self._archive: Any = None
        self._archive_path = ""
        self._archive_game_path = ""
        self._archive_is_nested = False
        self._in_archive = False
        self._crypto: Any = None

    @property
    def info(self) -> WorkspaceInfo:
        if self.in_archive:
            return WorkspaceInfo(
                self._archive.name,
                self._archive_path,
                str(getattr(self._archive, "version", "—")),
                str(getattr(getattr(self._archive, "platform", None), "value", "PC")).upper(),
            )
        if self._game_root is not None:
            edition = "GTA V Enhanced" if self._game_target == "gta5_enhanced" else "GTA V Legacy"
            return WorkspaceInfo(self._game_root.name, str(self._game_root), edition, "PC")
        return WorkspaceInfo("No game", "", "—", "PC")

    @property
    def is_open(self) -> bool:
        return self._game_root is not None or self._archive is not None

    @property
    def has_game(self) -> bool:
        return self._game_root is not None

    @property
    def has_archive(self) -> bool:
        return self._archive is not None

    @property
    def in_archive(self) -> bool:
        return self._in_archive and self._archive is not None

    @property
    def game_name(self) -> str:
        return self._game_root.name if self._game_root is not None else "No game"

    @property
    def game_path(self) -> str:
        return str(self._game_root) if self._game_root is not None else ""

    @property
    def archive_name(self) -> str:
        return self._archive.name if self._archive is not None else ""

    @property
    def archive_game_path(self) -> str:
        return self._archive_game_path

    @property
    def archive_is_nested(self) -> bool:
        return self._archive_is_nested

    @property
    def archive_prefix(self) -> str:
        return str(getattr(self._archive, "prefix", "")) if self._archive is not None else ""

    @property
    def archive_tree_id(self) -> str:
        root = self._root_archive()
        if root is None:
            return ""
        return self._archive_game_path or str(root.source_path or root.name)

    @property
    def root_archive_name(self) -> str:
        root = self._root_archive()
        return str(root.name) if root is not None else ""

    @property
    def archive_chain_prefixes(self) -> tuple[str, ...]:
        if self._archive is None:
            return ()
        chain: list[str] = []
        archive = self._archive
        while archive is not None:
            chain.append(str(archive.prefix))
            archive = archive.parent
        return tuple(reversed(chain))

    def open_game(self, path: str | Path) -> WorkspaceInfo:
        from fivefury import GameTarget, load_game_keys

        root = Path(path).expanduser().resolve()
        if not root.is_dir():
            raise ValueError(f"Game folder does not exist: {root}")
        enhanced_exe = root / "GTA5_Enhanced.exe"
        legacy_exe = root / "GTA5.exe"
        if enhanced_exe.is_file():
            target = GameTarget.GTA5_ENHANCED
            executable = enhanced_exe
        elif legacy_exe.is_file():
            target = GameTarget.GTA5
            executable = legacy_exe
        else:
            raise ValueError("Select the GTA V folder containing GTA5.exe or GTA5_Enhanced.exe")

        crypto = load_game_keys(executable, gen9=target is GameTarget.GTA5_ENHANCED)
        self.close()
        self._game_root = root
        self._game_target = target.value
        self._crypto = crypto
        self._in_archive = False
        return self.info

    def open_archive(self, path: str | Path) -> WorkspaceInfo:
        from fivefury import RpfArchive, ensure_game_crypto

        resolved = Path(path).expanduser().resolve()
        if not resolved.is_file():
            raise ValueError(f"Archive does not exist: {resolved}")
        if resolved.suffix.casefold() != ".rpf":
            raise ValueError("HikariHopper can only open .rpf archives")
        archive = RpfArchive.from_path(
            resolved,
            crypto=self._crypto or ensure_game_crypto(),
            load_nested=False,
        )
        self._close_archive()
        self._archive = archive
        self._archive_path = str(resolved)
        self._archive_game_path = self._relative_game_path(resolved)
        self._archive_is_nested = False
        self._in_archive = True
        return self.info

    def show_game(self) -> WorkspaceInfo:
        if self._game_root is None:
            raise ValueError("No GTA V installation is loaded")
        self._in_archive = False
        return self.info

    def show_archive(self) -> WorkspaceInfo:
        if self._archive is None:
            raise ValueError("No RPF archive is open")
        self._in_archive = True
        return self.info

    def show_root_archive(self) -> WorkspaceInfo:
        root = self._root_archive()
        if root is None:
            raise ValueError("No RPF archive is open")
        self._archive = root
        self._archive_path = str(root.source_path or root.name)
        self._archive_is_nested = False
        self._in_archive = True
        return self.info

    def close_archive(self) -> None:
        self._close_archive()
        self._in_archive = False

    def close(self) -> None:
        self._close_archive()
        self._game_root = None
        self._game_target = ""
        self._crypto = None
        self._in_archive = False

    def entries(self, directory_path: str = ".") -> list[EntryRecord]:
        if self.in_archive:
            return self.archive_entries(directory_path)
        return self.game_entries(directory_path)

    def creation_target(self, directory_path: str = ".") -> EntryCreationTarget:
        normalized = _normalized_directory(directory_path)
        if self.in_archive:
            directory = self._archive_directory(self._archive, normalized)
            if directory is None:
                raise ValueError(f"Archive folder does not exist: {normalized}")
            root = self._root_archive()
            root_path = Path(root.source_path).expanduser().resolve()
            if not root_path.is_file():
                raise ValueError("The root RPF archive is no longer available")
            target = EntryCreationTarget(
                archive_path=root_path,
                archive_prefix=self.archive_prefix,
                internal_directory=normalized,
                crypto=root.crypto,
            )
            root.close()
            return target

        if self._game_root is None:
            raise ValueError("No writable explorer location is open")
        root = self._game_root.resolve()
        directory = root if normalized == "." else (root / normalized).resolve()
        if directory != root and root not in directory.parents:
            raise ValueError(f"Folder is outside the game installation: {normalized}")
        if not directory.is_dir():
            raise ValueError(f"Game folder does not exist: {normalized}")
        return EntryCreationTarget(directory=directory, crypto=self._crypto)

    def deletion_target(
        self,
        entries: list[EntryRecord],
        directory_path: str = ".",
    ) -> EntryDeletionTarget:
        if not entries:
            raise ValueError("No files are selected")
        if any(entry.is_directory for entry in entries):
            raise ValueError("Folder deletion is not supported")

        location = self.creation_target(directory_path)
        if location.in_archive:
            if any(
                getattr(entry.native_entry, "_archive", None) is not self._archive
                for entry in entries
            ):
                raise ValueError("A selected file is outside the active RPF")
            return EntryDeletionTarget(
                location=location,
                archive_paths=tuple(self.entry_local_path(entry) for entry in entries),
            )
        loose_paths = tuple(self._game_entry_path(entry) for entry in entries)
        open_archive = self._root_archive()
        if open_archive is not None:
            source_path = Path(open_archive.source_path).expanduser().resolve()
            if source_path in loose_paths:
                self._close_archive()
        return EntryDeletionTarget(location=location, loose_paths=loose_paths)

    def export_entries(
        self,
        entries: list[EntryRecord],
        output_dir: str | Path,
    ) -> list[Path]:
        """Materialize entries for a native file drag using standalone RPF bytes."""
        if not self.in_archive:
            return [self._game_entry_path(entry) for entry in entries]

        root = Path(output_dir).resolve()
        root.mkdir(parents=True, exist_ok=True)
        exported: list[Path] = []
        for entry in entries:
            destination = self._safe_export_child(root, entry.name)
            self._export_archive_entry(entry, destination)
            exported.append(destination)
        return exported

    def read_entry_bytes(self, entry: EntryRecord) -> bytes:
        """Read one loose or archived file as a standalone byte stream."""
        if entry.is_directory:
            raise ValueError(f"Cannot read a directory as a file: {entry.name}")
        if self.in_archive:
            if entry.native_entry is None:
                raise ValueError(f"Archive entry is unavailable: {entry.name}")
            return bytes(entry.native_entry.read_standalone())
        return self._game_entry_path(entry).read_bytes()

    def entry_source_path(self, entry: EntryRecord) -> str:
        """Return the stable source notation shown by secondary viewers."""
        if self.in_archive:
            archive = self._archive_game_path or self._archive_path
            return f"{archive}::{entry.path}"
        return str(self._game_entry_path(entry))

    def archive_entry_target(self, entry: EntryRecord) -> ArchiveEntryTarget:
        if not self.in_archive or entry.native_entry is None:
            raise ValueError("The selected entry is not inside an RPF archive")
        archive = entry.native_entry._archive
        if archive is None:
            raise ValueError("The selected archive entry is detached")
        while archive.parent is not None:
            archive = archive.parent
        root_path = Path(archive.source_path).expanduser().resolve()
        if not root_path.is_file():
            raise ValueError("The root RPF archive is no longer available")
        return ArchiveEntryTarget(
            root_path=root_path,
            entry_path=str(entry.native_entry.full_path),
            crypto=archive.crypto,
            source_archive=archive,
        )

    def reload_archive(self, root_path: str | Path) -> bool:
        if self._archive is None:
            return False
        current = self._archive
        root = current
        while root.parent is not None:
            root = root.parent
        expected = Path(root.source_path).expanduser().resolve()
        requested = Path(root_path).expanduser().resolve()
        if expected != requested:
            return False

        from fivefury import RpfArchive
        from fivefury.rpf.entries import RpfFileEntry

        current_prefix = str(current.prefix)
        was_in_archive = self._in_archive
        root.close()
        reloaded_root = RpfArchive.from_path(
            requested,
            crypto=self._crypto,
            load_nested=False,
        )
        reloaded = reloaded_root
        if current_prefix:
            nested_entry = reloaded_root.find_entry(current_prefix)
            if not isinstance(nested_entry, RpfFileEntry):
                reloaded_root.close()
                raise ValueError(
                    f"Could not reopen nested archive: {current_prefix}"
                )
            owner = nested_entry._archive
            if owner is None:
                reloaded_root.close()
                raise ValueError(
                    f"Nested archive is detached: {current_prefix}"
                )
            nested = owner.load_nested_archive(
                nested_entry,
                recursive=False,
                strict=True,
            )
            if nested is None:
                reloaded_root.close()
                raise ValueError(
                    f"Could not reopen nested archive: {current_prefix}"
                )
            reloaded = nested

        self._archive = reloaded
        self._archive_path = str(reloaded.source_path or reloaded.name)
        self._archive_is_nested = reloaded.parent is not None
        self._in_archive = was_in_archive
        return True

    def open_nested(self, entry: EntryRecord) -> WorkspaceInfo:
        if self._archive is None or entry.native_entry is None:
            raise ValueError("The selected entry does not contain a nested archive")
        owner = getattr(entry.native_entry, "_archive", None)
        if owner is None or self._root_archive(owner) is not self._root_archive():
            raise ValueError("The selected entry does not belong to the open RPF")
        nested = owner.load_nested_archive(
            entry.native_entry,
            recursive=False,
            strict=True,
        )
        if nested is None:
            raise ValueError(f"FiveFury could not decode {entry.name} as a nested RPF archive")
        self._archive = nested
        self._archive_path = entry.path
        self._archive_is_nested = True
        self._in_archive = True
        return self.info

    def archive_entry(self, path: str) -> EntryRecord | None:
        normalized = _normalized_directory(path)
        parent = PurePosixPath(normalized).parent.as_posix()
        for entry in self.archive_entries(parent):
            if entry.path.replace("\\", "/") == normalized:
                return entry
        return None

    def archive_entry_from_root(self, path: str) -> EntryRecord | None:
        root = self._root_archive()
        if root is None:
            return None
        entry = root.find_entry(path)
        return self._archive_record(entry) if entry is not None else None

    def archive_entries_at(
        self,
        archive_prefix: str,
        directory_path: str = ".",
    ) -> list[EntryRecord]:
        archive = self._archive_by_prefix(archive_prefix)
        if archive is None:
            return []
        return self._archive_records(archive, directory_path)

    def archive_has_tree_children(
        self,
        archive_prefix: str,
        directory_path: str = ".",
    ) -> bool:
        return any(
            entry.is_directory or entry.name.casefold().endswith(".rpf")
            for entry in self.archive_entries_at(archive_prefix, directory_path)
        )

    def activate_entry_archive(self, entry: EntryRecord) -> WorkspaceInfo:
        native_entry = entry.native_entry
        archive = getattr(native_entry, "_archive", None)
        if archive is None or self._root_archive(archive) is not self._root_archive():
            raise ValueError("The selected entry does not belong to the open RPF")
        self._archive = archive
        self._archive_path = str(archive.source_path or archive.name)
        self._archive_is_nested = archive.parent is not None
        self._in_archive = True
        return self.info

    @staticmethod
    def entry_archive_prefix(entry: EntryRecord) -> str:
        archive = getattr(entry.native_entry, "_archive", None)
        return str(getattr(archive, "prefix", ""))

    @staticmethod
    def entry_local_path(entry: EntryRecord) -> str:
        return str(getattr(entry.native_entry, "path", entry.path))

    @staticmethod
    def loaded_nested_prefix(entry: EntryRecord) -> str:
        child = getattr(entry.native_entry, "child_archive", None)
        return str(getattr(child, "prefix", "")) if child is not None else ""

    def game_entries(self, directory_path: str) -> list[EntryRecord]:
        if self._game_root is None:
            return []
        relative = _normalized_directory(directory_path)
        directory = self._game_root if relative == "." else self._game_root / relative
        if not directory.is_dir():
            return []
        records: list[EntryRecord] = []
        try:
            children = sorted(directory.iterdir(), key=lambda item: (not item.is_dir(), item.name.casefold()))
        except OSError:
            return []
        for item in children:
            try:
                relative_path = item.relative_to(self._game_root).as_posix()
                if item.is_dir():
                    total_children, navigable_children = self._child_counts(item)
                    records.append(
                        EntryRecord(
                            name=item.name,
                            path=relative_path,
                            kind="Folder",
                            size=0,
                            children=total_children,
                            native_entry=item,
                            navigable_children=navigable_children,
                        )
                    )
                elif item.is_file():
                    records.append(
                        EntryRecord(
                            name=item.name,
                            path=relative_path,
                            kind=self._file_kind(item.name),
                            size=item.stat().st_size,
                            native_entry=item,
                        )
                    )
            except OSError:
                continue
        return records

    def archive_entries(self, directory_path: str) -> list[EntryRecord]:
        if self._archive is None:
            return []
        return self._archive_records(self._archive, directory_path)

    def _archive_records(self, archive: Any, directory_path: str) -> list[EntryRecord]:
        directory = self._archive_directory(archive, directory_path)
        if directory is None:
            return []
        records: list[EntryRecord] = []
        for item in directory.directories:
            records.append(self._archive_record(item))
        for item in directory.files:
            records.append(self._archive_record(item))
        return records

    def _archive_record(self, item: Any) -> EntryRecord:
        if item.is_directory:
            navigable_children = len(item.directories) + sum(
                child.name.casefold().endswith(".rpf") for child in item.files
            )
            return EntryRecord(
                name=item.name,
                path=str(item.full_path),
                kind="Folder",
                size=0,
                children=len(item.directories) + len(item.files),
                native_entry=item,
                navigable_children=navigable_children,
            )
        size = int(
            getattr(item, "file_uncompressed_size", 0)
            or getattr(item, "file_size", 0)
        )
        return EntryRecord(
            name=item.name,
            path=str(item.full_path),
            kind=self._file_kind(item.name),
            size=size,
            native_entry=item,
        )

    def _export_archive_entry(self, entry: EntryRecord, destination: Path) -> None:
        if entry.is_directory:
            destination.mkdir(parents=True, exist_ok=True)
            for child in self.archive_entries(entry.path):
                child_destination = self._safe_export_child(destination, child.name)
                self._export_archive_entry(child, child_destination)
            return
        if entry.native_entry is None:
            raise ValueError(f"Archive entry is unavailable: {entry.name}")
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_bytes(entry.native_entry.read_standalone())

    def _game_entry_path(self, entry: EntryRecord) -> Path:
        if self._game_root is None:
            raise ValueError("No GTA V installation is loaded")
        candidate = (self._game_root / entry.path).resolve()
        root = self._game_root.resolve()
        if candidate != root and root not in candidate.parents:
            raise ValueError(f"Entry is outside the game folder: {entry.path}")
        if not candidate.exists():
            raise ValueError(f"Entry no longer exists: {entry.path}")
        return candidate

    @staticmethod
    def _safe_export_child(parent: Path, name: str) -> Path:
        if not name or name in {".", ".."} or "/" in name or "\\" in name:
            raise ValueError(f"Unsafe archive entry name: {name!r}")
        return parent / name

    def _directory(self, path: str) -> Any:
        return self._archive_directory(self._archive, path)

    def _archive_directory(self, archive: Any, path: str) -> Any:
        if archive is None:
            return None
        normalized = _normalized_directory(path)
        if normalized == ".":
            return archive.root
        try:
            return archive.directory(normalized)
        except (KeyError, ValueError, LookupError):
            return None

    def _archive_by_prefix(self, prefix: str) -> Any:
        root = self._root_archive()
        if root is None:
            return None
        pending = [root]
        while pending:
            archive = pending.pop()
            if str(archive.prefix) == prefix:
                return archive
            pending.extend(reversed(archive.children))
        return None

    def _root_archive(self, archive: Any = None) -> Any:
        root = self._archive if archive is None else archive
        while root is not None and root.parent is not None:
            root = root.parent
        return root

    def _close_archive(self) -> None:
        if self._archive is not None:
            archive = self._archive
            while archive.parent is not None:
                archive = archive.parent
            archive.close()
        self._archive = None
        self._archive_path = ""
        self._archive_game_path = ""
        self._archive_is_nested = False

    def _relative_game_path(self, path: Path) -> str:
        if self._game_root is None:
            return ""
        try:
            return path.relative_to(self._game_root).as_posix()
        except ValueError:
            return ""

    @staticmethod
    def _child_counts(directory: Path) -> tuple[int, int]:
        try:
            total = 0
            navigable = 0
            for item in directory.iterdir():
                total += 1
                if item.is_dir() or item.name.casefold().endswith(".rpf"):
                    navigable += 1
            return total, navigable
        except OSError:
            return 0, 0

    @staticmethod
    def _file_kind(name: str) -> str:
        from fivefury.gamefile import guess_game_file_type

        game_file_type = guess_game_file_type(name)
        game_file_kind = _GAME_FILE_KIND_LABELS.get(getattr(game_file_type, "name", ""))
        if game_file_kind is not None:
            return game_file_kind
        return _GENERAL_FILE_KIND_LABELS.get(Path(name).suffix.casefold(), "File")
