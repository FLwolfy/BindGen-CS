#!/usr/bin/env python3
"""Render the tested configuration catalog from current source and public-boundary fixtures."""

import json
from pathlib import Path
import re


ROOT = Path(__file__).resolve().parents[1]


def properties(project: str, prefix: str) -> dict[str, tuple[str, str]]:
    result = {}
    declaration = re.compile(
        r"public\s+([^\r\n]+?)\s+(@?\w+)\s*\{\s*get;\s*set;\s*\}([^\r\n]*)"
    )
    for path in sorted((ROOT / "src" / project / "Configuration").glob(prefix + "*.cs")):
        for match in declaration.finditer(path.read_text(encoding="utf-8-sig")):
            type_name, name, suffix = match.groups()
            name = name.lstrip("@")
            default = suffix.strip().removeprefix("=").strip().removesuffix(";")
            if not default or default == "null!":
                if type_name.startswith(("List", "HashSet")):
                    default = "[]"
                elif type_name.startswith("Dictionary"):
                    default = "{}"
                elif type_name == "bool":
                    default = "false"
                elif type_name.endswith("?"):
                    default = "null"
                else:
                    default = "configured during initialization"
            result[name] = (type_name, default)
    return result


def first_file(folder: Path, pattern: str, excluded: str = "") -> Path | None:
    return next((path for path in sorted(folder.glob(pattern))
                 if not excluded or not path.name.startswith(excluded)), None)


def generate(project: str, prefix: str, test_project: str, output: str, language: str) -> None:
    available = properties(project, prefix)
    parts = [
        "# Tested Configuration Entries" if language == "csharp" else "# Tested C++ Bridge Configuration Entries",
        "",
        f"This catalog is generated from `{test_project}` fixtures and the current `{prefix}` source.",
        "It describes tested behavior; [Configuration Guide](configuration-guide.md) explains composition,",
        "targets, ownership, and failure boundaries. Use the CLI `schema` command for the full binding configuration.",
        "",
    ]
    for folder in sorted((ROOT / "tests" / test_project / "entryTests").iterdir()):
        if not folder.is_dir() or not any(folder.glob("*.cs")):
            continue
        snapshot = first_file(folder, "expected*.json", "expected.bindings")
        expected = json.loads(snapshot.read_text(encoding="utf-8-sig")) if snapshot else {}
        candidate = folder.name[0].lower() + folder.name[1:]
        selected = candidate if candidate in available else expected.get("Property", candidate)
        type_name, default = available.get(selected, ("composition behavior", "see the selected configuration"))
        parts += [f"## {candidate}", ""]
        if selected != candidate:
            parts += [f"The `{candidate}` fixture checks composition through the final `{selected}` value.", ""]
        parts += [
            f"- Result property: `{selected}`",
            f"- Type: `{type_name}`",
            f"- Source default: `{default}`",
            f"- Expected fixture value: `{json.dumps(expected.get('Expected'), ensure_ascii=False)}`",
            "",
            "### Configuration",
            "",
            "```json",
        ]
        config = first_file(folder, "config*.json")
        parts += [config.read_text(encoding="utf-8-sig").strip() if config else "{}", "```", ""]
        bindings = first_file(folder, "expected.bindings*.json")
        if bindings:
            markers = json.loads(bindings.read_text(encoding="utf-8-sig"))
            parts += ["### Generated output assertions", "", f"```{language}", "// Required markers"]
            parts += markers.get("Contains", [])
            parts += ["// Excluded markers"] + markers.get("NotContains", []) + ["```", ""]
    (ROOT / "docs" / output).write_text("\n".join(parts).rstrip() + "\n", encoding="utf-8")


def main() -> None:
    generate("BGCS", "CsCodeGeneratorConfig", "BGCS.Configuration.Tests", "config.md", "csharp")
    generate("BGCS.Cpp2C", "Cpp2CGeneratorConfig", "BGCS.Cpp2C.Configuration.Tests", "cpp2c.config.md", "cpp")


if __name__ == "__main__":
    main()
