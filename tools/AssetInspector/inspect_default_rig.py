import json
import sys
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/".python-tools"))
import UnityPy
import numpy as np
from read_map_metadata import matrix, transform_for

GAME=Path(r"E:\Steam\steamapps\common\Escape from Duckov\Duckov_Data")
env=UnityPy.load(str(GAME/"resources.assets"),str(GAME/"globalgamemanagers.assets"))
models=[]; controllers=[]
for obj in env.objects:
    if obj.type.name=="GameObject":
        go=obj.parse_as_object()
        if go.m_Name!="0_CharacterModel_Duck_Jeff": continue
        root=transform_for(go)
        bones=[]
        def walk(tr):
            name=tr.m_GameObject.deref_parse_as_object().m_Name
            if name in ("Hip","Leg.Upper.R","Leg.Lower.R","Foot.R.001","Foot.R.002","Arm.Upper.R","Arm.Fore.R","Hand.R","Duck_Armature","Root","DuckV2_2"):
                bones.append({"name":name,"world":matrix(tr)[:3,3].tolist(),"scale":[tr.m_LocalScale.x,tr.m_LocalScale.y,tr.m_LocalScale.z]})
            for child in tr.m_Children: walk(child.deref_parse_as_object())
        walk(root); models.append({"name":go.m_Name,"bones":bones})
    elif obj.type.name=="AnimatorController":
        d=obj.parse_as_dict()
        if "VehicleType" in str(d.get("m_TOS",{})):
            controllers.append(d)
report={"models":models,"controllers":controllers}
path=ROOT/"artifacts/inspection/full-release-default-rig.json"
path.write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding="utf-8")
print(json.dumps({"models":models,"controllers":[c.get('m_Name') for c in controllers]},ensure_ascii=False))
