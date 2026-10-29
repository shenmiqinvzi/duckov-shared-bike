"""Read-only: list interaction/teleporter components in the Ground Zero sub-scene."""
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / ".python-tools"))
import UnityPy

GAME = Path(r"E:\Steam\steamapps\common\Escape from Duckov\Duckov_Data")
env = UnityPy.load(str(GAME / "level14"))

def go_of(obj):
    try:
        data = obj.parse_as_object()
        pointer = getattr(data, "m_GameObject", None)
        if pointer is None or not pointer.m_PathID:
            return None, data
        return pointer.deref_parse_as_object(), data
    except Exception:
        return None, None

for obj in env.objects:
    if obj.type.name != "MonoBehaviour":
        continue
    try:
        go, data = go_of(obj)
        if go is None:
            continue
        script = data.m_Script.deref_parse_as_object()
        cls = getattr(script, "m_ClassName", "")
    except Exception:
        continue
    if cls not in ("SimpleTeleporter", "MultiSceneTeleporter", "SimpleTeleporterSpawner", "Door", "CountDownArea"):
        continue
    print(f"[{cls}] go={go.m_Name} active={go.m_IsActive}")
    try:
        d = obj.parse_as_dict()
        for key in ("target", "selfTeleportPoint", "transitionType", "transitionTime", "requireItem", "requireOnce",
                    "interactTime", "disableOnFinish", "coolTime", "interactableGroup", "delay", "blackScreen",
                    "targetScene", "scene", "pairCount"):
            if key in d:
                value = d[key]
                print(f"   {key}={value}")
    except Exception as exc:
        print("   fields unavailable:", exc)
