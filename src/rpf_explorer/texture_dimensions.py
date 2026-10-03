"""Texture sizing policies shared by previews and editing actions."""


def mip_count_for_dimensions(width: int, height: int, min_mip_size: int) -> int:
    """Return the TexFury chain length for the requested dimensions."""
    return len(mip_dimensions_for_minimum(width, height, min_mip_size))


def mip_dimensions_for_minimum(
    width: int, height: int, min_mip_size: int,
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
        if (
            min(next_width, next_height) < minimum
            or (next_width, next_height) == (mip_width, mip_height)
        ):
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


def power_of_two_dimensions(width: int, height: int, mode: str) -> tuple[int, int]:
    from texfury import next_power_of_two, pot_dimensions

    if not (0 < width <= 0xFFFF and 0 < height <= 0xFFFF):
        raise ValueError("Texture dimensions must be between 1 and 65535")
    if mode == "nearest":
        return pot_dimensions(width, height)
    if mode not in {"down", "up"}:
        raise ValueError(f"Unknown power-of-two rounding mode: {mode}")
    upper = (next_power_of_two(width), next_power_of_two(height))
    if mode == "up":
        return upper
    return tuple(limit if limit == size else limit // 2
                 for size, limit in zip((width, height), upper, strict=True))
