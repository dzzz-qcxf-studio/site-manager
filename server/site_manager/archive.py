from __future__ import annotations

import shutil
import tarfile
from pathlib import Path, PurePosixPath


class ArchiveSafetyError(ValueError):
    """Raised when a tar archive contains content unsafe to publish."""


class IndexMissingError(ArchiveSafetyError):
    """Raised when the archive is safe but has no root index.html."""


def extract_safe_archive(archive_path: Path, destination: Path) -> int:
    if destination.exists():
        raise ValueError("Archive destination must not already exist.")

    destination.mkdir(parents=True)
    destination_root = destination.resolve()
    total_bytes = 0
    try:
        with tarfile.open(archive_path, "r:gz") as archive:
            for member in archive:
                relative_path = _validate_member(member)
                output_path = destination.joinpath(*relative_path.parts)
                resolved_output = output_path.resolve()
                try:
                    resolved_output.relative_to(destination_root)
                except ValueError as error:
                    raise ArchiveSafetyError(f"Archive entry escapes destination: {member.name}") from error

                if member.isdir():
                    output_path.mkdir(parents=True, exist_ok=True)
                    continue

                output_path.parent.mkdir(parents=True, exist_ok=True)
                source = archive.extractfile(member)
                if source is None:
                    raise ArchiveSafetyError(f"Unable to read archive entry: {member.name}")
                with source, output_path.open("xb") as output:
                    shutil.copyfileobj(source, output, length=1024 * 1024)
                output_path.chmod(0o644)
                total_bytes += member.size

        index_path = destination / "index.html"
        if not index_path.is_file() or index_path.is_symlink():
            raise IndexMissingError("Archive root must contain a regular index.html file.")
        return total_bytes
    except Exception:
        shutil.rmtree(destination, ignore_errors=True)
        raise


def _validate_member(member: tarfile.TarInfo) -> PurePosixPath:
    path = PurePosixPath(member.name)
    if not member.name or "\\" in member.name or path.is_absolute() or ".." in path.parts:
        raise ArchiveSafetyError(f"Unsafe archive entry path: {member.name}")
    if not (member.isfile() or member.isdir()):
        raise ArchiveSafetyError(f"Unsafe archive entry type: {member.name}")
    return path
