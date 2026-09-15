"""Create a clean source deployment ZIP from the current working tree, not just HEAD."""
from pathlib import Path
import argparse
import hashlib
import json
import subprocess
import zipfile

ROOT = Path(__file__).resolve().parents[1]
PROJECTS = {
    "AI_WebApi", "AI_WebApi.Common", "AI_WebApi.Extensions", "AI_WebApi.IServices",
    "AI_WebApi.Model", "AI_WebApi.Repository", "AI_WebApi.Services", "AI_WebApi.Tests",
}
ROOT_FILES = {"AI_WebApi.sln", "Dockerfile", "compose.yaml", ".dockerignore", ".gitignore", ".env.ai_webapi.example"}
EXTRA_FILES = {"docs/DEPLOY.md", "scripts/package-deploy.py"}
DENIED_DIRS = {".git", "bin", "obj", "publish", "tmp", "logs", "testresults", "__pycache__", ".vs", ".idea", ".vscode", "data"}
DENIED_SUFFIXES = {".dll", ".exe", ".pdb", ".log", ".zip", ".7z", ".pem", ".key", ".pfx", ".p12", ".bak", ".pyc"}


def git(*args):
    return subprocess.check_output(["git", "-C", str(ROOT), *args])


def allowed(name):
    path = Path(name)
    parts = path.parts
    if name not in ROOT_FILES | EXTRA_FILES and parts[0] not in PROJECTS:
        return False
    if any(part.lower() in DENIED_DIRS for part in parts[:-1]):
        return False
    lower = path.name.lower()
    if path.suffix.lower() in DENIED_SUFFIXES or lower == "secrets.json":
        return False
    if lower.startswith(".env") and name != ".env.ai_webapi.example":
        return False
    if path.suffix.lower() == ".json" and lower.startswith("appsettings") and lower != "appsettings.example.json":
        return False
    return True


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", default=str(ROOT / "dist" / "aiwebapi-project-deploy.zip"))
    output = Path(parser.parse_args().output).resolve()
    if output.exists():
        raise SystemExit(f"Output exists; choose a new --output path: {output}")
    names = sorted({n.decode("utf-8") for n in git("ls-files", "--cached", "--others", "--exclude-standard", "-z").split(b"\0") if n})
    files = {}
    for name in names:
        if not allowed(name):
            continue
        source = ROOT / name
        if not source.is_file():
            continue
        if source.is_symlink() or not source.resolve().is_relative_to(ROOT):
            raise SystemExit(f"Unsafe package path: {name}")
        files[name] = source.read_bytes()
    required = ROOT_FILES | EXTRA_FILES | {f"{p}/{p}.csproj" for p in PROJECTS}
    missing = required - files.keys()
    if missing:
        raise SystemExit(f"Missing required files: {sorted(missing)}")
    # Verify configuration examples contain placeholders, not local runtime secrets.
    settings = json.loads(files["AI_WebApi/appsettings.example.json"])
    assert settings["MainDB"] == "project_main"
    assert len(settings["DBS"]) == 1 and "Database=project;" in settings["DBS"][0]["Connection"]
    assert "Pwd=<password>" in settings["DBS"][0]["Connection"]
    assert settings["IReGoTools"]["SessionRefSigningKey"].startswith("<")
    assert settings["IReGoTools"]["ReportImage"]["SigningKey"].startswith("<")
    manifest = {
        "format": "aiwebapi-source-deploy-v1", "base_commit": git("rev-parse", "HEAD").decode().strip(),
        "source": "current working tree including uncommitted changes", "data_generation": "project-20260915",
        "files": {name: hashlib.sha256(data).hexdigest() for name, data in files.items()},
    }
    files["PACKAGE-MANIFEST.json"] = (json.dumps(manifest, indent=2, ensure_ascii=False) + "\n").encode("utf-8")
    # Fail rather than silently package a working tree that changed during collection.
    for name in manifest["files"]:
        if (ROOT / name).read_bytes() != files[name]:
            raise SystemExit(f"File changed while packaging; retry: {name}")
    output.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(output, "x", zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for name, data in files.items():
            archive.writestr(name, data)
    with zipfile.ZipFile(output) as archive:
        assert archive.testzip() is None
        for name, digest in manifest["files"].items():
            assert hashlib.sha256(archive.read(name)).hexdigest() == digest
        assert all(allowed(name) or name == "PACKAGE-MANIFEST.json" for name in archive.namelist())
    digest = hashlib.sha256(output.read_bytes()).hexdigest()
    output.with_suffix(output.suffix + ".sha256").write_text(f"{digest}  {output.name}\n", encoding="utf-8")
    print(json.dumps({"package": str(output), "files": len(files), "bytes": output.stat().st_size, "sha256": digest}, ensure_ascii=False))


if __name__ == "__main__":
    main()
