import json,hashlib
from pathlib import Path
r=Path(__file__).resolve().parent
expected=json.loads((r/'isolated-mirror-02/source-world-manifest.json').read_text())
checks={}
for name,digest in expected.items():
 p=r/'isolated-world-01/runtime/Worlds'/name
 checks[name]=p.exists() and hashlib.sha256(p.read_bytes()).hexdigest()==digest
print(json.dumps({'original_world_unchanged':all(checks.values()),'files':checks},indent=2))
raise SystemExit(0 if all(checks.values()) else 1)
