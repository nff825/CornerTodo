#!/usr/bin/env python3
"""Isolated package regression checks; no downloaded code or real task data."""
from __future__ import annotations
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import stat
import struct
import tempfile
import zipfile

module_path = Path(__file__).with_name("build_bundle.py")
spec = importlib.util.spec_from_file_location("cornertodo_bundle", module_path)
bundle = importlib.util.module_from_spec(spec)
spec.loader.exec_module(bundle)

def run(output: Path) -> int:
    output.mkdir(parents=True, exist_ok=True)
    checks = 0
    def check(value, text):
        nonlocal checks
        checks += 1
        if not value:
            raise AssertionError(text)
    def rejected(action, text):
        nonlocal checks
        checks += 1
        try:
            action()
        except (bundle.BundleError, OSError, ValueError, struct.error):
            return
        raise AssertionError(text)
    with tempfile.TemporaryDirectory(prefix="package-tests-", dir=output) as temporary:
        root = Path(temporary).resolve()
        check(root.is_relative_to(output.resolve()), "test temporary root escaped")
        package = root / "app"
        package.mkdir()
        (package / "main").write_bytes(b"native payload fixture")
        (package / "notes.txt").write_text("sample only", encoding="utf-8")
        archive = root / "permissions.zip"
        report = bundle.make_zip(package, archive, {"main"})
        with zipfile.ZipFile(archive) as zipped:
            check(zipped.testzip() is None, "ZIP CRC")
            check(zipped.getinfo("app/main").create_system == 3, "UNIX ZIP creator")
            check((zipped.getinfo("app/main").external_attr >> 16) == stat.S_IFREG | 0o755, "executable permissions")
            check((zipped.getinfo("app/notes.txt").external_attr >> 16) == stat.S_IFREG | 0o644, "data permissions")
        check(report["unixPermissionsVerified"], "permission report")
        source = root / "source"
        source.mkdir()
        (source / "LICENSE").write_text("test-only fixture", encoding="utf-8")
        (source / "Main.cs").write_text("class Sample {}", encoding="utf-8")
        listing = root / "source-files.json"
        listing.write_text(json.dumps(["LICENSE", "Main.cs"]), encoding="utf-8")
        args = argparse.Namespace(source_root=source, source_list=listing, output_dir=root / "published", version="0.1.0-alpha.1")
        report = bundle.build_source(args)
        check(report["sourceFiles"] == 2, "explicit source inventory")
        manifest = json.loads((args.output_dir / report["manifest"]).read_text(encoding="utf-8"))
        check(not manifest["macOSLaunchVerified"], "source packaging must not claim native execution")
        for record in manifest["files"]:
            copied = args.output_dir / report["sourceDirectory"] / record["path"]
            check(hashlib.sha256(copied.read_bytes()).hexdigest() == record["sha256"], "source byte preservation")
        rejected(lambda: bundle.build_source(args), "existing output was overwritten")
        for invalid in ("../secret.txt", "/absolute.txt", "Data/todos.json", "output/local.json", "Main.cs", "missing.cs"):
            files = ["Main.cs", invalid] if invalid == "Main.cs" else [invalid]
            listing.write_text(json.dumps(files), encoding="utf-8")
            args.output_dir = root / ("invalid-" + str(checks))
            rejected(lambda: bundle.build_source(args), "unsafe/missing/duplicate source accepted: " + invalid)
        # A minimal thin x64 image has no code signature. Static parsing may
        # accept it with an explicit warning, never as a trusted/runnable app.
        x64 = root / "thin-x64"
        x64.write_bytes(struct.pack("<8I", 0xFEEDFACF, 0x01000007, 3, 2, 0, 0, 0, 0))
        record = bundle.inspect_macho(x64)[0]
        check(record["architecture"] == "x64" and not record["signature"]["present"], "unsigned x64 static result")
        arm = root / "thin-arm64"
        arm.write_bytes(struct.pack("<8I", 0xFEEDFACF, 0x0100000C, 0, 2, 0, 0, 0, 0))
        rejected(lambda: bundle.inspect_macho(arm), "ARM64 image without its signature accepted")
        truncated = root / "truncated"
        truncated.write_bytes(bytes.fromhex("cffaedfe"))
        rejected(lambda: bundle.inspect_macho(truncated), "truncated native image accepted")
        plain = root / "plain.txt"
        plain.write_text("not a native executable", encoding="utf-8")
        check(bundle.inspect_macho(plain) is None, "ordinary content mistaken for native code")
    print(f"PASS: {checks} isolated packaging checks (source allowlist, hashes, ZIP permissions and Mach-O parsing).")
    return checks

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output-dir", type=Path, required=True)
    arguments = parser.parse_args()
    run(arguments.output_dir)

