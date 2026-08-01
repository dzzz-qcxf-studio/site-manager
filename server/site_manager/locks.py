from __future__ import annotations

import threading
from pathlib import Path

try:
    import fcntl
except ImportError:  # pragma: no cover - local Windows tests use the thread fallback.
    fcntl = None


_thread_locks: dict[Path, threading.Lock] = {}
_thread_locks_guard = threading.Lock()


class SiteLock:
    def __init__(self, locks_directory: Path, site_id: str) -> None:
        self._path = locks_directory / f"{site_id}.lock"
        self._file = None
        with _thread_locks_guard:
            self._thread_lock = _thread_locks.setdefault(self._path, threading.Lock())

    def __enter__(self) -> "SiteLock":
        self._path.parent.mkdir(parents=True, exist_ok=True)
        self._thread_lock.acquire()
        self._file = self._path.open("a+")
        if fcntl is not None:
            fcntl.flock(self._file.fileno(), fcntl.LOCK_EX)
        return self

    def __exit__(self, exc_type, exc_value, traceback) -> None:
        if self._file is not None:
            if fcntl is not None:
                fcntl.flock(self._file.fileno(), fcntl.LOCK_UN)
            self._file.close()
        self._thread_lock.release()
