"""Read-only: find MonoBehaviours that reference the MultiSceneTeleporter script and print fields."""
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / ".python-tools"))
import UnityPy

GAME = Path(r"E:\Steam\steamapps\common\Escape from Duckov\Duckov_Data")
files = [GAME / "globalgamemanagers.assets", GAME / "resources.assets", GAME / "level14", GAME / "level19"]
env = UnityPy.load(*(str(p) for p in files))

# Find the MonoScript PathID for MultiSceneTeleporter in each file.
script_ids = {}
for obj in env.objects:
    if obj.type.name != "MonoScript":
        continue
    try:
        data = obj.parse_as_object()
    except Exception:
        continue
    if getattr(data, "m_ClassName", "") == "MultiSceneTeleporter":
        script_ids[obj.assets_file.name] = obj.path_id
        print("script", obj.assets_file.name, obj.path_id)

keys = ("m_Enabled", "disableOnFinish", "requireOnce", "interactTime", "coolTime", "teleportOnTriggerEnter",
        "requireItem", "finishWhenTimeOut", "interactableGroup", "overrideInteractName")
for obj in env.objects:
    if obj.type.name != "MonoBehaviour":
        continue
    try:
        d = obj.parse_as_dict()
        pointer = d.get("m_Script")
        if pointer is None:
            continue
        if pointer.m_PathID not in script_ids.values():
            continue
    except Exception:
        continue
    go_name = "?"
    try:
        go_name = d["m_GameObject"].deref_parse_as_object().m_Name
    except Exception:
        pass
    print(f"[{obj.assets_file.name}] {go_name}")
    for key in keys:
        if key in d:
            print(f"   {key}={d[key]}")
