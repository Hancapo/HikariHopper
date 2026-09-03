from __future__ import annotations

import os
import subprocess
import sys
from pathlib import Path


def test_bootstrap_modules_do_not_import_fivefury() -> None:
    project_root = Path(__file__).parents[1]
    environment = os.environ.copy()
    environment["PYTHONPATH"] = str(project_root / "src")
    result = subprocess.run(
        [
            sys.executable,
            "-c",
            (
                "import sys; "
                "import rpf_explorer.app; "
                "import rpf_explorer.backend; "
                "print('fivefury' in sys.modules)"
            ),
        ],
        check=True,
        capture_output=True,
        text=True,
        env=environment,
    )

    assert result.stdout.strip() == "False"
