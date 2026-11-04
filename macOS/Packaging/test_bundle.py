#!/usr/bin/env python3
"""Isolated package regression checks; no downloaded code or real task data."""
from __future__ import annotations
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import plistlib
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
        identity = bundle.version_metadata("0.2.0-alpha.1", "0.2.1")
        check(identity == {"version": "0.2.0-alpha.1", "bundleShortVersion": "0.2.0", "buildNumber": "0.2.1"}, "alpha identity mapping")
        next_identity = bundle.version_metadata("0.2.0-alpha.2", "0.2.2")
        check(tuple(map(int, next_identity["buildNumber"].split("."))) > tuple(map(int, identity["buildNumber"].split("."))), "alpha build was not incremented independently")
        for version in ("../bad", "0.2", "00.2.0", "0.2.0-", "0.2.0/alpha", "0.2.0-alpha..1"):
            rejected(lambda value=version: bundle.version_metadata(value, "0.2.1"), "invalid public version accepted")
        for number in (None, "0.2.0-alpha.1", "0.2", "00.2.1", "0.2.01", "0.2.1.0", "0.2.100", "0.3.1", "-1.2.1"):
            rejected(lambda value=number: bundle.version_metadata("0.2.0-alpha.1", value), "missing, mismatched or invalid build number accepted")
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
        (source / "macOS").mkdir()
        (source / "macOS" / "VERSION").write_text("0.2.0-alpha.1\n", encoding="utf-8")
        listing = root / "source-files.json"
        listing.write_text(json.dumps(["LICENSE", "Main.cs", "macOS/VERSION"]), encoding="utf-8")
        args = argparse.Namespace(source_root=source, source_list=listing, output_dir=root / "published", version="0.2.0-alpha.1", build_number="0.2.1")
        report = bundle.build_source(args)
        check(report["sourceFiles"] == 3, "explicit source inventory")
        manifest = json.loads((args.output_dir / report["manifest"]).read_text(encoding="utf-8"))
        check(not manifest["macOSLaunchVerified"], "source packaging must not claim native execution")
        check(manifest["buildNumber"] == "0.2.1" and manifest["bundleShortVersion"] == "0.2.0", "source build identity lost")
        for record in manifest["files"]:
            copied = args.output_dir / report["sourceDirectory"] / record["path"]
            check(hashlib.sha256(copied.read_bytes()).hexdigest() == record["sha256"], "source byte preservation")
        rejected(lambda: bundle.build_source(args), "existing output was overwritten")
        original_version = args.version
        args.version = "0.2.0-alpha.2"; args.output_dir = root / "mismatched-source-version"
        rejected(lambda: bundle.build_source(args), "source version mismatch accepted")
        check(not args.output_dir.exists(), "source version validation wrote output")
        args.version = original_version
        for invalid in ("../secret.txt", "/absolute.txt", "Data/todos.json", "output/local.json", "backups/tasks.json", "__pycache__/private.pyc", ".git/config", "Main.cs", "missing.cs"):
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
        publish = root / "publish-x64"
        publish.mkdir()
        (publish / bundle.EXECUTABLE).write_bytes(x64.read_bytes())
        (publish / (bundle.EXECUTABLE + ".dll")).write_bytes(b"isolated managed assembly fixture")
        (publish / (bundle.EXECUTABLE + ".deps.json")).write_text(json.dumps({"libraries": {"CornerTodo.MacDesktop/0.2.0-alpha.1": {"type": "project"}}}), encoding="utf-8")
        (publish / (bundle.EXECUTABLE + ".runtimeconfig.json")).write_text("{}", encoding="utf-8")
        guide = root / "guide.md"; guide.write_text("Synthetic guide; see FEEDBACK.txt", encoding="utf-8")
        feedback = root / "feedback.txt"; feedback.write_text("Version: 0.2.0-alpha.1\nDevice:\nResult:\n", encoding="utf-8")
        native_args = argparse.Namespace(publish_dir=publish, arch="x64", version="0.2.0-alpha.1", build_number="0.2.1", verify_publish=False,
                                         output_dir=root / "native-package", icon_png=None, guide=guide, feedback=feedback)
        native_report = bundle.build(native_args)
        native_manifest = json.loads((native_args.output_dir / native_report["manifest"]).read_text(encoding="utf-8"))
        info_path = native_args.output_dir / native_report["bundle"] / "Contents" / "Info.plist"
        with info_path.open("rb") as stream:
            info = plistlib.load(stream)
        check(info["CFBundleShortVersionString"] == "0.2.0" and info["CFBundleVersion"] == "0.2.1"
              and info["CornerTodoExperimentalVersion"] == "0.2.0-alpha.1", "Info.plist version or build is incorrect")
        check(native_manifest["version"] == native_report["version"] == "0.2.0-alpha.1"
              and native_manifest["buildNumber"] == native_report["buildNumber"] == "0.2.1", "native report identity differs")
        check(not native_manifest["macOSLaunchVerified"] and not native_manifest["developerIdSigned"] and not native_manifest["notarized"],
              "static package processing invented native launch or trust")
        support = {entry["path"]: entry for entry in native_manifest["supportingFiles"]}
        check(set(support) == {"CornerTodo.app/Contents/Info.plist", "测试说明.md", "FEEDBACK.txt"}, "guide, feedback or bundle metadata missing from manifest")
        with zipfile.ZipFile(native_args.output_dir / native_report["archive"]) as zipped:
            for name, entry in support.items():
                check(hashlib.sha256(zipped.read(Path(native_report["bundle"]).parts[0] + "/" + name)).hexdigest() == entry["sha256"],
                      "support manifest differs from delivered ZIP")
        rejected(lambda: bundle.build(native_args), "existing native output was overwritten")
        native_args.version = "0.2.0-alpha.2"; native_args.output_dir = root / "mismatched-publish-version"
        rejected(lambda: bundle.build(native_args), "published project version mismatch was accepted")
        check(not native_args.output_dir.exists(), "published identity validation wrote partial output")
        arm = root / "thin-arm64"
        arm.write_bytes(struct.pack("<8I", 0xFEEDFACF, 0x0100000C, 0, 2, 0, 0, 0, 0))
        rejected(lambda: bundle.inspect_macho(arm), "ARM64 image without its signature accepted")
        truncated = root / "truncated"
        truncated.write_bytes(bytes.fromhex("cffaedfe"))
        rejected(lambda: bundle.inspect_macho(truncated), "truncated native image accepted")
        plain = root / "plain.txt"
        plain.write_text("not a native executable", encoding="utf-8")
        check(bundle.inspect_macho(plain) is None, "ordinary content mistaken for native code")
    print(f"PASS: {checks} isolated packaging checks (release/build identity, source allowlist, hashes, ZIP permissions and Mach-O parsing).")
    return checks

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output-dir", type=Path, required=True)
    arguments = parser.parse_args()
    run(arguments.output_dir)

