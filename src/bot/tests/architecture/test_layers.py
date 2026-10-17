import subprocess
import sys
from pathlib import Path

PROJECT = Path(__file__).parents[2]


def test_layers_point_inwards() -> None:
    # The contracts are in pyproject.toml ([tool.importlinter]); never weaken them to pass
    linter = Path(sys.executable).parent / "lint-imports"
    result = subprocess.run([str(linter)], cwd=PROJECT, capture_output=True, text=True, check=False)

    assert result.returncode == 0, result.stdout + result.stderr
