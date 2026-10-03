"""AD HOC compatibility for empty YTDs rejected by FiveFury Python 0.5.1.

These two synthetic templates were written and strictly validated by FiveFury.NET
(78dd78edd50298e1440a310191c095242d90f4b0). They contain ZERO textures, not a
hidden placeholder. No .NET runtime is required to use the templates.

Remove this module once the application's FiveFury integration supports empty
read/write/validation natively. See docs/EMPTY_YTD_AD_HOC.md for the narrow scope.
"""

from __future__ import annotations

from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from fivefury import GameTarget
    from fivefury.ytd import Ytd

_TEMPLATES = {
    'gta5': bytes.fromhex(
        '525343370D00000000000200000000D0EDCA211100200000B1A71911E89F0681'
        'C17287DCF4EA98B5BA8CDEBC7E00000000000000E09F0D'
    ),
    'gta5_enhanced': bytes.fromhex(
        '52534337050000000000020000000050EDCA211100200000B1A71911E89F0681'
        'C17287DCF4EA98B5BA8CDEBC7E00000000000000E09F0D'
    ),
}


def empty_ytd_bytes(game: str | GameTarget) -> bytes:
    """Return the verified empty template for an explicit, supported edition."""
    from fivefury.game_target import coerce_game_target

    return _TEMPLATES[coerce_game_target(game)]


def read_ytd(data: bytes) -> Ytd:
    """Recognize only our empty templates; delegate every other YTD unchanged."""
    from fivefury.resource import split_rsc7_sections
    from fivefury.ytd import Ytd, read_ytd as upstream_read_ytd

    for game, template in _TEMPLATES.items():
        if data[:16] != template[:16]:
            continue
        # RPF storage may recompress the resource. Require the exact header and
        # decompressed contents, not merely a zero count in an arbitrary file.
        if split_rsc7_sections(data) == split_rsc7_sections(template):
            return Ytd([], game=game)
    return upstream_read_ytd(data)
