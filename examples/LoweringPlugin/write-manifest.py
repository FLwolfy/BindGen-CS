#!/usr/bin/env python3
"""Lock this Contracts-only example's actual built assembly closure."""
from pathlib import Path
import argparse
import hashlib
import json

root = Path(__file__).resolve().parent
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--configuration", default="Release")
args = parser.parse_args()
directory = root / "bin" / args.configuration / "net10.0"
entry = directory / "BGCS.Example.LoweringPlugin.dll"
if not entry.is_file():
    parser.error("Build BGCS.Example.LoweringPlugin.csproj before writing its manifest.")
files = sorted([*directory.glob("*.dll"), *directory.glob("*.deps.json")])
manifest = {
    "schemaVersion": 1,
    "plugin": {"id": "bgcs.examples.engine-lowering", "version": "4.0.0", "contractsMajor": 4,
               "services": [{"id": "engine.rename-tick", "port": "cppCallableLowering", "priority": 100}]},
    "assembly": entry.relative_to(root).as_posix(),
    "entryType": "BGCS.Examples.LoweringPlugin.EngineLoweringPlugin",
    "assets": [{"path": path.relative_to(root).as_posix(),
                "digest": {"value": hashlib.sha256(path.read_bytes()).hexdigest()}} for path in files]
}
(root / "plugin.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
