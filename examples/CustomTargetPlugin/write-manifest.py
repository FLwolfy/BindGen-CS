#!/usr/bin/env python3
"""Lock the actual Contracts-only target plugin assembly closure after building it."""
import argparse
import hashlib
import json
from pathlib import Path

root = Path(__file__).resolve().parent
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--configuration", default="Release")
args = parser.parse_args()
directory = root / "bin" / args.configuration / "net10.0"
entry = directory / "BGCS.Example.CustomTargetPlugin.dll"
if not entry.is_file():
    parser.error("Build BGCS.Example.CustomTargetPlugin.csproj first.")
schema = {"type": "object", "additionalProperties": False, "required": ["profile"],
          "properties": {"profile": {"type": "string", "minLength": 1}}}
files = sorted([*directory.glob("*.dll"), *directory.glob("*.deps.json")])
manifest = {
    "schemaVersion": 1,
    "plugin": {"id": "bgcs.examples.custom-target", "version": "4.1.2", "contractsMajor": 4,
               "services": [{"id": "example.custom-target", "port": "nativeTargetProvider", "priority": 0}],
               "configurationSchema": schema},
    "assembly": entry.relative_to(root).as_posix(),
    "entryType": "BGCS.Examples.CustomTargetPlugin.CustomTargetPlugin",
    "assets": [{"path": path.relative_to(root).as_posix(),
                "digest": {"value": hashlib.sha256(path.read_bytes()).hexdigest()}} for path in files]
}
(root / "plugin.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
