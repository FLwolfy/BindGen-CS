"""Prepare an independent C++ facade through the public bridge generation pipeline."""

import json
from pathlib import Path
import re
import shutil


def prepare(root, work, dotnet, tool, compiler, environment, run, sysroot=None):
    shutil.copytree(root / "tests/fixtures/CppFacade", work / "CppFacade")
    config = {
        "entryFiles": ["CppFacade/counter.hpp"],
        "outputPath": "Bridge",
        "compilerPath": str(compiler),
        "languageStandard": "c++17",
        "parseSystemIncludes": False,
        "parseComments": False,
        "parseMacros": False,
        "enableIncrementalCache": False,
    }
    if sysroot:
        config.update(targetId="emscripten-wasm32-emscripten", targetSysRoot=str(sysroot))
        config["systemIncludeFolders"] = [str(sysroot / "include/c++/v1"), str(sysroot / "include")]
    path = work / "bridge.json"
    path.write_text(json.dumps(config, indent=2), encoding="utf-8")
    run([dotnet, str(tool), "bridge", str(path)], work, work / "bridge-generation.log", environment)
    include = work / "Bridge/include"
    headers = (include / "Classes.h").read_text(encoding="utf-8")
    headers += (include / "common.h").read_text(encoding="utf-8")
    symbols = re.findall(r"^API\([^\n]+?\)\s+(\w+)\s*\(", headers, re.MULTILINE)
    required = {
        "BridgeCounterCreate", "BridgeCounterDestroy", "BridgeCounter_Add", "BridgeCounter_Read",
        "BridgeCounter_LiveCount", "BridgeCounterAsBridgeBaseB", "BridgeCounterFromBridgeBaseB",
    }
    if not required.issubset(symbols):
        raise RuntimeError("C++ bridge generation did not preserve the independent facade's required operations.")
    source = work / "Native/api.c"
    text = '#include "../Bridge/include/Classes.h"\n' + source.read_text(encoding="utf-8")
    text = text.replace("    #undef BGCS_FIXTURE_SYMBOL",
                        "\n".join("    BGCS_FIXTURE_SYMBOL(" + symbol + ")" for symbol in symbols)
                        + "\n    #undef BGCS_FIXTURE_SYMBOL")
    source.write_text(text, encoding="utf-8")
    return work / "Bridge/src/Classes.cpp", symbols


def cxx_compiler(compiler):
    compiler = Path(compiler)
    suffix = compiler.suffix
    stem = compiler.stem
    name = "clang++" if stem == "clang" else "g++" if stem in ("gcc", "cc") else None
    candidate = compiler.with_name(name + suffix) if name else None
    if candidate and candidate.is_file():
        return str(candidate)
    raise RuntimeError("The independent C++ fixture requires clang++ or g++ beside the selected C compiler.")
