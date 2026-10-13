"""Build/verify a deterministic, explicitly incomplete T18 review source sidecar."""
import argparse
import csv
import hashlib
import io
import json
import lzma
from pathlib import Path
import re
import subprocess
import tarfile
import zipfile

ROOT = Path(__file__).resolve().parent.parent
READY = ROOT / 'packaging/release-readiness'


def digest(data):
    return hashlib.sha256(data).hexdigest()


def encoded(value):
    return (json.dumps(value, ensure_ascii=False, indent=2, sort_keys=True) + '\n').encode()


def verify(path):
    with zipfile.ZipFile(path) as z:
        manifest = json.loads(z.read('manifest.json'))
        assert set(z.namelist()) == {x['path'] for x in manifest} | {'manifest.json'}
        assert len(z.namelist()) == len(manifest) + 1
        for item in manifest:
            data = z.read(item['path'])
            assert len(data) == item['bytes'] and digest(data) == item['sha256'], item['path']
        status = json.loads(z.read('status.json'))
        assert status['completeCorrespondingSource'] is False
        assert status['publicReleaseApproved'] is False
        with tarfile.open(fileobj=io.BytesIO(z.read('application.tar'))) as app:
            assert app.extractfile('LICENSE').read() == z.read('LICENSE')
        print(f"PASS source sidecar manifest {len(manifest)} entries; incomplete/source release blocked")


def build(output):
    subprocess.run(['git', 'diff', '--quiet', 'HEAD', '--'], cwd=ROOT, check=True)
    sha = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip()
    files = {'LICENSE': (ROOT / 'LICENSE').read_bytes()}
    inputs = json.loads((READY / 'source-inputs.json').read_text())
    for item in inputs:
        paths = item.get('parts', [item['file']])
        data = b''.join((READY / p).read_bytes() for p in paths)
        assert len(data) == item['bytes'] and digest(data) == item['sha256'], item['file']
        if 'sha512' in item:
            assert hashlib.sha512(data).hexdigest() == item['sha512'], item['file']
        files[item['file']] = data
    # Use committed Git objects rather than checkout line-ending conversions.
    tracked = subprocess.check_output(['git', 'ls-files', '-z'], cwd=ROOT).decode().split('\0')
    paths = [p for p in tracked if p and not p.startswith('packaging/release-readiness/sources/')]
    requests = ''.join(f'{sha}:{p}\n' for p in sorted(paths)).encode()
    batch = subprocess.check_output(['git', 'cat-file', '--batch'], input=requests, cwd=ROOT)
    snapshot = {}
    cursor = 0
    for p in sorted(paths):
        end = batch.index(b'\n', cursor)
        header = batch[cursor:end].split()
        assert header[1] == b'blob', p
        length = int(header[2])
        cursor = end + 1
        snapshot[p] = batch[cursor:cursor + length]
        cursor += length + 1
    archive = io.BytesIO()
    with tarfile.open(fileobj=archive, mode='w', format=tarfile.PAX_FORMAT) as app:
        for p, data in snapshot.items():
            info = tarfile.TarInfo(p)
            info.size = len(data)
            info.mode = 0o644
            info.mtime = 1577836800
            app.addfile(info, io.BytesIO(data))
    files['application.tar'] = archive.getvalue()
    for p in tracked:
        if p.startswith('packaging/') and not p.startswith('packaging/release-readiness/sources/'):
            files['evidence/' + p] = snapshot[p]
    # Preserve literal recipe lines/patches and module source headers. These are
    # candidate mappings, not evaluated make variables or build attestations.
    with tarfile.open(fileobj=io.BytesIO(lzma.decompress(files['sources/vlc-3.0.23.tar.xz'])), mode='r:') as tar:
        members = {m.name: m for m in tar.getmembers() if m.isfile()}
        prefix = 'vlc-3.0.23/'
        rules = {}
        modules = {}
        for name in sorted(members):
            if name.startswith(prefix + 'contrib/src/'):
                data = tar.extractfile(name).read()
                files['vlc-build-inputs/' + name[len(prefix):]] = data
                if name.endswith('/rules.mak'):
                    rules[name] = {'recipeSha256': digest(data), 'literalInputs': [line for line in data.decode(errors='replace').splitlines() if re.search(r'(URL|VERSION|HASH|DEPS_|\$\(APPLY\))', line)], 'selection': 'unresolved; recipe presence does not prove linkage', 'archives': 'see source-inputs and contrib-acquisition; recipe version is not binary attestation'}
            elif name.startswith(prefix + 'modules/') and name.endswith('Makefile.am'):
                data = tar.extractfile(name).read()
                text = data.decode(errors='replace').replace('\\\n', ' ')
                for match in re.finditer(r'^(lib\w+_plugin)_la_SOURCES\s*\+?=\s*(.*)$', text, re.M):
                    entry = modules.setdefault(match[1], [])
                    entry.append({'makefile': name, 'assignment': match[0], 'linkAssignments': re.findall(r'^' + re.escape(match[1]) + r'_la_(?:LIBADD|LDFLAGS|CPPFLAGS)\s*\+?=.*$', text, re.M), 'sources': []})
                    for source in match[2].split():
                        full = prefix + 'modules/' + source
                        if full in members:
                            raw = tar.extractfile(full).read()
                            entry[-1]['sources'].append({'path': full, 'sha256': digest(raw), 'header': raw.decode(errors='replace').split('*/', 1)[0][:6000]})
        inventory = list(csv.DictReader((READY / 'native-inventory.csv').read_text().splitlines()))
        mapping = []
        for item in inventory:
            name = Path(item['path']).name.removesuffix('.dll')
            mapping.append({'binary': item['path'], 'sha256': item['sha256'], 'architecture': item['architecture'], 'sourceCandidates': modules.get(name, []), 'review': 'literal module mapping only; linked contrib/license/provenance unresolved' if name in modules else 'see component coverage; no module target mapping'})
        files['module-source-candidates.json'] = encoded(mapping)
        files['contrib-recipe-index.json'] = encoded(rules)
    files['status.json'] = encoded({'commit': sha, 'completeCorrespondingSource': False, 'publicReleaseApproved': False, 'nativeEvidenceCommit': '61a98bb5f51a0aa997ab37429e00cee3be18a4cf', 'applicationLicense': 'MIT; third-party excluded', 'missing': ['exact per-architecture selected contrib graph/archives and applied patches', 'native compiler/toolchain/environment and build attestation', 'per-file linked-code notices and version compatibility closure', 'SQLitePCLRaw exact provenance (Apache-2.0, not blanket GPL source duty)', 'clean PC and retained manual validation'], 'nativeRows': len(mapping), 'moduleCandidateRows': sum(bool(x['sourceCandidates']) for x in mapping), 'contribRecipes': len(rules)})
    files['manifest.json'] = encoded([{'path': p, 'bytes': len(d), 'sha256': digest(d)} for p, d in sorted(files.items())])
    output.mkdir(parents=True, exist_ok=False)
    path = output / f'RandomMultimediaManager-source-review-{sha[:12]}.zip'
    # Stored entries avoid compressor version differences across Python platforms.
    with zipfile.ZipFile(path, 'w', compression=zipfile.ZIP_STORED) as z:
        for p, data in sorted(files.items()):
            info = zipfile.ZipInfo(p, (2020, 1, 1, 0, 0, 0))
            info.create_system = 3
            info.external_attr = 0o100644 << 16
            z.writestr(info, data)
    hash_value = digest(path.read_bytes())
    path.with_suffix('.zip.sha256').write_text(f'{hash_value}  {path.name}\n', encoding='utf-8')
    verify(path)
    print(f'ZIP {path} SHA256 {hash_value}')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, default=ROOT / 'artifacts/t18-source')
    parser.add_argument('--verify', type=Path)
    args = parser.parse_args()
    verify(args.verify) if args.verify else build(args.output.resolve())
