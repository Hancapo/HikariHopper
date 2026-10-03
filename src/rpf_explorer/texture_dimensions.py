"""Texture sizing policies shared by previews and editing actions."""


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
