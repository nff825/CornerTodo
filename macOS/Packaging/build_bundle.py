#!/usr/bin/env python3
"""Build an experimental macOS .app/ZIP from unmodified dotnet publish files.

This checks Mach-O code-page hashes, not Developer ID, CMS trust, Gatekeeper,
notarization, bundle resource sealing, or actual execution on a Mac.
Only Python's standard library is used. The script never invokes a shell.
"""

from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import json
import math
import os
from pathlib import Path
import plistlib
import re
import shutil
import stat
import struct
import sys
import zipfile


EXECUTABLE = "CornerTodo.MacDesktop"
CPU_NAMES = {0x0100000C: "arm64", 0x01000007: "x64", 7: "x86", 12: "arm"}
THIN_MAGICS = {
    b"\xcf\xfa\xed\xfe": ("<", True),
    b"\xfe\xed\xfa\xcf": (">", True),
    b"\xce\xfa\xed\xfe": ("<", False),
    b"\xfe\xed\xfa\xce": (">", False),
}
FAT_MAGICS = {
    b"\xca\xfe\xba\xbe": (">", False),
    b"\xbe\xba\xfe\xca": ("<", False),
    b"\xca\xfe\xba\xbf": (">", True),
    b"\xbf\xba\xfe\xca": ("<", True),
}
HASHES = {1: ("sha1", 20), 2: ("sha256", 32), 3: ("sha256", 20),
          4: ("sha384", 48), 5: ("sha512", 64)}


class BundleError(ValueError):
    pass


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def bounds(data: bytes, offset: int, length: int, label: str) -> None:
    if offset < 0 or length < 0 or offset + length > len(data):
        raise BundleError(f"{label}: range outside file")


def be32(data: bytes, offset: int) -> int:
    bounds(data, offset, 4, "signature integer")
    return struct.unpack_from(">I", data, offset)[0]


def read_c_string(data: bytes, offset: int, limit: int) -> str:
    if not 0 <= offset < limit <= len(data):
        raise BundleError("invalid signature identifier range")
    end = data.find(b"\0", offset, limit)
    if end < 0:
        raise BundleError("signature identifier is not terminated")
    return data[offset:end].decode("utf-8", errors="replace")


def verify_code_directory(data: bytes, slice_offset: int, slice_size: int,
                          signature_file_offset: int, cd: bytes) -> dict:
    if len(cd) < 44 or be32(cd, 0) != 0xFADE0C02:
        raise BundleError("invalid CodeDirectory header")
    length, version, flags, hash_offset, ident_offset, special_slots, code_slots, code_limit = \
        struct.unpack_from(">8I", cd, 4)
    if length != len(cd):
        raise BundleError("CodeDirectory length mismatch")
    hash_size, hash_type, platform, page_exponent = struct.unpack_from("4B", cd, 36)
    minimum = 44
    if version >= 0x20100:
        minimum = 48
        if len(cd) < minimum or be32(cd, 44) != 0:
            raise BundleError("unsupported or invalid CodeDirectory scatter vector")
    if version >= 0x20200:
        minimum = 52
    if version >= 0x20300:
        minimum = 64
        if len(cd) < minimum:
            raise BundleError("truncated CodeDirectory 64-bit limit")
        wide_limit = struct.unpack_from(">Q", cd, 56)[0]
        if wide_limit:
            code_limit = wide_limit
    if version >= 0x20400:
        minimum = 88
    if version >= 0x20500:
        minimum = 96
    if version >= 0x20600:
        minimum = 108
    if len(cd) < minimum:
        raise BundleError("CodeDirectory version/header length mismatch")
    if hash_type not in HASHES or hash_size != HASHES[hash_type][1]:
        raise BundleError(f"unsupported CodeDirectory hash format {hash_type}/{hash_size}")
    if page_exponent > 30:
        raise BundleError("invalid CodeDirectory page size")
    if code_limit <= 0 or code_limit > slice_size or code_limit > signature_file_offset:
        raise BundleError("invalid CodeDirectory code limit")
    page_size = (1 << page_exponent) if page_exponent else code_limit
    expected_slots = (code_limit + page_size - 1) // page_size
    if code_slots != expected_slots:
        raise BundleError(f"CodeDirectory code-slot mismatch: {code_slots} != {expected_slots}")
    if hash_offset - special_slots * hash_size < minimum:
        raise BundleError("CodeDirectory hash table overlaps its header")
    if hash_offset + code_slots * hash_size > length:
        raise BundleError("CodeDirectory hash table exceeds its blob")
    identifier = read_c_string(cd, ident_offset, length)
    algorithm = HASHES[hash_type][0]
    for slot in range(code_slots):
        start = slot * page_size
        end = min(start + page_size, code_limit)
        actual = hashlib.new(algorithm, data[slice_offset + start:slice_offset + end]).digest()[:hash_size]
        expected = cd[hash_offset + slot * hash_size:hash_offset + (slot + 1) * hash_size]
        if actual != expected:
            raise BundleError(f"CodeDirectory page hash mismatch at code slot {slot}")
    directory_digest = hashlib.new(algorithm, cd).digest()
    return {"identifier": identifier, "version": hex(version), "flags": hex(flags),
            "adHoc": bool(flags & 2), "hashAlgorithm": algorithm, "hashSize": hash_size,
            "pageSize": page_size, "codeLimit": code_limit, "codeSlots": code_slots,
            "specialSlots": special_slots, "codePageHashesVerified": True,
            "specialSlotHashesVerified": False, "codeDirectoryDigest": directory_digest.hex(),
            "cdHash": directory_digest[:20].hex(), "platform": platform}


def verify_signature(data: bytes, slice_offset: int, slice_size: int,
                     signature_offset: int, signature_size: int) -> dict:
    if signature_offset < 0 or signature_size < 8 or signature_offset + signature_size > slice_size:
        raise BundleError("LC_CODE_SIGNATURE range exceeds Mach-O slice")
    signature = data[slice_offset + signature_offset:slice_offset + signature_offset + signature_size]
    magic, length = struct.unpack_from(">II", signature)
    if length < 8 or length > len(signature):
        raise BundleError("invalid embedded signature length")
    signature = signature[:length]
    directories = []
    cms_present = False
    if magic == 0xFADE0C02:
        directories.append(verify_code_directory(data, slice_offset, slice_size,
                                                 signature_offset, signature))
    elif magic == 0xFADE0CC0:
        if len(signature) < 12:
            raise BundleError("truncated signature SuperBlob")
        count = be32(signature, 8)
        table_end = 12 + count * 8
        if count > 4096 or table_end > length:
            raise BundleError("invalid signature blob table")
        seen_ranges = []
        for index in range(count):
            blob_type, offset = struct.unpack_from(">II", signature, 12 + index * 8)
            if offset < table_end or offset + 8 > length:
                raise BundleError("signature sub-blob offset outside SuperBlob")
            blob_magic, blob_length = struct.unpack_from(">II", signature, offset)
            if blob_length < 8 or offset + blob_length > length:
                raise BundleError("signature sub-blob length outside SuperBlob")
            if any(offset < old_end and offset + blob_length > old_start for old_start, old_end in seen_ranges):
                raise BundleError("overlapping signature sub-blobs")
            seen_ranges.append((offset, offset + blob_length))
            if blob_magic == 0xFADE0C02:
                record = verify_code_directory(data, slice_offset, slice_size,
                                               signature_offset, signature[offset:offset + blob_length])
                record["slotType"] = hex(blob_type)
                directories.append(record)
            if blob_type == 0x10000:
                cms_present = True
    else:
        raise BundleError(f"unsupported embedded signature magic {magic:#x}")
    if not directories:
        raise BundleError("embedded signature has no CodeDirectory")
    return {"present": True, "codePageHashesVerified": True,
            "cmsBlobPresent": cms_present, "cmsTrustVerified": False,
            "codeDirectories": directories}


def inspect_slice(data: bytes, offset: int, size: int, expected_cpu: int | None = None) -> dict:
    bounds(data, offset, size, "Mach-O slice")
    magic = data[offset:offset + 4]
    if magic not in THIN_MAGICS:
        raise BundleError("fat slice does not contain a supported Mach-O image")
    endian, is_64 = THIN_MAGICS[magic]
    header_size = 32 if is_64 else 28
    if size < header_size:
        raise BundleError("truncated Mach-O header")
    cpu, subtype, file_type, command_count, command_bytes = struct.unpack_from(endian + "5I", data, offset + 4)
    if expected_cpu is not None and cpu != expected_cpu:
        raise BundleError("fat architecture differs from contained Mach-O CPU")
    if command_count > 65536 or header_size + command_bytes > size:
        raise BundleError("invalid Mach-O load-command table")
    command_pos = offset + header_size
    command_end = command_pos + command_bytes
    signature = None
    for _ in range(command_count):
        if command_pos + 8 > command_end:
            raise BundleError("truncated Mach-O load command")
        command, command_size = struct.unpack_from(endian + "2I", data, command_pos)
        if command_size < 8 or command_size % 4 or command_pos + command_size > command_end:
            raise BundleError("invalid Mach-O load-command size")
        if command == 0x1D:  # LC_CODE_SIGNATURE / linkedit_data_command
            if command_size < 16 or signature is not None:
                raise BundleError("invalid or duplicate LC_CODE_SIGNATURE")
            signature = struct.unpack_from(endian + "2I", data, command_pos + 8)
        command_pos += command_size
    if command_pos != command_end:
        raise BundleError("Mach-O load-command bytes do not match the header")
    result = {"architecture": CPU_NAMES.get(cpu, f"cpu-{cpu:#x}"), "cpuType": hex(cpu),
              "cpuSubtype": hex(subtype), "fileType": file_type, "is64Bit": is_64,
              "sliceOffset": offset, "sliceSize": size}
    if signature is None:
        if cpu == 0x0100000C:
            raise BundleError("arm64 Mach-O lacks the required embedded code signature")
        result["signature"] = {"present": False, "codePageHashesVerified": False,
                               "warning": "No embedded signature; macOS launch and policy remain unverified."}
    else:
        result["signature"] = verify_signature(data, offset, size, *signature)
    return result


def inspect_macho(path: Path) -> list[dict] | None:
    data = path.read_bytes()
    magic = data[:4]
    if magic in THIN_MAGICS:
        return [inspect_slice(data, 0, len(data))]
    if magic not in FAT_MAGICS:
        return None
    endian, is_64 = FAT_MAGICS[magic]
    bounds(data, 0, 8, "fat header")
    count = struct.unpack_from(endian + "I", data, 4)[0]
    record_size = 32 if is_64 else 20
    if not 0 < count <= 128:
        raise BundleError("invalid Mach-O fat architecture count")
    table_end = 8 + count * record_size
    bounds(data, 0, table_end, "fat architecture table")
    records, occupied = [], []
    for index in range(count):
        pos = 8 + index * record_size
        if is_64:
            cpu, _, offset, size, _, _ = struct.unpack_from(endian + "2I2Q2I", data, pos)
        else:
            cpu, _, offset, size, _ = struct.unpack_from(endian + "5I", data, pos)
        if offset < table_end or size <= 0:
            raise BundleError("fat slice overlaps its architecture table")
        if any(offset < old_end and offset + size > old_start for old_start, old_end in occupied):
            raise BundleError("overlapping fat Mach-O slices")
        occupied.append((offset, offset + size))
        records.append(inspect_slice(data, offset, size, cpu))
    return records


def inspect_publish(publish_dir: Path, arch: str) -> dict:
    required = [EXECUTABLE, EXECUTABLE + ".dll", EXECUTABLE + ".deps.json",
                EXECUTABLE + ".runtimeconfig.json"]
    for name in required:
        if not (publish_dir / name).is_file():
            raise BundleError(f"publish output is missing {name}")
    files, native, warnings = [], [], []
    for path in sorted(publish_dir.rglob("*")):
        relative = path.relative_to(publish_dir).as_posix()
        if path.is_symlink():
            target = os.readlink(path)
            if Path(target).is_absolute() or not path.resolve().is_relative_to(publish_dir):
                raise BundleError(f"publish symlink points outside its root: {relative}")
            files.append({"path": relative, "symlink": target})
            continue
        if not path.is_file():
            continue
        entry = {"path": relative, "bytes": path.stat().st_size, "sha256": sha256_file(path)}
        files.append(entry)
        try:
            slices = inspect_macho(path)
        except BundleError as error:
            raise BundleError(f"{relative}: {error}") from error
        if slices is None:
            if relative == EXECUTABLE or path.suffix.lower() == ".dylib":
                raise BundleError(f"{relative}: expected a Mach-O executable/library")
            continue
        if arch not in {record["architecture"] for record in slices}:
            raise BundleError(f"{relative}: no {arch} slice in native dependency")
        for record in slices:
            if not record["signature"]["present"]:
                warnings.append(f"{relative} ({record['architecture']}): no embedded signature")
        native.append({"path": relative, "sha256": entry["sha256"], "slices": slices})
    if not any(record["path"] == EXECUTABLE for record in native):
        raise BundleError("apphost did not pass Mach-O validation")
    return {"publishFileCount": len(files), "dependencyFileCount": len(files) - 1,
            "nativeFileCount": len(native), "files": files, "nativeFiles": native,
            "warnings": warnings, "nativeLaunchVerified": False,
            "appleCodeSignValidationRun": False, "cmsTrustVerified": False,
            "bundleResourceSealVerified": False}


def zip_entry(name: str, mode: int, directory: bool = False) -> zipfile.ZipInfo:
    entry = zipfile.ZipInfo(name, date_time=(2026, 1, 1, 0, 0, 0))
    entry.create_system = 3  # UNIX; required in addition to external_attr.
    entry.external_attr = mode << 16
    if directory:
        entry.external_attr |= 0x10
    entry.compress_type = zipfile.ZIP_DEFLATED
    return entry


def make_zip(package_dir: Path, destination: Path, native_paths: set[str]) -> dict:
    expected_modes = {}
    with zipfile.ZipFile(destination, "x", compression=zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
        paths = [package_dir] + sorted(package_dir.rglob("*"))
        for path in paths:
            name = path.relative_to(package_dir.parent).as_posix()
            if path.is_symlink():
                mode = stat.S_IFLNK | 0o777
                payload = os.readlink(path).encode("utf-8")
            elif path.is_dir():
                name += "/"
                mode = stat.S_IFDIR | 0o755
                payload = b""
            elif path.is_file():
                relative = path.relative_to(package_dir).as_posix()
                executable = relative in native_paths or path.suffix.lower() == ".dylib"
                mode = stat.S_IFREG | (0o755 if executable else 0o644)
                payload = path.read_bytes()
            else:
                raise BundleError(f"unsupported package member: {name}")
            expected_modes[name] = mode
            archive.writestr(zip_entry(name, mode, path.is_dir() and not path.is_symlink()), payload)
    with zipfile.ZipFile(destination, "r") as archive:
        bad = archive.testzip()
        if bad is not None:
            raise BundleError(f"ZIP CRC check failed: {bad}")
        for member in archive.infolist():
            if member.create_system != 3 or (member.external_attr >> 16) != expected_modes[member.filename]:
                raise BundleError(f"ZIP UNIX permission mismatch: {member.filename}")
        return {"entryCount": len(archive.infolist()), "crcVerified": True,
                "unixCreateSystemVerified": True, "unixPermissionsVerified": True,
                "mainExecutableMode": "0755", "symlinksPreserved": True}


def build(args: argparse.Namespace) -> dict:
    if args.publish_dir is None or args.arch is None:
        raise BundleError("--publish-dir and --arch are required for an application bundle")
    publish_dir = args.publish_dir.resolve()
    if not publish_dir.is_dir():
        raise BundleError("publish directory does not exist")
    inspection = inspect_publish(publish_dir, args.arch)
    if args.verify_publish:
        return {"architecture": args.arch, "version": args.version,
                "verificationScope": "Static Mach-O code-page hashes only; no Mac launch or Apple trust evaluation.",
                **inspection}
    if args.output_dir is None:
        raise BundleError("--output-dir is required when building a bundle")
    if re.fullmatch(r"\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?", args.version) is None:
        raise BundleError("invalid version")
    output_dir = args.output_dir.resolve()
    if output_dir.is_relative_to(publish_dir):
        raise BundleError("output directory must not be inside publish output")
    output_dir.mkdir(parents=True, exist_ok=True)
    package_name = f"CornerTodo-macos-v{args.version}-{args.arch}-experimental"
    package_dir = output_dir / package_name
    archive_path = output_dir / (package_name + ".zip")
    manifest_path = output_dir / (package_name + ".manifest.json")
    checksum_path = output_dir / (package_name + ".zip.sha256")
    for destination in (package_dir, archive_path, manifest_path, checksum_path):
        if destination.exists():
            raise BundleError(f"output already exists; use a fresh output directory: {destination.name}")
    app_dir = package_dir / "CornerTodo.app"
    contents = app_dir / "Contents"
    binary_dir = contents / "MacOS"
    resources = contents / "Resources"
    resources.mkdir(parents=True)
    shutil.copytree(publish_dir, binary_dir, symlinks=True)
    for entry in inspection["files"]:
        if "sha256" in entry and sha256_file(binary_dir / entry["path"]) != entry["sha256"]:
            raise BundleError(f"copied publish file changed: {entry['path']}")
    base_version = args.version.split("-", 1)[0]
    metadata = {
        "CFBundleIdentifier": "org.cornertodo.desktop", "CFBundleExecutable": EXECUTABLE,
        "CFBundleName": "CornerTodo", "CFBundleDisplayName": "CornerTodo Experimental",
        "CFBundlePackageType": "APPL", "CFBundleInfoDictionaryVersion": "6.0",
        "CFBundleShortVersionString": base_version, "CFBundleVersion": base_version,
        "CFBundleDevelopmentRegion": "zh_CN", "LSMinimumSystemVersion": "14.0",
        "NSHighResolutionCapable": True, "NSPrincipalClass": "NSApplication", "LSUIElement": True,
        "CFBundleGetInfoString": f"CornerTodo {args.version} Experimental - cross-compiled; not Mac-tested",
        "CornerTodoExperimentalVersion": args.version,
        "CornerTodoDistributionStatus": "No Developer ID signing or notarization; internal test build",
    }
    with (contents / "Info.plist").open("wb") as stream:
        plistlib.dump(metadata, stream, sort_keys=False)
    # PNG is retained only as a resource. Do not claim it is an .icns icon.
    if args.icon_png is not None:
        if args.icon_png.read_bytes()[:8] != b"\x89PNG\r\n\x1a\n":
            raise BundleError("--icon-png is not a PNG file")
        shutil.copyfile(args.icon_png, resources / "CornerTodo.png")
    if args.guide is not None:
        shutil.copyfile(args.guide, package_dir / "测试说明.md")
    native_paths = {"CornerTodo.app/Contents/MacOS/" + entry["path"] for entry in inspection["nativeFiles"]}
    zip_checks = make_zip(package_dir, archive_path, native_paths)
    archive_hash = sha256_file(archive_path)
    checksum_path.write_text(archive_hash + "  " + archive_path.name + "\n", encoding="ascii")
    manifest = {
        "product": "CornerTodo", "version": args.version, "status": "experimental",
        "platform": "macOS", "minimumMacOSVersion": "14.0", "architecture": args.arch,
        "builtAtUtc": dt.datetime.now(dt.timezone.utc).isoformat(), "selfContained": True,
        "bundle": package_name + "/CornerTodo.app", "executable": EXECUTABLE,
        "developerIdSigned": False, "notarized": False,
        "verificationScope": "Static Mach-O code-page hashes, copied bytes, ZIP CRC and UNIX permissions. No native launch, CMS trust, Gatekeeper or bundle resource-seal verification.",
        "publish": inspection, "zip": {"file": archive_path.name, "bytes": archive_path.stat().st_size,
                                        "sha256": archive_hash, **zip_checks},
    }
    manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    return {"archive": archive_path.name, "manifest": manifest_path.name,
            "checksum": checksum_path.name, "bundle": package_name + "/CornerTodo.app",
            "sha256": archive_hash, "publishFileCount": inspection["publishFileCount"],
            "nativeFileCount": inspection["nativeFileCount"], "warnings": inspection["warnings"],
            "zipChecks": zip_checks, "macOSLaunchVerified": False}


def build_source(args: argparse.Namespace) -> dict:
    if args.output_dir is None or args.source_list is None:
        raise BundleError("source packaging requires --output-dir and --source-list")
    if re.fullmatch(r"\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?", args.version) is None:
        raise BundleError("invalid version")
    root = args.source_root.resolve()
    relative_paths = json.loads(args.source_list.read_text(encoding="utf-8-sig"))
    if not isinstance(relative_paths, list) or not relative_paths:
        raise BundleError("source list must be a nonempty array")
    inventory = []
    seen = set()
    forbidden = {"data", "output", "release", "archive", "backups", "bin", "obj", ".tools", "artifacts"}
    for relative in relative_paths:
        if not isinstance(relative, str) or "\\" in relative or ":" in relative:
            raise BundleError("source entries must be literal portable relative paths")
        path = Path(relative)
        if path.is_absolute() or any(part in ("", ".", "..") for part in relative.split("/")):
            raise BundleError(f"invalid source entry: {relative}")
        if any(part.lower() in forbidden for part in path.parts) or path.suffix.lower() in {".exe", ".dll", ".log", ".bak", ".zip", ".tmp"}:
            raise BundleError(f"private/runtime entry rejected: {relative}")
        if relative.lower() in seen:
            raise BundleError(f"duplicate source entry: {relative}")
        seen.add(relative.lower())
        source = root / path
        if not source.is_file() or source.is_symlink() or not source.resolve().is_relative_to(root):
            raise BundleError(f"missing or unsafe source file: {relative}")
        inventory.append({"path": relative, "bytes": source.stat().st_size, "sha256": sha256_file(source)})
    output = args.output_dir.resolve()
    output.mkdir(parents=True, exist_ok=True)
    package_name = f"CornerTodo-macos-v{args.version}-source"
    package = output / package_name
    archive = output / (package_name + ".zip")
    manifest_path = output / (package_name + ".manifest.json")
    checksum = output / (package_name + ".zip.sha256")
    if any(path.exists() for path in (package, archive, manifest_path, checksum)):
        raise BundleError("source output already exists; use a fresh output directory")
    for record in inventory:
        target = package / record["path"]
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(root / record["path"], target)
        if sha256_file(target) != record["sha256"]:
            raise BundleError(f"source copy changed: {record['path']}")
    zip_checks = make_zip(package, archive, set())
    zip_checks.pop("mainExecutableMode", None)
    # A second read proves that ZIP contents match the explicit clean source.
    with zipfile.ZipFile(archive) as zipped:
        actual = {entry.filename for entry in zipped.infolist() if not entry.is_dir()}
        expected = {package_name + "/" + entry["path"] for entry in inventory}
        if actual != expected:
            raise BundleError("source ZIP contains missing or unlisted files")
        for record in inventory:
            if hashlib.sha256(zipped.read(package_name + "/" + record["path"])).hexdigest() != record["sha256"]:
                raise BundleError(f"source ZIP bytes changed: {record['path']}")
    digest = sha256_file(archive)
    checksum.write_text(digest + "  " + archive.name + "\n", encoding="ascii")
    manifest = {"product": "CornerTodo", "platform": "macOS", "version": args.version,
                "status": "experimental-source", "explicitSourceCount": len(inventory), "files": inventory,
                "zip": {"file": archive.name, "sha256": digest, **zip_checks},
                "macOSLaunchVerified": False}
    manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    return {"archive": archive.name, "manifest": manifest_path.name, "checksum": checksum.name,
            "sourceDirectory": package_name, "sourceFiles": len(inventory), "sha256": digest}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--publish-dir", type=Path)
    parser.add_argument("--output-dir", type=Path)
    parser.add_argument("--arch", choices=("arm64", "x64"))
    parser.add_argument("--version", default="0.1.0-alpha.1")
    parser.add_argument("--icon-png", type=Path)
    parser.add_argument("--guide", type=Path)
    parser.add_argument("--verify-publish", "--verify-only", action="store_true")
    parser.add_argument("--source-root", type=Path)
    parser.add_argument("--source-list", type=Path)
    args = parser.parse_args()
    try:
        result = build_source(args) if args.source_root is not None else build(args)
    except (BundleError, OSError, ValueError, struct.error) as error:
        print(f"ERROR: {error}", file=sys.stderr)
        return 1
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
