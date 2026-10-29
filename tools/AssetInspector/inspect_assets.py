"""Read-only Unity hierarchy and scene metadata inspection. Never saves Unity objects."""
import argparse
import collections
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / ".python-tools"))
import UnityPy


def inspect(paths, output, pattern):
    env = UnityPy.load(*(str(path) for path in paths))
    report = {"unitypy": UnityPy.__version__, "files": {}, "named": [], "skeletons": [], "build_settings": []}
    for name, file in env.files.items():
        report["files"][name] = {"unity": getattr(file, "unity_version", None)}
    counts = collections.Counter(obj.type.name for obj in env.objects)
    report["types"] = dict(counts)
    for obj in env.objects:
        if obj.type.name == "BuildSettings":
            report["build_settings"].append(obj.parse_as_dict())
        elif obj.type.name in ("GameObject", "MonoScript", "AnimatorController", "Avatar", "AnimationClip"):
            try:
                data = obj.parse_as_object()
                name = getattr(data, "m_Name", "")
                if re.search(pattern, name, re.I):
                    entry = {"type": obj.type.name, "file": obj.assets_file.name, "id": obj.path_id, "name": name}
                    if obj.type.name == "MonoScript":
                        entry.update(class_name=data.m_ClassName, namespace=data.m_Namespace, assembly=data.m_AssemblyName)
                    report["named"].append(entry)
            except Exception as exc:
                report.setdefault("errors", []).append({"type": obj.type.name, "error": str(exc)})
        elif obj.type.name == "SkinnedMeshRenderer":
            try:
                data = obj.parse_as_object()
                name = data.m_GameObject.deref_parse_as_object().m_Name
                bones = []
                for pointer in data.m_Bones:
                    if not pointer.m_PathID:
                        continue
                    bone = pointer.deref_parse_as_object()
                    go = bone.m_GameObject.deref_parse_as_object()
                    parent = bone.m_Father.deref_parse_as_object() if bone.m_Father.m_PathID else None
                    bones.append({"name": go.m_Name, "id": pointer.m_PathID,
                                  "parent": parent.m_GameObject.deref_parse_as_object().m_Name if parent else None,
                                  "position": {axis: getattr(bone.m_LocalPosition, axis) for axis in "xyz"},
                                  "rotation": {axis: getattr(bone.m_LocalRotation, axis) for axis in "xyzw"}})
                if any(re.search(pattern, bone["name"], re.I) for bone in bones) or re.search(pattern, name, re.I):
                    report["skeletons"].append({"name": name, "file": obj.assets_file.name, "id": obj.path_id, "bones": bones})
            except Exception as exc:
                report.setdefault("errors", []).append({"type": "SkinnedMeshRenderer", "error": str(exc)})
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({"types": counts, "named": len(report["named"]), "skeletons": len(report["skeletons"]), "output": str(output)}, ensure_ascii=False))


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("paths", nargs="+", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--pattern", default="duck|motor|character|scene|foot|pelvis|hip|arm|leg|idle|ride")
    args = parser.parse_args()
    inspect(args.paths, args.output, args.pattern)
