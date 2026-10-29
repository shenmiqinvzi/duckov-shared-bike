"""Read-only: inspect a named landmark GameObject and its components in map scenes."""
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / ".python-tools"))
import UnityPy

GAME = Path(r"E:\Steam\steamapps\common\Escape from Duckov\Duckov_Data")
levels = [10, 11, 13, 14, 18, 19]
env = UnityPy.load(*(str(GAME / f"level{i}") for i in levels))

def component_names(go):
    names = []
    for entry in go.m_Component:
        pointer = entry.component if hasattr(entry, "component") else entry[1]
        try:
            obj = pointer.deref()
            names.append(obj.type.name)
        except Exception:
            names.append("?")
    return names

for obj in env.objects:
    if obj.type.name != "GameObject":
        continue
    try:
        go = obj.parse_as_object()
    except Exception:
        continue
    name = go.m_Name
    if not any(key in name for key in ("ManholeExit", "BunkerEntry", "StartPoint3")):
        continue
    parent_name = "-"
    for entry in go.m_Component:
        pointer = entry.component if hasattr(entry, "component") else entry[1]
        try:
            comp_obj = pointer.deref()
            if comp_obj.type.name not in ("Transform", "RectTransform"):
                continue
            tr = comp_obj.parse_as_object()
            if tr.m_Father.m_PathID:
                parent_name = tr.m_Father.deref_parse_as_object().m_GameObject.deref_parse_as_object().m_Name
            break
        except Exception:
            continue
    print(f"[{obj.assets_file.name}] {name} active={go.m_IsActive} layer={go.m_Layer} parent={parent_name}")
    print("  components:", component_names(go))
    for entry in go.m_Component:
        pointer = entry.component if hasattr(entry, "component") else entry[1]
        try:
            comp = pointer.deref().parse_as_object()
        except Exception:
            continue
        script = getattr(comp, "m_Script", None)
        if script is None or not script.m_PathID:
            continue
        cls = script.deref_parse_as_object()
        print("   script:", getattr(cls, "m_ClassName", "?"), "namespace=", getattr(cls, "m_Namespace", "?"))
