"""Inspect built scene IDs, native landmarks and duck bind-pose metadata without editing game files."""
import json
import math
import struct
import sys
from pathlib import Path

ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/".python-tools"))
import UnityPy
import numpy as np

GAME=Path(r"E:\Steam\steamapps\common\Escape from Duckov\Duckov_Data")
env=UnityPy.load(str(GAME/"globalgamemanagers.assets"),str(GAME/"resources.assets"),
                 *(str(GAME/f"level{i}") for i in (10,11,13,14,18,19)))

def matrix(transform):
    p=transform.m_LocalPosition; q=transform.m_LocalRotation; s=transform.m_LocalScale
    x,y,z,w=q.x,q.y,q.z,q.w
    result=np.eye(4)
    result[:3,:3]=np.array([[1-2*y*y-2*z*z,2*x*y-2*z*w,2*x*z+2*y*w],
                           [2*x*y+2*z*w,1-2*x*x-2*z*z,2*y*z-2*x*w],
                           [2*x*z-2*y*w,2*y*z+2*x*w,1-2*x*x-2*y*y]])@np.diag([s.x,s.y,s.z])
    result[:3,3]=[p.x,p.y,p.z]
    if transform.m_Father.m_PathID:
        result=matrix(transform.m_Father.deref_parse_as_object())@result
    return result

def transform_for(go):
    for component in go.m_Component:
        pointer=component.component if hasattr(component,"component") else component[1]
        obj=pointer.deref()
        if obj.type.name=="Transform": return obj.parse_as_object()
    return None

def text(raw,offset):
    length=struct.unpack_from("<i",raw,offset)[0]; offset+=4
    if length<0 or length>10000: raise ValueError("Invalid serialized string length")
    value=raw[offset:offset+length].decode("utf-8"); offset+=length
    return value,(offset+3)&~3

report={"scenes":[],"landmarks":[],"models":[],"mono":[]}
for obj in env.objects:
    if obj.type.name=="MonoBehaviour":
        base=obj.parse_as_object(check_read=False)
        if not base.m_Script.m_PathID: continue
        script=base.m_Script.deref_parse_as_object()
        if script.m_ClassName=="SceneInfoCollection":
            raw=obj.get_raw_data(); _,offset=text(raw,28)
            count=struct.unpack_from("<i",raw,offset)[0]; offset+=4
            for index in range(count):
                sid,offset=text(raw,offset); offset+=12
                guid,offset=text(raw,offset); label,offset=text(raw,offset); description,offset=text(raw,offset)
                report["scenes"].append({"id":sid,"guid":guid,"display_key":label,"description":description})
            if offset!=len(raw): raise ValueError("Scene metadata parser did not consume the exact serialized object")
        elif script.m_ClassName in ("MultiSceneTeleporter","SceneLoader","MultiSceneCore","CharacterModel","CharacterMainControl","SubSceneEntry"):
            go=base.m_GameObject.deref_parse_as_object() if base.m_GameObject.m_PathID else None
            entry={"class":script.m_ClassName,"file":obj.assets_file.name,"id":obj.path_id,"name":go.m_Name if go else base.m_Name,"bytes":obj.byte_size}
            if obj.assets_file.name.startswith("level"):
                entry["raw_hex"]=obj.get_raw_data().hex()
            report["mono"].append(entry)
    elif obj.type.name=="GameObject":
        go=obj.parse_as_object(); name=go.m_Name
        if obj.assets_file.name.startswith("level") and any(word in name.lower() for word in ("exit","entry","spawnpoint","teleport","location","startpoint","extract")):
            tr=transform_for(go)
            if tr:
                report["landmarks"].append({"file":obj.assets_file.name,"name":name,"id":obj.path_id,"position":matrix(tr)[:3,3].tolist()})
        elif obj.assets_file.name=="resources.assets" and "CharacterModel_Duck" in name:
            tr=transform_for(go)
            if tr:
                report["models"].append({"name":name,"id":obj.path_id,"position":matrix(tr)[:3,3].tolist(),"scale":[tr.m_LocalScale.x,tr.m_LocalScale.y,tr.m_LocalScale.z]})

path=ROOT/"artifacts/inspection/full-release-map-metadata.json"
path.write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding="utf-8")
print(json.dumps({"scenes":report["scenes"],"landmarks":len(report["landmarks"]),"models":report["models"]},ensure_ascii=False))
