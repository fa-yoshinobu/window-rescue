"""Check the pinned runtime against Microsoft's public release metadata."""

import argparse
import json
from pathlib import Path
import re
import urllib.request
import xml.etree.ElementTree as ET


def pinned_version(project):
    version = ET.parse(project).findtext(".//RuntimeFrameworkVersion", "").strip()
    if not re.fullmatch(r"\d+\.\d+\.\d+", version):
        raise ValueError("RuntimeFrameworkVersion must pin a stable major.minor.patch version")
    return version


def check_version(pinned, metadata):
    channel = pinned.rsplit(".", 1)[0]
    latest = metadata["latest-runtime"]
    if metadata["channel-version"] != channel or not re.fullmatch(r"\d+\.\d+\.\d+", latest):
        raise ValueError("Invalid runtime release metadata")
    if metadata["support-phase"] == "eol":
        raise ValueError(f".NET {channel} is out of support; upgrade the target framework")
    if pinned != latest:
        raise ValueError(f"Pinned runtime {pinned}; latest {latest}. Update RuntimeFrameworkVersion and release again.")
    if not any(r.get("windowsdesktop", {}).get("version") == latest for r in metadata["releases"]):
        raise ValueError(f"Windows Desktop runtime {latest} is not published yet")
    return f"Pinned .NET and Windows Desktop runtime {pinned} are current."


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project", type=Path, default=Path("src/WindowRescue/WindowRescue.csproj"))
    args = parser.parse_args()
    try:
        pinned = pinned_version(args.project)
        channel = pinned.rsplit(".", 1)[0]
        url = f"https://builds.dotnet.microsoft.com/dotnet/release-metadata/{channel}/releases.json"
        with urllib.request.urlopen(url, timeout=30) as response:
            metadata = json.load(response)
        print(check_version(pinned, metadata))
    except (OSError, ValueError, KeyError, ET.ParseError) as error:
        raise SystemExit(f"Runtime check failed: {error}") from error


if __name__ == "__main__":
    main()
