from PySide6.QtCore import Qt
from PySide6.QtTest import QSignalSpy

from rpf_explorer.backend import RpfProvider
from rpf_explorer.bridge import ExplorerBridge, TreeListModel, TreeRecord
from rpf_explorer.tabs import ExplorerTabs


def test_bridge_starts_without_demo_content() -> None:
    bridge = ExplorerBridge()

    assert not bridge.hasWorkspace
    assert not bridge.hasGame
    assert not bridge.hasArchive
    assert bridge.archiveName == "No game"
    assert bridge.visibleCount == 0
    assert bridge.treeModel.rowCount() == 0
    assert bridge.selectedIndex == -1
    assert bridge.selectionCount == 0


def test_entry_selection_supports_control_shift_and_select_all(tmp_path, monkeypatch) -> None:
    import fivefury

    monkeypatch.setattr(fivefury, "load_game_keys", lambda *args, **kwargs: None)
    (tmp_path / "GTA5.exe").write_bytes(b"test executable")
    for name in ("a.txt", "b.txt", "c.txt", "d.txt"):
        (tmp_path / name).write_text(name, encoding="utf-8")

    bridge = ExplorerBridge()
    bridge.openGame(str(tmp_path))
    bridge.selectEntry(1)
    bridge.selectEntry(3, Qt.KeyboardModifier.ShiftModifier.value)

    assert bridge.selectionCount == 3
    assert bridge.selectedIndex == 3
    assert bridge.selectedName == "3 items"
    assert bridge.entriesModel.data(
        bridge.entriesModel.index(2, 0), bridge.entriesModel.SELECTED
    )

    bridge.selectEntry(2, Qt.KeyboardModifier.ControlModifier.value)
    assert bridge.selectionCount == 2
    assert not bridge.entriesModel.data(
        bridge.entriesModel.index(2, 0), bridge.entriesModel.SELECTED
    )

    bridge.selectAllEntries()
    assert bridge.selectionCount == bridge.visibleCount


def test_entry_headers_sort_by_name_type_and_numeric_size(tmp_path, monkeypatch) -> None:
    import fivefury

    monkeypatch.setattr(fivefury, "load_game_keys", lambda *args, **kwargs: None)
    (tmp_path / "GTA5.exe").write_bytes(b"game")
    (tmp_path / "z_folder").mkdir()
    (tmp_path / "a_folder").mkdir()
    (tmp_path / "small.ydr").write_bytes(b"x")
    (tmp_path / "large.ytd").write_bytes(b"x" * 100)
    (tmp_path / "medium.txt").write_bytes(b"x" * 10)

    bridge = ExplorerBridge()
    bridge.openGame(str(tmp_path))

    bridge.sortEntries("name")
    names = [
        bridge.entriesModel.entry_at(row).name
        for row in range(bridge.entriesModel.rowCount())
    ]
    assert names[:2] == ["z_folder", "a_folder"]

    bridge.sortEntries("size")
    files = [
        bridge.entriesModel.entry_at(row)
        for row in range(bridge.entriesModel.rowCount())
        if not bridge.entriesModel.entry_at(row).is_directory
    ]
    assert [entry.size for entry in files] == sorted(entry.size for entry in files)

    bridge.sortEntries("size")
    files = [
        bridge.entriesModel.entry_at(row)
        for row in range(bridge.entriesModel.rowCount())
        if not bridge.entriesModel.entry_at(row).is_directory
    ]
    assert [entry.size for entry in files] == sorted(
        (entry.size for entry in files), reverse=True
    )

    bridge.sortEntries("type")
    files = [
        bridge.entriesModel.entry_at(row)
        for row in range(bridge.entriesModel.rowCount())
        if not bridge.entriesModel.entry_at(row).is_directory
    ]
    assert [entry.kind.casefold() for entry in files] == sorted(
        entry.kind.casefold() for entry in files
    )


def test_entry_sorting_preserves_selection(tmp_path, monkeypatch) -> None:
    import fivefury

    monkeypatch.setattr(fivefury, "load_game_keys", lambda *args, **kwargs: None)
    (tmp_path / "GTA5.exe").write_bytes(b"game")
    (tmp_path / "small.txt").write_bytes(b"x")
    (tmp_path / "large.txt").write_bytes(b"x" * 100)

    bridge = ExplorerBridge()
    bridge.openGame(str(tmp_path))
    selected_row = next(
        row
        for row in range(bridge.entriesModel.rowCount())
        if bridge.entriesModel.entry_at(row).name == "small.txt"
    )
    bridge.selectEntry(selected_row)

    bridge.sortEntries("size")

    assert bridge.selectedName == "small.txt"
    assert bridge.selectionCount == 1
    assert bridge.entriesModel.entry_at(bridge.selectedIndex).name == "small.txt"
    assert bridge.entriesModel.data(
        bridge.entriesModel.index(bridge.selectedIndex, 0),
        bridge.entriesModel.SELECTED,
    )


def test_marquee_selection_replaces_adds_and_toggles(tmp_path, monkeypatch) -> None:
    import fivefury

    monkeypatch.setattr(fivefury, "load_game_keys", lambda *args, **kwargs: None)
    (tmp_path / "GTA5.exe").write_bytes(b"test executable")
    for name in ("a.txt", "b.txt", "c.txt", "d.txt"):
        (tmp_path / name).write_text(name, encoding="utf-8")

    bridge = ExplorerBridge()
    bridge.openGame(str(tmp_path))
    bridge.beginMarqueeSelection(Qt.KeyboardModifier.NoModifier.value)
    bridge.updateMarqueeSelection(1, 3)
    bridge.updateMarqueeSelection(2, 3)
    bridge.endMarqueeSelection()
    assert bridge.selectionCount == 2

    bridge.beginMarqueeSelection(Qt.KeyboardModifier.ShiftModifier.value)
    bridge.updateMarqueeSelection(0, 1)
    bridge.endMarqueeSelection()
    assert bridge.selectionCount == 4

    bridge.beginMarqueeSelection(Qt.KeyboardModifier.ControlModifier.value)
    bridge.updateMarqueeSelection(1, 2)
    bridge.endMarqueeSelection()
    assert bridge.selectionCount == 2
    assert bridge.entriesModel.data(
        bridge.entriesModel.index(0, 0), bridge.entriesModel.SELECTED
    )
    assert bridge.entriesModel.data(
        bridge.entriesModel.index(3, 0), bridge.entriesModel.SELECTED
    )


def test_archive_selection_exports_standalone_files_and_folders(tmp_path) -> None:
    from fivefury import RpfArchive

    archive_path = tmp_path / "sample.rpf"
    archive = RpfArchive.empty(archive_path.name)
    archive.file("data/root.txt", b"root")
    archive.file("data/nested/child.txt", b"child")
    archive.save(archive_path)

    provider = RpfProvider()
    provider.open_archive(archive_path)
    entry = provider.archive_entries(".")[0]
    exported = provider.export_entries([entry], tmp_path / "export")

    assert exported == [tmp_path / "export" / "data"]
    assert (exported[0] / "root.txt").read_bytes() == b"root"
    assert (exported[0] / "nested" / "child.txt").read_bytes() == b"child"


def test_archive_entry_target_replaces_ytd_in_root_archive(tmp_path) -> None:
    from fivefury import RpfArchive
    from fivefury.ytd import Texture, TextureFormat, Ytd, read_ytd

    original = Texture.from_raw(
        bytes([0, 0, 255, 255]) * 4,
        2,
        2,
        TextureFormat.A8R8G8B8,
        1,
        name="original",
    )
    replacement = Texture.from_raw(
        bytes([0, 255, 0, 255]) * 4,
        2,
        2,
        TextureFormat.A8R8G8B8,
        1,
        name="replacement",
    )
    archive_path = tmp_path / "textures.rpf"
    archive = RpfArchive.empty(archive_path.name)
    archive.file("assets/test.ytd", Ytd([original]).to_bytes())
    archive.save(archive_path)

    provider = RpfProvider()
    provider.open_archive(archive_path)
    entry = provider.archive_entries("assets")[0]
    target = provider.archive_entry_target(entry)

    target.save(Ytd([replacement]).to_bytes())
    assert provider.reload_archive(archive_path)

    with RpfArchive.from_path(archive_path) as saved:
        saved_entry = saved.find_entry("assets/test.ytd")
        assert saved_entry is not None
        assert read_ytd(saved_entry.read_standalone()).names() == ["replacement"]


def test_archive_entry_target_replaces_ytd_in_nested_archive(tmp_path) -> None:
    from fivefury import RpfArchive
    from fivefury.ytd import Texture, TextureFormat, Ytd, read_ytd

    original = Texture.from_raw(
        bytes([0, 0, 255, 255]) * 4,
        2,
        2,
        TextureFormat.A8R8G8B8,
        1,
        name="original",
    )
    replacement = Texture.from_raw(
        bytes([255, 0, 0, 255]) * 4,
        2,
        2,
        TextureFormat.A8R8G8B8,
        1,
        name="nested_replacement",
    )
    nested = RpfArchive.empty("nested.rpf")
    nested.file("test.ytd", Ytd([original]).to_bytes())
    outer_path = tmp_path / "outer.rpf"
    outer = RpfArchive.empty(outer_path.name)
    outer.file("nested.rpf", nested.to_bytes())
    outer.save(outer_path)

    provider = RpfProvider()
    provider.open_archive(outer_path)
    nested_entry = provider.archive_entries(".")[0]
    provider.open_nested(nested_entry)
    ytd_entry = provider.archive_entries(".")[0]
    target = provider.archive_entry_target(ytd_entry)

    target.save(Ytd([replacement]).to_bytes())
    assert provider.reload_archive(outer_path)
    assert provider.archive_name == "nested.rpf"

    with RpfArchive.from_path(outer_path) as saved:
        saved_entry = saved.find_entry("nested.rpf/test.ytd")
        assert saved_entry is not None
        assert read_ytd(saved_entry.read_standalone()).names() == [
            "nested_replacement"
        ]


def test_tree_selection_updates_without_resetting_the_model() -> None:
    model = TreeListModel()
    model.set_rows([TreeRecord("root", "game://.", 0, "root", expanded=True)])
    resets = QSignalSpy(model.modelReset)

    model.set_rows(
        [TreeRecord("root", "game://.", 0, "root", expanded=True, selected=True)]
    )

    assert resets.count() == 0


def test_tree_expansion_inserts_rows_without_resetting_the_model() -> None:
    model = TreeListModel()
    root = TreeRecord("root", "game://.", 0, "root", expanded=False)
    sibling = TreeRecord("sibling", "game://sibling", 1, "folder")
    model.set_rows([root, sibling])
    resets = QSignalSpy(model.modelReset)
    insertions = QSignalSpy(model.rowsInserted)
    removals = QSignalSpy(model.rowsRemoved)

    expanded_root = TreeRecord("root", "game://.", 0, "root", expanded=True)
    child = TreeRecord("child", "game://child", 1, "folder")
    model.set_rows([expanded_root, child, sibling])

    assert resets.count() == 0
    assert insertions.count() == 1
    assert model.row_at(1) == child

    model.set_rows([root, sibling])

    assert resets.count() == 0
    assert removals.count() == 1
    assert model.rowCount() == 2


def test_tree_click_navigates_without_expanding_folder(tmp_path, monkeypatch) -> None:
    import fivefury

    monkeypatch.setattr(fivefury, "load_game_keys", lambda *args, **kwargs: None)
    (tmp_path / "GTA5.exe").write_bytes(b"test executable")
    (tmp_path / "mods" / "nested").mkdir(parents=True)

    bridge = ExplorerBridge()
    bridge.openGame(str(tmp_path))
    bridge.navigateTree("game://mods")

    mods = bridge.treeModel.row_at(1)
    assert mods is not None
    assert mods.selected
    assert not mods.expanded
    assert bridge.treeModel.rowCount() == 2


def test_tree_row_activation_navigates_and_expands_folder(tmp_path, monkeypatch) -> None:
    import fivefury

    monkeypatch.setattr(fivefury, "load_game_keys", lambda *args, **kwargs: None)
    (tmp_path / "GTA5.exe").write_bytes(b"test executable")
    (tmp_path / "mods" / "nested").mkdir(parents=True)

    bridge = ExplorerBridge()
    bridge.openGame(str(tmp_path))
    bridge.activateTreeRow(1)

    assert bridge.currentPath == "mods"
    assert bridge.treeModel.row_at(1).expanded
    assert bridge.treeModel.row_at(2).label == "nested"


def test_fivefury_formats_have_specific_type_labels() -> None:
    expected = {
        "model.ydr": "Drawable",
        "models.ydd": "Drawable Dictionary",
        "textures.ytd": "Texture Dictionary",
        "vehicle.yft": "Fragment",
        "collision.ybn": "Collision Bounds",
        "placement.ymap": "Map Data",
        "archetypes.ytyp": "Archetype Definitions",
        "manifest.ymf": "Map Manifest",
        "metadata.ymt": "Metadata Container",
        "animations.ycd": "Clip Dictionary",
        "expressions.yed": "Expression Dictionary",
        "effects.ypt": "Particle Dictionary",
        "roads.ynd": "Node Dictionary",
        "navigation.ynv": "Navigation Mesh",
        "speech.awc": "Audio Container",
        "game_sounds.dat": "Audio Metadata",
        "american_rel.rpf": "RPF Package",
        "labels.gxt2": "Text Table",
        "scene.cut": "Cutscene",
        "gtxd.meta": "Texture Parent Data",
        "heightmap_0.dat": "Height Map",
        "water.xml": "Water Data",
        "gta5_cache_y.dat": "GTA V Cache",
        "vehicles.meta": "Vehicle Definitions",
        "handling.meta": "Vehicle Handling",
        "carcols.meta": "Vehicle Colors",
        "carmodcols.meta": "Vehicle Mod Colors",
        "carvariations.meta": "Vehicle Variations",
        "vehiclelayouts.meta": "Vehicle Layouts",
        "peds.meta": "Ped Definitions",
        "expression_sets.xml": "Expression Sets",
        "legacy.cdr": "PS3 Drawable",
    }

    assert {name: RpfProvider._file_kind(name) for name in expected} == expected


def test_bridge_opens_a_real_fivefury_archive(tmp_path) -> None:
    from fivefury import RpfArchive

    archive_path = tmp_path / "sample.rpf"
    archive = RpfArchive.empty(archive_path.name)
    archive.file("folder/readme.txt", b"hello")
    archive.save(archive_path)

    bridge = ExplorerBridge()
    bridge.openArchive(str(archive_path))

    assert bridge.hasArchive
    assert bridge.hasWorkspace
    assert bridge.archiveName == "sample.rpf"
    assert bridge.tabTitle == "sample.rpf"
    assert bridge.visibleCount == 1
    assert bridge.treeModel.rowCount() == 2
    assert bridge.selectedIndex == -1


def test_nested_archive_opens_from_folder_tree(tmp_path) -> None:
    from fivefury import RpfArchive

    nested_path = tmp_path / "nested.rpf"
    nested = RpfArchive.empty(nested_path.name)
    nested.file("inside/readme.txt", b"nested")
    nested.save(nested_path)

    outer_path = tmp_path / "outer.rpf"
    outer = RpfArchive.empty(outer_path.name)
    outer.file("nested.rpf", nested_path.read_bytes())
    outer.save(outer_path)

    bridge = ExplorerBridge()
    bridge.openArchive(str(outer_path))

    assert bridge.treeModel.row_at(1).label == "nested.rpf"
    nested_row = bridge.treeModel.row_at(1)
    bridge.navigateTree(nested_row.path)

    assert bridge.archiveName == "nested.rpf"
    assert bridge.tabTitle == "outer.rpf"
    assert bridge.visibleCount == 1
    assert bridge.entriesModel.entry_at(0).name == "inside"
    assert [
        bridge.treeModel.row_at(row).label
        for row in range(bridge.treeModel.rowCount())
    ] == ["outer.rpf", "nested.rpf", "inside"]
    assert bridge.treeModel.row_at(1).expanded
    assert bridge.treeModel.row_at(1).selected
    assert bridge.treeFocusPath == nested_row.path
    assert bridge.treeFocusIndex == 1


def test_game_folder_is_the_primary_workspace(tmp_path, monkeypatch) -> None:
    import fivefury
    from fivefury import RpfArchive

    monkeypatch.setattr(fivefury, "load_game_keys", lambda *args, **kwargs: None)
    (tmp_path / "GTA5.exe").write_bytes(b"test executable")
    mods_path = tmp_path / "mods"
    mods_path.mkdir()
    (mods_path / "nested").mkdir()
    archive_path = tmp_path / "x64a.rpf"
    archive = RpfArchive.empty(archive_path.name)
    archive.file("common/data/example.xml", b"<example />")
    archive.save(archive_path)

    bridge = ExplorerBridge()
    bridge.openGame(str(tmp_path))

    assert bridge.hasWorkspace
    assert bridge.hasGame
    assert not bridge.hasArchive
    assert bridge.gamePath == str(tmp_path)
    assert bridge.visibleCount == 3
    assert bridge.treeModel.rowCount() == 3
    assert bridge.selectedIndex == -1

    bridge.selectEntry(0)
    assert bridge.selectedIndex == 0
    bridge.navigate("mods")
    assert bridge.selectedIndex == -1
    bridge.navigate(".")

    archive_row = next(
        row
        for row in range(bridge.entriesModel.rowCount())
        if bridge.entriesModel.entry_at(row).name == "x64a.rpf"
    )
    bridge.activateEntry(archive_row)

    assert bridge.hasArchive
    assert bridge.inArchive
    assert bridge.archiveName == "x64a.rpf"
    assert bridge.tabTitle == tmp_path.name
    assert bridge.archiveGamePathParts == ["x64a.rpf"]
    assert [bridge.treeModel.row_at(row).label for row in range(bridge.treeModel.rowCount())] == [
        tmp_path.name,
        "mods",
        "x64a.rpf",
        "common",
    ]

    common_row = bridge.treeModel.row_at(3)
    bridge.toggleTreeNode(common_row.path)
    assert bridge.treeModel.row_at(4).label == "data"
    assert not bridge.treeModel.row_at(4).has_children

    bridge.toggleTreeNode("rpf://x64a.rpf")
    assert [bridge.treeModel.row_at(row).label for row in range(bridge.treeModel.rowCount())] == [
        tmp_path.name,
        "mods",
        "x64a.rpf",
    ]

    bridge.toggleTreeNode("game://mods")
    assert bridge.treeModel.row_at(2).label == "nested"
    assert not bridge.treeModel.row_at(2).has_children
    bridge.toggleTreeNode("game://mods")
    assert bridge.treeModel.rowCount() == 3


def test_nested_archive_keeps_complete_game_tree_route(tmp_path, monkeypatch) -> None:
    import fivefury
    from fivefury import RpfArchive

    monkeypatch.setattr(fivefury, "load_game_keys", lambda *args, **kwargs: None)
    (tmp_path / "GTA5.exe").write_bytes(b"game")

    nested = RpfArchive.empty("inner.rpf")
    nested.file("final/readme.txt", b"inner")
    outer = RpfArchive.empty("outer.rpf")
    outer.file("branch/keep.txt", b"outer")
    outer.file("branch/inner.rpf", nested.to_bytes())
    outer.save(tmp_path / "outer.rpf")

    bridge = ExplorerBridge()
    bridge.openGame(str(tmp_path))
    outer_row = next(
        row
        for row in range(bridge.treeModel.rowCount())
        if bridge.treeModel.row_at(row).label == "outer.rpf"
    )
    bridge.navigateTree(bridge.treeModel.row_at(outer_row).path)
    branch_row = next(
        bridge.treeModel.row_at(row)
        for row in range(bridge.treeModel.rowCount())
        if bridge.treeModel.row_at(row).label == "branch"
    )
    bridge.toggleTreeNode(branch_row.path)
    nested_row = next(
        bridge.treeModel.row_at(row)
        for row in range(bridge.treeModel.rowCount())
        if bridge.treeModel.row_at(row).label == "inner.rpf"
    )

    bridge.navigateTree(nested_row.path)

    assert [
        bridge.treeModel.row_at(row).label
        for row in range(bridge.treeModel.rowCount())
    ] == [tmp_path.name, "outer.rpf", "branch", "inner.rpf", "final"]
    assert bridge.treeModel.row_at(1).expanded
    assert bridge.treeModel.row_at(2).expanded
    assert bridge.treeModel.row_at(3).expanded
    assert bridge.treeModel.row_at(3).selected
    assert bridge.archiveGamePathParts == ["outer.rpf", "branch", "inner.rpf"]
    assert [segment["label"] for segment in bridge.navigationPathSegments] == [
        "outer.rpf",
        "branch",
        "inner.rpf",
    ]
    assert [segment["archive"] for segment in bridge.navigationPathSegments] == [
        True,
        False,
        True,
    ]

    bridge.navigateTree(branch_row.path)

    assert bridge.archiveName == "outer.rpf"
    assert bridge.currentPath == "branch"
    assert bridge.treeFocusPath == branch_row.path
    assert [
        bridge.treeModel.row_at(row).label
        for row in range(bridge.treeModel.rowCount())
    ] == [tmp_path.name, "outer.rpf", "branch", "inner.rpf", "final"]


def test_tree_focus_follows_path_when_rows_are_inserted(tmp_path, monkeypatch) -> None:
    import fivefury
    from fivefury import RpfArchive

    monkeypatch.setattr(fivefury, "load_game_keys", lambda *args, **kwargs: None)
    (tmp_path / "GTA5.exe").write_bytes(b"game")
    (tmp_path / "a_folder" / "child").mkdir(parents=True)
    archive = RpfArchive.empty("z_archive.rpf")
    archive.file("readme.txt", b"archive")
    archive.save(tmp_path / "z_archive.rpf")

    bridge = ExplorerBridge()
    bridge.openGame(str(tmp_path))
    archive_row = next(
        bridge.treeModel.row_at(row)
        for row in range(bridge.treeModel.rowCount())
        if bridge.treeModel.row_at(row).label == "z_archive.rpf"
    )
    bridge.focusTreePath(archive_row.path)
    previous_index = bridge.treeFocusIndex

    bridge._expanded_nodes.add("game://a_folder")
    bridge._refresh_tree()

    assert bridge.treeFocusPath == archive_row.path
    assert bridge.treeFocusIndex == previous_index + 1


def test_deeply_nested_archive_keeps_every_archive_node(tmp_path, monkeypatch) -> None:
    import fivefury
    from fivefury import RpfArchive

    monkeypatch.setattr(fivefury, "load_game_keys", lambda *args, **kwargs: None)
    (tmp_path / "GTA5.exe").write_bytes(b"game")

    deepest = RpfArchive.empty("deepest.rpf")
    deepest.file("final/readme.txt", b"deepest")
    middle = RpfArchive.empty("middle.rpf")
    middle.file("next/deepest.rpf", deepest.to_bytes())
    outer = RpfArchive.empty("outer.rpf")
    outer.file("branch/middle.rpf", middle.to_bytes())
    outer.save(tmp_path / "outer.rpf")

    bridge = ExplorerBridge()
    bridge.openGame(str(tmp_path))
    bridge.navigateTree("rpf://outer.rpf")
    bridge.toggleTreeNode(
        next(
            bridge.treeModel.row_at(row).path
            for row in range(bridge.treeModel.rowCount())
            if bridge.treeModel.row_at(row).label == "branch"
        )
    )
    bridge.navigateTree(
        next(
            bridge.treeModel.row_at(row).path
            for row in range(bridge.treeModel.rowCount())
            if bridge.treeModel.row_at(row).label == "middle.rpf"
        )
    )
    bridge.toggleTreeNode(
        next(
            bridge.treeModel.row_at(row).path
            for row in range(bridge.treeModel.rowCount())
            if bridge.treeModel.row_at(row).label == "next"
        )
    )
    deepest_path = next(
        bridge.treeModel.row_at(row).path
        for row in range(bridge.treeModel.rowCount())
        if bridge.treeModel.row_at(row).label == "deepest.rpf"
    )

    bridge.navigateTree(deepest_path)

    assert [
        bridge.treeModel.row_at(row).label
        for row in range(bridge.treeModel.rowCount())
    ] == [
        tmp_path.name,
        "outer.rpf",
        "branch",
        "middle.rpf",
        "next",
        "deepest.rpf",
        "final",
    ]
    assert bridge.treeFocusPath == deepest_path
    assert bridge.archiveGamePathParts == [
        "outer.rpf",
        "branch",
        "middle.rpf",
        "next",
        "deepest.rpf",
    ]


def test_loaded_flat_rpf_has_no_tree_expansion_control(tmp_path) -> None:
    from fivefury import RpfArchive

    flat = RpfArchive.empty("flat.rpf")
    flat.file("navmesh.ynv", b"flat")
    nested_path = tmp_path / "flat.rpf"
    flat.save(nested_path)

    structured = RpfArchive.empty("structured.rpf")
    structured.file("folder/readme.txt", b"structured")
    outer = RpfArchive.empty("outer.rpf")
    outer.file("flat.rpf", nested_path.read_bytes())
    outer.file("structured.rpf", structured.to_bytes())
    outer.save(tmp_path / "outer.rpf")

    bridge = ExplorerBridge()
    bridge.openArchive(str(tmp_path / "outer.rpf"))
    flat_row = next(
        bridge.treeModel.row_at(row)
        for row in range(bridge.treeModel.rowCount())
        if bridge.treeModel.row_at(row).label == "flat.rpf"
    )

    bridge.navigateTree(flat_row.path)

    flat_row = next(
        bridge.treeModel.row_at(row)
        for row in range(bridge.treeModel.rowCount())
        if bridge.treeModel.row_at(row).label == "flat.rpf"
    )
    assert flat_row.selected
    assert not flat_row.has_children
    assert not flat_row.expanded

    structured_path = next(
        bridge.treeModel.row_at(row).path
        for row in range(bridge.treeModel.rowCount())
        if bridge.treeModel.row_at(row).label == "structured.rpf"
    )
    bridge.navigateTree(structured_path)
    structured_row = next(
        bridge.treeModel.row_at(row)
        for row in range(bridge.treeModel.rowCount())
        if bridge.treeModel.row_at(row).label == "structured.rpf"
    )
    assert structured_row.has_children
    assert structured_row.expanded


def test_archive_keeps_its_game_relative_breadcrumb(tmp_path, monkeypatch) -> None:
    import fivefury
    from fivefury import RpfArchive

    monkeypatch.setattr(fivefury, "load_game_keys", lambda *args, **kwargs: None)
    (tmp_path / "GTA5.exe").write_bytes(b"test executable")
    archive_path = tmp_path / "mods" / "update" / "update.rpf"
    archive_path.parent.mkdir(parents=True)
    archive = RpfArchive.empty(archive_path.name)
    archive.file("x64/data/example.xml", b"<example />")
    archive.save(archive_path)

    bridge = ExplorerBridge()
    bridge.openGame(str(tmp_path))
    bridge.openArchive(str(archive_path))

    assert bridge.archiveGamePathParts == ["mods", "update", "update.rpf"]
    assert bridge.pathParts == []

    bridge.navigate("x64/data")

    assert bridge.archiveGamePathParts == ["mods", "update", "update.rpf"]
    assert bridge.pathParts == ["x64", "data"]


def test_tabs_keep_independent_explorer_state(tmp_path, monkeypatch) -> None:
    import fivefury

    monkeypatch.setattr(fivefury, "load_game_keys", lambda *args, **kwargs: None)
    enhanced = tmp_path / "Enhanced"
    enhanced.mkdir()
    (enhanced / "GTA5_Enhanced.exe").write_bytes(b"enhanced")
    (enhanced / "mods").mkdir()
    legacy = tmp_path / "Legacy"
    legacy.mkdir()
    (legacy / "GTA5.exe").write_bytes(b"legacy")
    (legacy / "update").mkdir()

    tabs = ExplorerTabs()
    first = tabs.activeBridge
    first.openGame(str(enhanced))
    first.navigate("mods")

    tabs.newTab()
    second = tabs.activeBridge
    second.openGame(str(legacy))
    second.navigate("update")

    assert tabs.rowCount() == 2
    assert tabs.activeIndex == 1
    assert second.gamePath == str(legacy)
    assert second.currentPath == "update"

    tabs.activateTab(0)

    assert tabs.activeBridge is first
    assert first.gamePath == str(enhanced)
    assert first.currentPath == "mods"

    tabs.closeActiveTab()

    assert tabs.rowCount() == 1
    assert tabs.activeBridge is second
    assert second.currentPath == "update"


def test_directory_named_rpf_navigates_as_folder(tmp_path, monkeypatch) -> None:
    import fivefury

    monkeypatch.setattr(fivefury, "load_game_keys", lambda *args, **kwargs: None)
    (tmp_path / "GTA5.exe").write_bytes(b"test executable")
    virtual_archive = tmp_path / "mods" / "update" / "update.rpf"
    (virtual_archive / "x64").mkdir(parents=True)

    bridge = ExplorerBridge()
    bridge.openGame(str(tmp_path))
    bridge.toggleTreeNode("game://mods")
    bridge.toggleTreeNode("game://mods/update")

    row = next(
        bridge.treeModel.row_at(index)
        for index in range(bridge.treeModel.rowCount())
        if bridge.treeModel.row_at(index).label == "update.rpf"
    )
    assert row.kind == "folder"
    assert row.path == "game://mods/update/update.rpf"

    bridge.navigateTree(row.path)

    assert not bridge.inArchive
    assert bridge.currentPath == "mods/update/update.rpf"
    assert bridge.visibleCount == 1
