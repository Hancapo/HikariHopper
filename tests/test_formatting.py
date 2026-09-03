from rpf_explorer.formatting import format_size, format_texture_format


def test_format_size_uses_binary_units() -> None:
    assert format_size(12) == "12 B"
    assert format_size(1024) == "1.0 KB"
    assert format_size(412 * 1024**2) == "412.0 MB"


def test_texture_formats_use_familiar_names_and_optional_exact_type() -> None:
    assert format_texture_format("BC1") == "DXT1"
    assert format_texture_format("BC5") == "ATI2"
    assert format_texture_format("BC7") == "BPTC RGBA"
    assert format_texture_format("BC1", include_exact=True) == "DXT1 (BC1)"
    assert format_texture_format("R8") == "R8"
