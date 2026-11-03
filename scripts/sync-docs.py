"""Synchronize the public contract and versioned branding into the sibling docs repo."""
from pathlib import Path
import hashlib,json,shutil,subprocess,sys
root=Path(__file__).resolve().parents[1];docs=root.parent/'docs'
subprocess.run([sys.executable,str(root/'scripts/export-api.py')],check=True)
tokens=json.loads((root/'branding/tokens.json').read_text(encoding='utf-8'))
css='/* Generated from branding/tokens.json. Run python scripts/sync-docs.py. */\n:root {\n'
for name,value in tokens['colors'].items():css+=f'  --{name}: {value};\n'
for name,value in tokens['radii'].items():css+=f'  --radius-{name}: {value};\n'
css+='  --bg: var(--canvas); --panel: var(--surface); --line: var(--border); --blue: var(--primary); --green: var(--positive); --red: var(--negative);\n  color-scheme: light;\n}\n'
(root/'branding/tokens.css').write_text(css,encoding='utf-8')
files={'BRANDING.md':'BRANDING.md','branding/tokens.json':'branding/tokens.json','branding/tokens.css':'src/css/tokens.css','web/public/meadow.svg':'static/img/meadow.svg'}
manifest={'version':tokens['version'],'files':{}}
for src,dest in files.items():
    target=docs/dest;target.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(root/src,target)
    manifest['files'][dest]=hashlib.sha256(target.read_bytes().replace(b'\r\n',b'\n')).hexdigest()
shutil.copyfile(root/'branding/tokens.css',root/'web/src/tokens.css')
(docs/'branding/manifest.json').write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8')
for file in (root/'api-contract').glob('*.json'):
    target=docs/'api'/file.name;target.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(file,target)
(docs/'static').mkdir(exist_ok=True);shutil.copyfile(root/'api-contract/openapi.json',docs/'static/openapi.json')
fonts=docs/'static/fonts';fonts.mkdir(exist_ok=True)
for src,dest in [('inter/files/inter-latin-400-normal.woff2','inter.woff2'),('inter/files/inter-latin-600-normal.woff2','inter-semibold.woff2'),('ibm-plex-mono/files/ibm-plex-mono-latin-400-normal.woff2','mono.woff2')]:shutil.copyfile(root/'web/node_modules/@fontsource'/src,fonts/dest)
print('Synchronized branding and API contract to',docs)
