#!/usr/bin/env python3
"""Fails if any tracked text file we maintain contains a byte outside ASCII.

Run from anywhere in the working tree:

    python tools/check-ascii.py

Exits 0 when clean, 1 when something is found.

    --fix            strip UTF-8 byte order marks, then re-check
    --list-skipped   show which paths were treated as third party

dotnet ef writes a byte order mark on the files it generates, so run --fix after
adding a migration. Real characters are never rewritten automatically; replacing
one is a judgement call.
"""

import argparse
import os
import subprocess
import sys

# Vendored third-party code. Replaced wholesale on upgrade, so edits here would
# be lost and are not ours to make.
THIRD_PARTY = (
    "src/XeonProductions.Web/wwwroot/lib/",
)

BOM = b"\xef\xbb\xbf"


def repo_root():
    out = subprocess.run(
        ["git", "rev-parse", "--show-toplevel"],
        capture_output=True, text=True, check=True)
    return out.stdout.strip()


def tracked_files(root):
    out = subprocess.run(
        ["git", "ls-files", "-z"],
        capture_output=True, cwd=root, check=True)
    return [p.decode() for p in out.stdout.split(b"\x00") if p]


def is_third_party(path):
    return path.startswith(THIRD_PARTY)


def strip_boms(root, paths):
    """Removes UTF-8 byte order marks. Returns the paths that changed."""
    changed = []

    for rel in paths:
        if is_third_party(rel):
            continue

        full = os.path.join(root, rel)

        try:
            raw = open(full, "rb").read()
        except OSError:
            continue

        if raw.startswith(BOM):
            open(full, "wb").write(raw[len(BOM):])
            changed.append(rel)

    return changed


def scan(root, paths):
    """Returns (violations, skipped). Violations are (path, line, col, what)."""
    violations = []
    skipped = []

    for rel in paths:
        if is_third_party(rel):
            skipped.append(rel)
            continue

        full = os.path.join(root, rel)

        try:
            raw = open(full, "rb").read()
        except OSError:
            continue

        # Binary files are not text and have nothing to normalise.
        if b"\x00" in raw:
            continue

        body = raw
        if raw.startswith(BOM):
            violations.append((rel, 1, 1, "UTF-8 byte order mark"))
            body = raw[len(BOM):]

        if all(b < 128 for b in body):
            continue

        try:
            text = body.decode("utf-8")
        except UnicodeDecodeError:
            violations.append((rel, 0, 0, "not valid UTF-8"))
            continue

        for lineno, line in enumerate(text.splitlines(), start=1):
            for col, ch in enumerate(line, start=1):
                if ord(ch) > 127:
                    violations.append((rel, lineno, col, "U+%04X" % ord(ch)))

    return violations, skipped


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--fix", action="store_true",
                        help="strip UTF-8 byte order marks before checking")
    parser.add_argument("--list-skipped", action="store_true",
                        help="print the third-party paths that were not checked")
    args = parser.parse_args()

    root = repo_root()
    paths = tracked_files(root)

    if args.fix:
        for rel in strip_boms(root, paths):
            print("stripped byte order mark from %s" % rel)

    violations, skipped = scan(root, paths)

    if args.list_skipped:
        for rel in skipped:
            print("skipped  %s" % rel)

    checked = len(paths) - len(skipped)

    if not violations:
        print("ASCII check clean across %d tracked files (%d third-party skipped)."
              % (checked, len(skipped)))
        return 0

    for rel, line, col, what in violations:
        print("ERROR    %s:%d:%d  %s" % (rel, line, col, what))

    print("\n%d non-ASCII character(s) found in %d file(s)."
          % (len(violations), len({v[0] for v in violations})))
    return 1


if __name__ == "__main__":
    sys.exit(main())
