"""Focused T18-D1 source integrity regression; synthetic temporary fixtures only."""
import hashlib
import io
import json
import lzma
import os
from pathlib import Path
import subprocess
import sys
import tarfile
import tempfile
import warnings
import zipfile

SCRIPT = Path(__file__).with_name('Package-SourceSidecar.py')
WORKER = r'''
import importlib.util
from pathlib import Path
import sys
spec = importlib.util.spec_from_file_location('sidecar', sys.argv[1])
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
module.ROOT = Path(sys.argv[2])
module.READY = module.ROOT / 'packaging/release-readiness'
# Isolate only Git queries. The real build/input/ZIP checks run unchanged.
module.subprocess.run = lambda *args, **kwargs: None
def git_output(args, **kwargs):
    if args[1] == 'rev-parse':
        return '1' * 40
    if args[1] == 'ls-files':
        return b'LICENSE\0'
    if args[1] == 'cat-file':
        kind = b'tree' if sys.argv[3] == 'git-not-blob' else b'blob'
        return b'1' * 40 + b' ' + kind + b' 4\nMIT\n\n'
    raise RuntimeError('Unexpected fixture Git query')
module.subprocess.check_output = git_output
module.build(module.ROOT / 'output')
'''


def archive_license(data):
    result = io.BytesIO()
    with tarfile.open(fileobj=result, mode='w') as tar:
        member = tarfile.TarInfo('LICENSE')
        member.size = len(data)
        tar.addfile(member, io.BytesIO(data))
    return result.getvalue()


def zip_fixture(path, case):
    files = {'LICENSE': b'MIT\n', 'payload': b'original',
             'application.tar': archive_license(b'MIT\n'),
             'status.json': json.dumps({'completeCorrespondingSource': False,
                                       'publicReleaseApproved': False}).encode()}
    if case in ('complete-true', 'approved-true'):
        status = json.loads(files['status.json'])
        status['completeCorrespondingSource' if case == 'complete-true' else 'publicReleaseApproved'] = True
        files['status.json'] = json.dumps(status).encode()
    if case == 'application-license':
        files['application.tar'] = archive_license(b'other license')
    manifest = [{'path': name, 'bytes': len(data), 'sha256': hashlib.sha256(data).hexdigest()}
                for name, data in files.items()]
    if case == 'payload':
        files['payload'] = b'tampered'
    if case == 'hash':
        next(item for item in manifest if item['path'] == 'payload')['sha256'] = '0' * 64
    if case == 'size':
        next(item for item in manifest if item['path'] == 'payload')['bytes'] += 1
    if case == 'missing':
        del files['payload']
    with warnings.catch_warnings():
        warnings.simplefilter('ignore', UserWarning)  # Intentional duplicate fixture.
        with zipfile.ZipFile(path, 'w') as zip_file:
            for name, data in files.items():
                zip_file.writestr(name, data)
            if case == 'duplicate':
                zip_file.writestr('payload', files['payload'])
            zip_file.writestr('manifest.json', json.dumps(manifest))


def build_fixture(root, case):
    ready = root / 'packaging/release-readiness'
    (ready / 'sources').mkdir(parents=True)
    (root / 'LICENSE').write_bytes(b'MIT\n')
    stream = io.BytesIO()
    with tarfile.open(fileobj=stream, mode='w'):
        pass
    data = lzma.compress(stream.getvalue())
    (ready / 'sources/vlc-3.0.23.tar.xz').write_bytes(data)
    item = {'file': 'sources/vlc-3.0.23.tar.xz', 'bytes': len(data),
            'sha256': hashlib.sha256(data).hexdigest(),
            'sha512': hashlib.sha512(data).hexdigest()}
    if case == 'input-size':
        item['bytes'] += 1
    elif case == 'input-sha256':
        item['sha256'] = '0' * 64
    elif case == 'input-sha512':
        item['sha512'] = '0' * 128
    (ready / 'source-inputs.json').write_text(json.dumps([item]), encoding='utf-8')
    (ready / 'native-inventory.csv').write_text('path,sha256,architecture\n', encoding='utf-8')


def run():
    modes = [('normal', [], None), ('-O', ['-O'], None), ('PYTHONOPTIMIZE=1', [], '1')]
    zip_cases = ['normal', 'payload', 'size', 'hash', 'missing', 'duplicate',
                 'complete-true', 'approved-true', 'application-license']
    build_cases = ['normal', 'input-size', 'input-sha256', 'input-sha512', 'git-not-blob']
    total = 0
    with tempfile.TemporaryDirectory(prefix='t18-source-integrity-') as directory:
        for index, (mode, flags, optimize) in enumerate(modes):
            env = os.environ.copy()
            env.pop('PYTHONOPTIMIZE', None)
            if optimize:
                env['PYTHONOPTIMIZE'] = optimize
            for operation, cases in [('verify', zip_cases), ('build', build_cases)]:
                for case in cases:
                    fixture = Path(directory) / f'{index}-{operation}-{case}'
                    if operation == 'verify':
                        zip_fixture(fixture, case)
                        command = [str(SCRIPT), '--verify', str(fixture)]
                    else:
                        build_fixture(fixture, case)
                        command = ['-c', WORKER, str(SCRIPT), str(fixture), case]
                    result = subprocess.run([sys.executable, *flags, *command],
                                            capture_output=True, text=True, env=env)
                    should_pass = case == 'normal'
                    if (result.returncode == 0) != should_pass:
                        raise RuntimeError(f'{mode}/{operation}/{case}: unexpected exit {result.returncode}\n{result.stdout}\n{result.stderr}')
                    if should_pass and 'PASS source sidecar' not in result.stdout:
                        raise RuntimeError(f'{mode}/{operation}/{case}: success result missing')
                    reasons = {
                        'payload': 'ZIP size/SHA256 mismatch', 'size': 'ZIP size/SHA256 mismatch',
                        'hash': 'ZIP size/SHA256 mismatch', 'missing': 'ZIP entry set',
                        'duplicate': 'ZIP entry count', 'complete-true': 'Expected incomplete',
                        'approved-true': 'Expected public release', 'application-license': 'Application LICENSE',
                        'input-size': 'Source input size/SHA256 mismatch',
                        'input-sha256': 'Source input size/SHA256 mismatch',
                        'input-sha512': 'Source input SHA512 mismatch', 'git-not-blob': 'Expected Git blob'}
                    if not should_pass and ('ValueError:' not in result.stderr or reasons[case] not in result.stderr):
                        raise RuntimeError(f'{mode}/{operation}/{case}: wrong failure reason\n{result.stderr}')
                    if not should_pass and 'PASS' in result.stdout:
                        raise RuntimeError(f'{mode}/{operation}/{case}: false PASS')
                    print(f'PASS {mode} / {operation} / {case}: exit {result.returncode}')
                    total += 1
    print(f'PASS {total} source integrity cases across all three Python modes')


if __name__ == '__main__':
    run()
