#!/usr/bin/env python3
"""Maintain the Jellyfin plugin repository manifest.

Reads the plugin metadata from build.yaml and inserts or replaces one version
entry in manifest.json. Jellyfin validates the archive against the checksum,
which has to be the MD5 of the release archive.

Usage:
    python3 scripts/update_manifest.py \
        --source-url https://.../jellyfin-plugin-dmmgravure.zip \
        --checksum <md5> \
        [--version 1.1.0.0] [--target-abi 10.11.0.0] \
        [--changelog "text"] [--timestamp 2026-09-14T14:34:15Z]
"""

from __future__ import annotations

import argparse
import datetime as dt
import json
import os
import re
import sys
from typing import Any

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BUILD_YAML = os.path.join(ROOT, "build.yaml")
MANIFEST = os.path.join(ROOT, "manifest.json")


def parse_build_yaml(path: str) -> dict[str, str]:
    """Extracts the flat keys and folded blocks used by jprm build files."""
    values: dict[str, str] = {}
    current: str | None = None
    buffer: list[str] = []

    with open(path, encoding="utf-8") as handle:
        lines = handle.read().splitlines()

    for line in lines:
        if line.strip() == "---":
            continue

        folded = re.match(r"^(\w+):\s*>\s*$", line)
        if folded:
            current = folded.group(1)
            buffer = []
            continue

        if current and (line.startswith((" ", "\t")) or not line.strip()):
            buffer.append(line.strip())
            continue

        if current:
            values[current] = " ".join(part for part in buffer if part)
            current = None

        pair = re.match(r"^(\w+):\s*(.*)$", line)
        if pair:
            value = pair.group(2).strip().strip('"').strip("'")
            if value not in ("", ">"):
                values[pair.group(1)] = value

    if current:
        values[current] = " ".join(part for part in buffer if part)

    return values


def version_key(version: str) -> tuple:
    parts = []
    for chunk in re.split(r"[.\-+]", version)[:4]:
        parts.append(int(chunk) if chunk.isdigit() else 0)
    while len(parts) < 4:
        parts.append(0)
    return tuple(parts)


def main() -> int:
    parser = argparse.ArgumentParser(description="Update the Jellyfin plugin manifest.")
    parser.add_argument("--source-url", required=True, help="Download URL of the release archive")
    parser.add_argument("--checksum", required=True, help="MD5 of the release archive")
    parser.add_argument("--version", help="Defaults to the version in build.yaml")
    parser.add_argument("--target-abi", help="Defaults to the targetAbi in build.yaml")
    parser.add_argument("--changelog", help="Defaults to the changelog in build.yaml")
    parser.add_argument("--timestamp", help="ISO 8601 timestamp, defaults to now")
    parser.add_argument("--manifest", default=MANIFEST, help="Path to the manifest file")
    args = parser.parse_args()

    build = parse_build_yaml(BUILD_YAML)

    version = args.version or build.get("version", "")
    target_abi = args.target_abi or build.get("targetAbi", "")
    changelog = args.changelog or build.get("changelog", "")
    guid = (build.get("guid") or "").lower()

    if not version or not target_abi or not guid:
        print("build.yaml is missing version, targetAbi or guid", file=sys.stderr)
        return 1

    checksum = args.checksum.strip().lower()
    if not re.fullmatch(r"[0-9a-f]{32}", checksum):
        print("checksum must be the 32 character MD5 of the archive", file=sys.stderr)
        return 1

    timestamp = args.timestamp or dt.datetime.now(dt.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")

    entry: dict[str, Any] = {
        "version": version,
        "changelog": changelog,
        "targetAbi": target_abi,
        "sourceUrl": args.source_url,
        "checksum": checksum,
        "timestamp": timestamp,
    }

    plugin: dict[str, Any] = {
        "guid": guid,
        "name": build.get("name", ""),
        "description": build.get("description", ""),
        "overview": build.get("overview", ""),
        "owner": build.get("owner", ""),
        "category": build.get("category", "Metadata"),
        "versions": [],
    }

    if build.get("imageUrl"):
        plugin["imageUrl"] = build["imageUrl"]

    manifest: list[dict[str, Any]] = []
    if os.path.exists(args.manifest):
        with open(args.manifest, encoding="utf-8") as handle:
            loaded = json.load(handle)
        manifest = loaded if isinstance(loaded, list) else [loaded]

    existing = next((p for p in manifest if str(p.get("guid", "")).lower() == guid), None)
    if existing is None:
        manifest.append(plugin)
        existing = plugin

    # Keep the mutable fields in sync with build.yaml.
    for key in ("name", "description", "overview", "owner", "category", "imageUrl"):
        if plugin.get(key):
            existing[key] = plugin[key]

    versions = [v for v in existing.get("versions", []) if v.get("version") != version]
    versions.append(entry)
    versions.sort(key=lambda v: version_key(str(v.get("version", "0"))), reverse=True)
    existing["versions"] = versions

    with open(args.manifest, "w", encoding="utf-8") as handle:
        json.dump(manifest, handle, ensure_ascii=False, indent=2)
        handle.write("\n")

    print(f"manifest updated: {os.path.relpath(args.manifest, ROOT)} -> {version} ({checksum})")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
