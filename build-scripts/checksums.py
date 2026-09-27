#!/usr/bin/env python3
"""Write or verify md5/sha1/sha256 sidecar checksum files for release packages.

Usage:
    checksums.py PACKAGE [PACKAGE ...]          write PKG.md5 / PKG.sha1 / PKG.sha256
    checksums.py --check PACKAGE [PACKAGE ...]  verify existing sidecars match

Sidecar files use the standard "<hex digest>  <file name>" format so they work
with md5sum/shasum -c style tooling on every platform.
"""

import hashlib
import sys
from pathlib import Path

ALGORITHMS = ("md5", "sha1", "sha256")


def sidecar_path(package: Path, algorithm: str) -> Path:
    return package.with_name(package.name + "." + algorithm)


def write_checksums(package: Path) -> None:
    if not package.is_file():
        raise FileNotFoundError(f"package not found: {package}")
    data = package.read_bytes()
    for algorithm in ALGORITHMS:
        digest = hashlib.new(algorithm, data).hexdigest()
        sidecar_path(package, algorithm).write_text(
            f"{digest}  {package.name}\n", encoding="utf-8"
        )


def check_checksums(package: Path) -> None:
    if not package.is_file():
        raise FileNotFoundError(f"package not found: {package}")
    data = package.read_bytes()
    for algorithm in ALGORITHMS:
        sidecar = sidecar_path(package, algorithm)
        if not sidecar.is_file():
            raise FileNotFoundError(f"missing checksum file: {sidecar}")
        expected = sidecar.read_text(encoding="utf-8").split()[0]
        actual = hashlib.new(algorithm, data).hexdigest()
        if expected.lower() != actual.lower():
            raise ValueError(f"{algorithm} mismatch for {package.name}: {expected} != {actual}")


def main(argv: list[str]) -> int:
    args = list(argv[1:])
    check = False
    if args and args[0] == "--check":
        check = True
        args = args[1:]
    if not args:
        print(__doc__.strip(), file=sys.stderr)
        return 2
    for name in args:
        package = Path(name)
        try:
            if check:
                check_checksums(package)
            else:
                write_checksums(package)
        except (OSError, ValueError) as error:
            print(f"::error::{error}", file=sys.stderr)
            return 1
        print(f"{'verified' if check else 'wrote'} checksums for {package.name}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
