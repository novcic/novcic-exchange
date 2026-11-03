"""Fail when public implementation or synchronized snapshots drift without review."""
from pathlib import Path
import json,hashlib,re
root=Path(__file__).resolve().parents[1]
manifest=json.loads((root/'api-contract/source-manifest.json').read_text(encoding='utf-8'))
for file,expected in manifest['files'].items():
    assert hashlib.sha256((root/file).read_bytes().replace(b'\r\n',b'\n')).hexdigest()==expected,f'API source changed: {file}. Review/export the contract and synchronize docs.'
api=(root/'src/Novcic.Api/Program.cs').read_text(encoding='utf-8')
routes={(m.upper(),re.sub(r'\{(\w+):guid\}',r'{\1}',p)) for m,p in re.findall(r'app.Map(Get|Post)\("(/api/[^"\n]+)"',api)}
for names,method in re.findall(r'foreach \(var route in new\[\] \{ ([^}]+) \}\)\s*app.Map(Get|Post)',api):
    for name in re.findall(r'"([^"]+)"',names):routes.add((method.upper(),'/api/'+name))
routes.update({('GET','/api/keys'),('POST','/api/keys'),('POST','/api/keys/{id}/revoke')})
routes.update({('GET','/api/profile'),('POST','/api/profile'),('POST','/api/profile/password'),('POST','/api/profile/email')})
catalog=json.loads((root/'api-contract/endpoints.json').read_text(encoding='utf-8'))
assert routes=={(e['method'],e['path']) for e in catalog},'Public route/catalog mismatch'
docs=root.parent/'docs'
if docs.exists():
    for file in ['openapi.json','endpoints.json','source-manifest.json']:
        assert (root/'api-contract'/file).read_bytes()==(docs/'api'/file).read_bytes(),f'Docs snapshot differs: {file}'
    for src,dest in [('BRANDING.md','BRANDING.md'),('branding/tokens.json','branding/tokens.json'),('branding/tokens.css','src/css/tokens.css')]:assert (root/src).read_bytes()==(docs/dest).read_bytes(),f'Branding differs: {dest}'
assert (root/'branding/tokens.css').read_bytes()==(root/'web/src/tokens.css').read_bytes(),'Web tokens drifted'
print(f'Checked {len(routes)} public routes, source hashes and shared branding.')
