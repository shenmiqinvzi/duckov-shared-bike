"""Read-only: layer usage of renderers and camera culling masks in map scenes."""
import collections
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / ".python-tools"))
import UnityPy

GAME = Path(r"E:\Steam\steamapps\common\Escape from Duckov\Duckov_Data")
level_files = [GAME / f"level{i}" for i in (10, 11, 13, 14, 18, 19)]
env = UnityPy.load(*(str(p) for p in level_files))

report = {"renderer_layers": {}, "cameras": {}}
for obj in env.objects:
    file = obj.assets_file.name
    if obj.type.name in ("MeshRenderer", "SkinnedMeshRenderer"):
        try:
            r = obj.parse_as_object()
            go = r.m_GameObject.deref_parse_as_object() if r.m_GameObject.m_PathID else None
            layer = go.m_Layer if go else -1
        except Exception:
            layer = -1
        report["renderer_layers"].setdefault(file, collections.Counter())[layer] += 1
    elif obj.type.name == "Camera":
        cam = obj.parse_as_object()
        go = cam.m_GameObject.deref_parse_as_object() if cam.m_GameObject.m_PathID else None
        report["cameras"].setdefault(file, []).append(
            {"name": go.m_Name if go else "?", "cullingMask": cam.m_CullingMask.m_Bits, "enabled": cam.m_Enabled})

out = ROOT / "artifacts/inspection/layers-and-cameras.json"
out.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
for file in sorted(report["renderer_layers"]):
    print(file, dict(report["renderer_layers"][file]))
for file in sorted(report["cameras"]):
    print(file, "CAMERAS", report["cameras"][file])
print("output", out)
