#!/usr/bin/env python3
"""Check operative documentation against the source-only product boundary."""
from pathlib import Path
import re
import tempfile
import unittest
from urllib.parse import unquote, urlsplit
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parents[1]
OBSOLETE = (
    r"bindgen-cs\s+(?:build|native-build)\b",
    r"BindingHost\.CreateDefault\s*\(",
    r"(?<!bridge\.)\bcpp\.(?:typeLowerings|callableLowerings|nativeShims|familyBindings|templateInstantiations|sequenceInputAdapters|virtualCallbacks)",
    r"records/acceptance",
    r"native/metadata/bridge\.manifest\.json",
)


def is_historical(path: str, text: str) -> bool:
    if path.startswith(("docs/history/", "docs/releases/")):
        return True
    return re.search(r"(?mi)^> (?:Historical (?:design|plan|architecture|working)|历史(?:计划|设计|记录))", text[:450]) is not None


def audit(root: Path) -> list[str]:
    problems = []
    documents = [root / name for name in ("README.md", "README.cn.md", "CONTRIBUTING.md")]
    for folder in ("docs", "examples", "src", "extensions"):
        documents.extend((root / folder).rglob("*.md"))
    for path in sorted(set(documents)):
        if not path.is_file() or {"bin", "obj", "Generated"}.intersection(path.relative_to(root).parts):
            continue
        text = path.read_text(encoding="utf-8-sig")
        name = path.relative_to(root).as_posix()
        if is_historical(name, text):
            continue
        for pattern in OBSOLETE:
            for match in re.finditer(pattern, text):
                problems.append(f"{name}:{text.count(chr(10), 0, match.start()) + 1}: obsolete contract {match.group()}")
        prose = re.sub(r"(?ms)^```[^\n]*\n.*?^```\s*$", "", text)
        for target in re.findall(r'\[[^\]]+\]\(([^\s)]+)(?:\s+"[^"]*")?\)', prose):
            target = target.strip("<>")
            parsed = urlsplit(target)
            if parsed.scheme or target.startswith("#"):
                continue
            relative = unquote(parsed.path)
            destination = root / relative.lstrip("/") if relative.startswith("/") else path.parent / relative
            if not destination.exists():
                problems.append(f"{name}: missing local link {target}")
    for project in (root / "examples").rglob("*.csproj"):
        for item in ET.parse(project).getroot().iter():
            kind = item.tag.rsplit("}", 1)[-1]
            include = item.get("Include", "")
            if kind in ("None", "Content") and "/native/bin/" in include.replace("\\", "/"):
                problems.append(f"{project.relative_to(root)}: copies a nonexistent BGCS native binary output")
            if kind == "PackageReference" and include.startswith("BGCS"):
                version = item.get("Version", "")
                if version and version[0].isdigit() and int(version.split(".")[0]) < 4:
                    problems.append(f"{project.relative_to(root)}: uses a retired BGCS package version")
    return problems


class DocumentationContractsTests(unittest.TestCase):
    def test_current_documentation_and_example_projects_match_the_product_contract(self):
        self.assertEqual([], audit(ROOT))

    def test_historical_mention_does_not_hide_an_operative_obsolete_command(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            (root / "README.md").write_text("# Current usage\nHistorical receipts are references.\nbindgen-cs build bindgen.json\n", encoding="utf-8")
            self.assertEqual(1, len(audit(root)))

    def test_explicit_history_can_preserve_original_commands(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            (root / "docs/history").mkdir(parents=True)
            (root / "docs/history/old.md").write_text("bindgen-cs native-build old.json\n", encoding="utf-8")
            self.assertEqual([], audit(root))

    def test_link_validation_distinguishes_local_files_from_external_urls(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            (root / "README.md").write_text("[missing](missing.md) [external](https://example.invalid/path)\n", encoding="utf-8")
            self.assertEqual(["README.md: missing local link missing.md"], audit(root))

    def test_example_cannot_infer_binary_output_or_keep_an_old_runtime_package(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            (root / "examples").mkdir()
            (root / "examples/Consumer.csproj").write_text('<Project><ItemGroup><None Include="Generated/*/native/bin/*" /><PackageReference Include="BGCS.Runtime" Version="3.0.0" /></ItemGroup></Project>', encoding="utf-8")
            self.assertEqual(2, len(audit(root)))


if __name__ == "__main__":
    unittest.main(verbosity=2)
