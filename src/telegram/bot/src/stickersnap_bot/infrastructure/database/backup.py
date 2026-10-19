"""The daily copy of the database: one file a day, the oldest beyond `keep` deleted."""

import sqlite3
from datetime import datetime
from pathlib import Path

PREFIX = "bot-"
SUFFIX = ".sqlite3"


def back_up(database_path: Path, folder: Path, now: datetime, keep: int) -> Path:
    """Copies the database with SQLite's online backup, safe while the service writes to it
    (a plain file copy could catch the WAL half-applied). A second run on one day replaces
    that day's copy. Returns the copy's path."""
    if keep < 1:
        raise ValueError("keep at least one copy")
    folder.mkdir(parents=True, exist_ok=True)
    target = folder / f"{PREFIX}{now:%Y-%m-%d}{SUFFIX}"
    partial = target.with_name(target.name + ".partial")
    # read-only: a missing database is an error, not a new empty file
    source = sqlite3.connect(f"{database_path.resolve().as_uri()}?mode=ro", uri=True)
    try:
        copy = sqlite3.connect(partial)
        try:
            source.backup(copy)
        finally:
            copy.close()
    finally:
        source.close()
    # whole or not at all: a copy cut off midway never takes a day's name
    partial.replace(target)
    copies = sorted(folder.glob(f"{PREFIX}*{SUFFIX}"))
    for old in copies[:-keep]:
        old.unlink()
    return target
