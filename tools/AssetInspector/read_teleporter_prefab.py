"""Read-only: find MultiSceneTeleporter components and print serialized interaction fields."""
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / ".python-tools"))
import UnityPy

GAME = Path(r"E:\Steam\steamapps\common\Escape from Duckov\Duckov_Data")
files = [GAME / "level14", GAME / "level19", GAME / "resources.assets", GAME / "sharedassets0.assets"]
env = UnityPy.load(*(str(p) for p in files))

keys = ("m_Enabled", "disableOnFinish", "requireOnce", "interactTime", "coolTime", "interactableGroup",
        "teleportOnTriggerEnter", "requireItem", "overrideInteractName", "finishWhenTimeOut")
for obj in env.objects:
    if obj.type.name != "MonoBehaviour":
        continue
    try:
        d = obj.parse_as_dict()
        script = d.get("m_Script")
        if script is None:
            continue
        cls = script.deref_parse_as_object()
        name = getattr(cls, "m_ClassName", "")
    except Exception:
        continue
    if name != "MultiSceneTeleporter":
        continue
    go_name = "?"
    try:
        go = d["m_GameObject"].deref_parse_as_object()
        go_name = go.m_Name
    except Exception:
        pass
    print(f"[{obj.assets_file.name}] {go_name}")
    for key in keys:
        if key in d:
            print(f"   {key}={d[key]}")
