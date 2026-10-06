#!/usr/bin/env python3
"""Archive original NuGet packages and tagged source as historical GitHub releases."""

import argparse
import concurrent.futures
import hashlib
import json
from pathlib import Path
import re
import subprocess
import sys
import urllib.error
import urllib.parse
import urllib.request
import xml.etree.ElementTree as ET
import zipfile


ROOT = Path(__file__).resolve().parent.parent
MANIFEST = ROOT / "scripts/historical-releases.json"
NUGET = "https://api.nuget.org/v3-flatcontainer"
PACKAGE_IDS = (
    "BGCS", "BGCS.Cpp2C", "BGCS.Runtime", "BGCS.Intermediate", "BGCS.CppAst",
    "BGCS.Core", "BGCS.Language", "BindGen-CS", "BindGen-CS.win-x64",
    "BindGen-CS.win-arm64", "BindGen-CS.linux-x64", "BindGen-CS.linux-arm64",
    "BindGen-CS.osx-arm64",
)
HEADINGS = (
    "Architecture and extensibility", "Parser portability", "Verification and packaging",
    "Requirements and scope", "Release provenance",
)
MAX_PACKAGE_BYTES = 250 * 1024 * 1024


def run(command, input_text=None):
    result = subprocess.run(command, cwd=ROOT, input=input_text, text=True,
                            encoding="utf-8", capture_output=True, timeout=300)
    if result.returncode:
        raise RuntimeError(result.stderr.strip() or result.stdout.strip())
    return result.stdout.strip()


def write_json(path, value):
    path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8", newline="\n")


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def load_plan():
    plan = json.loads(MANIFEST.read_text(encoding="utf-8"))
    if plan["repository"] != "FLwolfy/BindGen-CS":
        raise ValueError("Historical archives belong to the declared BGCS repository.")
    tags = set()
    for release in plan["releases"]:
        tag = release["tag"]
        if not re.fullmatch(r"(?:runtime-)?v\d+\.\d+\.\d+(?:-alpha\.\d+)?", tag):
            raise ValueError("Invalid archival tag: " + tag)
        if tag in tags or tag == plan["latest"]["tag"]:
            raise ValueError("Historical tags must be unique and exclude the current release.")
        tags.add(tag)
        if not re.fullmatch(r"[0-9a-f]{40}", release["commit"]):
            raise ValueError("Every archive must pin its original source commit.")
        if len(set(release["packages"])) != len(release["packages"]):
            raise ValueError("Duplicate package IDs in " + tag)
        if not set(release["packages"]).issubset(PACKAGE_IDS):
            raise ValueError("Unknown package ID in " + tag)
        source = run(["git", "rev-parse", release["sourceTag"] + "^{commit}"])
        if source != release["commit"]:
            raise ValueError("The original source tag changed: " + release["sourceTag"])
        notes = (ROOT / release["notes"]).read_text(encoding="utf-8")
        if not notes.startswith("# " + release["title"] + "\n"):
            raise ValueError("Release title differs from its reviewed notes: " + tag)
        if re.findall(r"^## (.+)$", notes, re.MULTILINE) != list(HEADINGS):
            raise ValueError("Release headings differ from the shared format: " + tag)
        if bool("-alpha." in tag) != release["prerelease"]:
            raise ValueError("Alpha tags must be marked as prereleases: " + tag)
        if not release["packages"] and not release["prerelease"]:
            raise ValueError("An unpublished source snapshot must be an alpha release.")
    return plan


def available_versions(package_id):
    url = NUGET + "/" + package_id.lower() + "/index.json"
    with urllib.request.urlopen(url, timeout=60) as response:
        return json.load(response)["versions"]


def inspect_package(path, release, package_id):
    with zipfile.ZipFile(path) as archive:
        names = [name for name in archive.namelist() if name.endswith(".nuspec")]
        if len(names) != 1 or archive.testzip() is not None:
            raise ValueError("Invalid original package archive: " + path.name)
        if archive.getinfo(names[0]).file_size > 1024 * 1024:
            raise ValueError("Package metadata exceeds its permitted size.")
        metadata = ET.fromstring(archive.read(names[0]))
        namespace = {"n": metadata.tag.split("}")[0].lstrip("{")}
        package = metadata.find("n:metadata", namespace)
        identity = package.findtext("n:id", namespaces=namespace)
        version = package.findtext("n:version", namespaces=namespace)
        repository = package.find("n:repository", namespace)
        if identity != package_id or version != release["packageVersion"]:
            raise ValueError("Package ID/version mismatch: " + path.name)
        if repository is None or repository.get("commit") not in release["packageCommits"]:
            raise ValueError("Unreviewed package source commit: " + path.name)
        if repository.get("url", "").rstrip("/").removesuffix(".git").lower() != \
                ("https://github.com/" + "FLwolfy/BindGen-CS").lower():
            raise ValueError("Package repository differs from BGCS: " + path.name)
        dependencies = [{"id": item.get("id"), "version": item.get("version")}
                        for item in package.findall(".//n:dependency", namespace)
                        if item.get("id") in PACKAGE_IDS]
        return {"id": identity, "version": version, "repositoryCommit": repository.get("commit"),
                "dependencies": dependencies, "filename": path.name, "size": path.stat().st_size,
                "sha256": digest(path)}


def download_package(release, package_id, output):
    version = release["packageVersion"]
    filename = package_id + "." + version + ".nupkg"
    url = NUGET + "/" + package_id.lower() + "/" + version + "/" + filename.lower()
    path = output / filename
    temporary = path.with_suffix(".download")
    try:
        with urllib.request.urlopen(url, timeout=90) as response, temporary.open("wb") as stream:
            size = 0
            while chunk := response.read(1024 * 1024):
                size += len(chunk)
                if size > MAX_PACKAGE_BYTES:
                    raise ValueError("NuGet package exceeds the archive size limit: " + filename)
                stream.write(chunk)
        result = inspect_package(temporary, release, package_id)
        temporary.replace(path)
        result.update(filename=filename, source=url)
        return result
    finally:
        temporary.unlink(missing_ok=True)


def prepare(plan, output):
    with concurrent.futures.ThreadPoolExecutor(max_workers=4) as executor:
        indexes = dict(zip(PACKAGE_IDS, executor.map(available_versions, PACKAGE_IDS)))
    output.mkdir(parents=True, exist_ok=True)
    for release in plan["releases"]:
        version = release["packageVersion"]
        published = {package for package, versions in indexes.items() if version in versions}
        if published != set(release["packages"]):
            raise ValueError("Published package set differs from the reviewed archive: " + release["tag"])
        directory = output / release["tag"]
        directory.mkdir(exist_ok=True)
        with concurrent.futures.ThreadPoolExecutor(max_workers=4) as executor:
            packages = list(executor.map(
                lambda package: download_package(release, package, directory), release["packages"]))
        missing = sorted({(dependency["id"], dependency["version"])
                          for package in packages for dependency in package["dependencies"]
                          if dependency["version"] not in indexes[dependency["id"]]})
        report = {"repository": plan["repository"], "tag": release["tag"],
                  "sourceTag": release["sourceTag"], "sourceCommit": release["commit"],
                  "prerelease": release["prerelease"], "rebuilt": False, "packages": packages,
                  "missingExactDependencyVersions": [{"id": pair[0], "version": pair[1]} for pair in missing]}
        write_json(directory / "release-archive.json", report)
        files = sorted(directory.glob("*.nupkg")) + [directory / "release-archive.json"]
        (directory / "SHA256SUMS").write_text(
            "".join(digest(path) + "  " + path.name + "\n" for path in files),
            encoding="utf-8", newline="\n")
        print(release["tag"] + ": " + str(len(packages)) + " original packages prepared.", flush=True)
    verify(plan, output)


def verify(plan, output):
    for release in plan["releases"]:
        directory = output / release["tag"]
        report = json.loads((directory / "release-archive.json").read_text(encoding="utf-8"))
        if any(report[key] != value for key, value in {
            "repository": plan["repository"], "tag": release["tag"],
            "sourceTag": release["sourceTag"], "sourceCommit": release["commit"],
            "prerelease": release["prerelease"],
        }.items()) or report["rebuilt"] is not False:
            raise ValueError("Archive source identity mismatch: " + release["tag"])
        expected = {package + "." + release["packageVersion"] + ".nupkg" for package in release["packages"]}
        expected.update(("release-archive.json", "SHA256SUMS"))
        if {path.name for path in directory.iterdir()} != expected:
            raise ValueError("Archive contains missing or unexpected files: " + release["tag"])
        hashes = {}
        for line in (directory / "SHA256SUMS").read_text(encoding="utf-8").splitlines():
            checksum, filename = line.split("  ", 1)
            if filename not in expected or filename == "SHA256SUMS" or filename in hashes:
                raise ValueError("Invalid archive checksum entry: " + filename)
            if digest(directory / filename) != checksum:
                raise ValueError("Archive checksum mismatch: " + filename)
            hashes[filename] = checksum
        if set(hashes) != expected - {"SHA256SUMS"}:
            raise ValueError("Archive checksum coverage is incomplete.")
        if {package["id"] for package in report["packages"]} != set(release["packages"]):
            raise ValueError("Archive package identities are incomplete.")
        for package in report["packages"]:
            filename = package["id"] + "." + release["packageVersion"] + ".nupkg"
            source = NUGET + "/" + package["id"].lower() + "/" + release["packageVersion"] + "/" + filename.lower()
            if package["filename"] != filename or package["source"] != source:
                raise ValueError("Archive package origin mismatch: " + filename)
            actual = inspect_package(directory / package["filename"], release, package["id"])
            if any(actual[key] != package[key] for key in actual):
                raise ValueError("Archive package evidence mismatch: " + package["filename"])
    print("All historical archives verified.", flush=True)


def github_api(repository, endpoint, payload=None, allow_missing=False, method="GET"):
    command = ["gh", "api", "repos/" + repository + "/" + endpoint,
               "--method", method, "-H", "X-GitHub-Api-Version: 2022-11-28"]
    if payload is not None:
        command += ["--input", "-"]
    try:
        return json.loads(run(command, json.dumps(payload) if payload is not None else None))
    except RuntimeError as error:
        if allow_missing and "HTTP 404" in str(error):
            return None
        raise RuntimeError(endpoint + ": " + str(error)) from error


def upload_release_asset(repository, release_id, path):
    endpoint = ("https://uploads.github.com/repos/" + repository + "/releases/"
                + str(release_id) + "/assets?name=" + urllib.parse.quote(path.name, safe=""))
    command = ["gh", "api", endpoint, "--method", "POST", "--input", str(path),
               "-H", "Content-Type: application/octet-stream",
               "-H", "Content-Length: " + str(path.stat().st_size),
               "-H", "X-GitHub-Api-Version: 2022-11-28"]
    return json.loads(run(command))


def remote_tag(repository, tag):
    result = github_api(repository, "git/ref/tags/" + tag, allow_missing=True)
    if result is None:
        return None
    target = result["object"]
    while target["type"] == "tag":
        target = github_api(repository, "git/tags/" + target["sha"])["object"]
    return target["sha"]


def find_release(repository, tag):
    # The tag endpoint excludes drafts; the authenticated list includes them.
    page = 1
    while True:
        releases = github_api(repository, "releases?per_page=100&page=" + str(page))
        matches = [release for release in releases if release["tag_name"] == tag]
        if len(matches) > 1:
            raise ValueError("Multiple releases use the archival tag: " + tag)
        if matches:
            return matches[0]
        if len(releases) < 100:
            return None
        page += 1


def publish(plan, output):
    verify(plan, output)
    repository = plan["repository"]
    latest = github_api(repository, "releases/latest")
    if latest["tag_name"] != plan["latest"]["tag"]:
        raise ValueError("The current latest release differs from the reviewed archive plan.")
    tagged = []
    for release in plan["releases"]:
        if remote_tag(repository, release["sourceTag"]) != release["commit"]:
            raise ValueError("The original remote tag changed: " + release["sourceTag"])
        existing_tag = remote_tag(repository, release["tag"])
        if existing_tag is not None and existing_tag != release["commit"]:
            raise ValueError("The archival tag already points to another commit.")
        tagged.append((release, existing_tag))

    results = []
    # Finish existing source tags before provisioning new archival aliases.
    for release, existing_tag in sorted(tagged, key=lambda item: item[1] is None):
        notes = ROOT / release["notes"]
        value = find_release(repository, release["tag"])
        if value is None:
            request = {"tag_name": release["tag"], "name": release["title"],
                       "body": notes.read_text(encoding="utf-8"), "draft": True,
                       "prerelease": release["prerelease"], "make_latest": "false"}
            if existing_tag is None:
                request["target_commitish"] = release["commit"]
            # The creation response owns the draft ID; release-list indexing can lag.
            value = github_api(repository, "releases", request, method="POST")
            if value["tag_name"] != release["tag"] or not value["draft"]:
                raise ValueError("The created release draft has the wrong identity.")
        directory = output / release["tag"]
        expected = {path.name: "sha256:" + digest(path) for path in directory.iterdir()}
        assets = {asset["name"]: asset for asset in value["assets"]}
        if set(assets) - set(expected):
            raise ValueError("Existing historical release has unexpected assets: " + release["tag"])
        missing = []
        for filename, checksum in expected.items():
            if filename in assets:
                if assets[filename].get("digest") != checksum:
                    raise ValueError("Existing release bytes differ: " + filename)
            else:
                missing.append(directory / filename)
        for path in sorted(missing):
            uploaded = upload_release_asset(repository, value["id"], path)
            if uploaded["name"] != path.name or uploaded.get("digest") != expected[path.name]:
                raise ValueError("The uploaded asset differs from its archive: " + path.name)
        uploaded = github_api(repository, "releases/" + str(value["id"]))
        actual = {asset["name"]: asset.get("digest") for asset in uploaded["assets"]}
        if actual != expected:
            raise ValueError("Uploaded release hashes or file set differ: " + release["tag"])
        published = github_api(repository, "releases/" + str(value["id"]), {
            "name": release["title"], "body": notes.read_text(encoding="utf-8"),
            "prerelease": release["prerelease"], "draft": False, "make_latest": "false",
        }, method="PATCH")
        if published["draft"] or remote_tag(repository, release["tag"]) != release["commit"]:
            raise ValueError("Published archive has the wrong tag or draft state.")
        results.append({"tag": release["tag"], "url": published["html_url"],
                        "prerelease": published["prerelease"], "assets": len(actual), "verified": True})
        print("Published " + published["html_url"], flush=True)
    if remote_tag(repository, plan["latest"]["tag"]) != plan["latest"]["commit"]:
        raise ValueError("The current release tag changed.")
    github_api(repository, "releases/" + str(latest["id"]), {
        "body": (ROOT / plan["latest"]["notes"]).read_text(encoding="utf-8"),
    }, method="PATCH")
    if github_api(repository, "releases/latest")["tag_name"] != plan["latest"]["tag"]:
        raise ValueError("Historical archives unexpectedly changed the latest release.")
    write_json(output / "publication-report.json", {"releases": results, "latest": plan["latest"]["tag"]})


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("operation", choices=("prepare", "verify", "publish"))
    parser.add_argument("--output", type=Path, default=ROOT / "artifacts/historical-release-archives")
    args = parser.parse_args()
    plan = load_plan()
    {"prepare": prepare, "verify": verify, "publish": publish}[args.operation](plan, args.output.resolve())


if __name__ == "__main__":
    try:
        main()
    except (OSError, RuntimeError, ValueError, KeyError, TypeError, zipfile.BadZipFile, ET.ParseError) as error:
        print("Historical release archival failed: " + str(error), file=sys.stderr)
        raise SystemExit(1)
