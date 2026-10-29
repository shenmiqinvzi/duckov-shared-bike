import json
import os
import re

path = os.path.expandvars(r"%USERPROFILE%\AppData\LocalLow\TeamSoda\Duckov\Saves\Save_1.sav")
text = open(path, encoding="utf-8", errors="replace").read()
pattern = re.compile(
    r'"(DuckovSharedBike\.(?:State|World|Payments)\.v1)"\s*:\s*\{[^}]*?"value"\s*:\s*("(?:[^"\\]|\\.)*")',
    re.S,
)
for match in pattern.finditer(text):
    key = match.group(1)
    raw = json.loads(match.group(2))
    print("===", key, "===")
    print(raw[:2500])
    print()
