#!/usr/bin/env python3
"""Helpers for Minetake VPM publish workflows."""
import argparse
import json
import os
import re
import sys
from datetime import datetime, timezone
from pathlib import Path

# Semver 2.0.0 core + optional prerelease (no +build).
SEMVER_RE = re.compile(
    r"^(?P<major>0|[1-9]\d*)\.(?P<minor>0|[1-9]\d*)\.(?P<patch>0|[1-9]\d*)"
    r"(?:-(?P<prerelease>(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*)"
    r"(?:\.(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*))*))?$"
)

IDENTIFIER_RE = re.compile(r"^[0-9A-Za-z-]+$")


def parse_semver(version):
    version = version.split("+", 1)[0].strip()
    match = SEMVER_RE.fullmatch(version)
    if not match:
        sys.exit(f"error: invalid semver {version!r}")
    return (
        int(match.group("major")),
        int(match.group("minor")),
        int(match.group("patch")),
        match.group("prerelease"),
    )


def dev_prerelease_id(run_number):
    week = datetime.now(timezone.utc).strftime("%G.W%V")
    return f"dev.{week}.{run_number}"


def normalize_identifier(identifier, run_number):
    if identifier is None or not str(identifier).strip():
        return dev_prerelease_id(run_number)
    ident = str(identifier).strip()
    if not IDENTIFIER_RE.fullmatch(ident):
        sys.exit(f"error: invalid prerelease identifier {ident!r} "
                 "(use letters, digits, and hyphens only)")
    return ident


def bump_major(version):
    major, _minor, _patch, _pre = parse_semver(version)
    return f"{major + 1}.0.0"


def bump_minor(version):
    major, minor, _patch, _pre = parse_semver(version)
    return f"{major}.{minor + 1}.0"


def bump_patch(version):
    major, minor, patch, _pre = parse_semver(version)
    return f"{major}.{minor}.{patch + 1}"


def bump_prerelease(version, identifier, run_number):
    major, minor, patch, prerelease = parse_semver(version)
    core = f"{major}.{minor}.{patch}"
    ident = normalize_identifier(identifier, run_number)

    if prerelease:
        if prerelease == ident:
            return f"{core}-{ident}"
        if prerelease.startswith(ident + "."):
            suffix = prerelease[len(ident) + 1:]
            parts = suffix.split(".") if suffix else []
            if parts and parts[-1].isdigit():
                parts[-1] = str(int(parts[-1]) + 1)
                return f"{core}-{ident}.{'.'.join(parts)}"
            return f"{core}-{ident}.1"
        return f"{core}-{ident}.1"

    if "." in ident and ident.split(".")[-1].isdigit():
        return f"{core}-{ident}"
    return f"{core}-{ident}.1"


def compute_bump(version, bump_type, identifier=None, run_number="0"):
    bump_type = bump_type.lower()
    if bump_type == "major":
        return bump_major(version)
    if bump_type == "minor":
        return bump_minor(version)
    if bump_type == "patch":
        return bump_patch(version)
    if bump_type == "prerelease":
        return bump_prerelease(version, identifier, run_number)
    sys.exit(f"error: unknown bump type {bump_type!r}")


def slugify_display_name(display_name):
    return re.sub(r"[^A-Za-z0-9.\-]+", "-", display_name).strip("-")


def release_tag(display_name, publish_version):
    return f"{slugify_display_name(display_name)}-v{publish_version}"


def release_title(display_name, publish_version):
    return f"{display_name} {publish_version}"


def update_package_json(path, publish_version):
    path = Path(path)
    data = json.loads(path.read_text(encoding="utf-8-sig"))
    data["version"] = publish_version
    path.write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8")


def write_github_output(values):
    out_path = os.environ.get("GITHUB_OUTPUT")
    if not out_path:
        for key, value in values.items():
            print(f"{key}={value}")
        return
    with open(out_path, "a", encoding="utf-8") as fh:
        for key, value in values.items():
            fh.write(f"{key}={value}\n")


def cmd_bump_package(args):
    path = Path(args.package_json)
    if not path.is_file():
        sys.exit(f"error: package.json not found: {path}")
    data = json.loads(path.read_text(encoding="utf-8-sig"))
    previous_version = (data.get("version") or "").strip()
    if not previous_version:
        sys.exit("error: package.json missing version")
    display_name = (data.get("displayName") or data.get("name") or "").strip()
    if not display_name:
        sys.exit("error: package.json missing displayName and name")

    publish_version = compute_bump(
        previous_version, args.bump_type, args.identifier, args.run_number
    )
    is_prerelease = args.bump_type.lower() == "prerelease"
    tag = release_tag(display_name, publish_version)
    title = release_title(display_name, publish_version)

    if args.write:
        update_package_json(path, publish_version)

    write_github_output({
        "previous_version": previous_version,
        "publish_version": publish_version,
        "display_name": display_name,
        "tag": tag,
        "release_name": title,
        "prerelease": "true" if is_prerelease else "false",
    })
    print(f"{previous_version} -> {publish_version} ({tag})")


def cmd_set_version(args):
    update_package_json(args.package_json, args.publish_version)


def main():
    p = argparse.ArgumentParser(description=__doc__)
    sub = p.add_subparsers(dest="cmd", required=True)

    bump = sub.add_parser("bump-package", help="Bump package.json version and emit publish metadata.")
    bump.add_argument("package_json")
    bump.add_argument("--bump-type", required=True,
                      choices=["major", "minor", "patch", "prerelease"])
    bump.add_argument("--identifier", default="",
                      help="Prerelease identifier (e.g. rc, beta). "
                           "Empty uses dev.<year>.W<week>.<run>.")
    bump.add_argument("--run-number", default="0")
    bump.add_argument("--write", action="store_true",
                      help="Write bumped version into package.json")

    set_ver = sub.add_parser("set-version", help="Write publish version into package.json.")
    set_ver.add_argument("package_json")
    set_ver.add_argument("publish_version")

    args = p.parse_args()
    if args.cmd == "bump-package":
        cmd_bump_package(args)
    elif args.cmd == "set-version":
        cmd_set_version(args)
    else:
        sys.exit(f"error: unknown command {args.cmd!r}")


if __name__ == "__main__":
    main()
