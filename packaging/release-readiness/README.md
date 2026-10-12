# T18 네이티브 재배포 자료 — 공개 배포 차단

기준 main `163f88f` / PR #29 병합 `09eb08f`. 이 자료는 2026-10-12에 검토 ZIP을 직접 다시 내려받아 대조한 결과다. **공개 배포 승인이나 앱 라이선스 지정이 아니다.** 제품/DB/정책/SDK/패키지 버전 변경은 없다.

## 고정한 검토물

- [run 38142117345](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/38142117345), job `114477931879`, attempt 1 success.
- [artifact 11703665480](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/38142117345/artifacts/11703665480), `t18-windows-review-61a98bb5f51a0aa997ab37429e00cee3be18a4cf-attempt-1`, 2026-10-25 만료 예정.
- artifact ZIP SHA256 `e945111bb4615e6df597d4eab403a5e6727899a7497de0ee024d99119c98f0e2`.
- 내부 `RandomMultimediaManager-win-x64-61a98bb5f51a.zip`: 231,919,246 bytes, SHA256 `53f8128461bc3153b84708340b20888d4794672c520ba97e781bde46ee3cb045`.
- commit `61a98bb5f51a0aa997ab37429e00cee3be18a4cf`, SDK 10.0.401 / runtime 10.0.12 / selfContained true / publicReleaseApproved false. 1,719개 ZIP 파일 중 자기 자신인 files.json을 제외한 1,718개 크기·SHA256를 전부 재검증했다.

`review-files.json`, `review-dependencies.json`, `review-build-info.json`은 **원래 artifact에서 복사한 동결 증거**다. 이번 PR로 새 패키지를 빌드했다는 의미가 아니며 이후 publish의 manifest로 재사용하면 안 된다. 새 고지·소스는 기존 artifact에 소급 포함되지 않는다.

## 실제 파일 목록과 출처

`native-inventory.csv`는 1,285행이다. PE의 CLR directory가 없는 native 이미지와 libvlc 폴더의 모든 지원/SDK import 파일을 기록했다. 경로·아키텍처·크기·SHA256·공식 배포물 byte 대조·검토 상태를 가진다. .NET managed DLL 전체는 review-files.json에 있다.

| VLC 폴더 | plugin DLL | 공식 VLC ZIP 동일 파일 | ZIP에 없는 import .lib |
|---|---:|---:|---|
| win-x64 | 323 | 423 | 2개는 공식 win64.7z의 sdk/lib와 별도 byte 일치 |
| win-x86 | 323 | 423 | 2개는 대조 미완료 |
| win-arm64 | 310 | 410 | 2개는 대조 미완료 |

총 plugin DLL은 956개다. 이전 검증 문서의 **436 modules는 libVLC 런타임 로그의 module 수**이며 x64 plugin DLL 파일 개수 323과 같은 값으로 취급하지 않는다. 이전 실행 로그/성공 기록은 보존한다. 비실행 아키텍처도 실제 ZIP에 포함돼 있으므로 고지 검토에서 제외하지 않았다. 지원 대상은 여전히 x64다.

공식 `https://download.videolan.org/pub/videolan/vlc/3.0.23/{win64,win32,winarm64}/vlc-3.0.23-{arch}.zip`을 내려받아 각 `.sha256` 원문과 대조했다. 1,256개 파일 byte 일치는 DLL/plugins뿐 아니라 Lua/hrtfs/header 등 포함 파일에도 적용된다. 정확한 archive hash·파일별 대응과 미대조는 `official-vlc-comparison.json`에 있다. 전체 공식 VLC ZIP의 GPL/Qt GUI 등 **제외된 파일까지 앱이 포함한다는 뜻은 아니다**.

## GPL FFmpeg를 포함한 LGPL metadata 불일치

`GPL_FFMPEG_EVIDENCE.json`에 동결 바이너리의 경로·SHA256·ASCII 문자열을 원문 보존한다. 세 아키텍처의 아래 두 파일(총 6개)에서 GPL 증거가 나왔다.

- `plugins/codec/libavcodec_plugin.dll`
- `plugins/video_chroma/libswscale_plugin.dll`

x64 avcodec 바이너리에는 FFmpeg 4.4.5 / Lavc58.134.100, configure의 `--enable-gpl --enable-postproc`, `--enable-static --disable-shared`, `libavcodec license: GPL version 2 or later` 등이 있다. NuGet nuspec의 LGPL-2.1-or-later 선언 또는 `.GPL` 패키지 미참조로 이 결합된 코드의 조건을 덮어쓸 수 없다. [FFmpeg 공식 안내](https://ffmpeg.org/legal.html)(2026-10-12 접근 확인)는 optional GPL 부분 사용 시 FFmpeg 전체에 GPL이 적용된다고 설명한다. 따라서 이 번들을 **LGPL만으로 충족된 패키지라고 승인하지 않는다**. 앱 전체에 적용될 최종 라이선스 범위를 이 문자열 검사만으로 확정하지 않는다.

`upstream/package-nuget-win.sh`는 VideoLAN libvlc-nuget의 `042f49a49609b2da7aeea0c94e51f809cf2e1575`(2026-04-16, NuGet 3.0.23.1 게시일에 해당하는 revision) 원문이다. `upstream/gpl-exclusions.json`의 35개 파일/폴더 제외 규칙에 해당하는 DLL은 현재 inventory에 0개다. 하지만 그 목록에 avcodec/swscale는 없다. **제외 규칙 통과는 LGPL-only 증거가 아님**이 이번 실물 검사로 드러났다. NuGet archive 자체에 이 packaging commit이 기록돼 있지 않으므로 이 스크립트를 해당 nupkg의 확정 빌드 attestation으로 취급하지 않는다.

나머지 plugin은 per-file 조건이 미완료다. 문자열에 GPL 표식이 없다는 사실을 LGPL 판정으로 바꾸지 않았다. VLC module 자체의 파일별 copyright/라이선스, 링크된 contrib 라이브러리(FFmpeg/LAME/OpenJPEG/GnuTLS/live555/libass 등) 버전·옵션·라이선스·변경·고지의 대응표가 필요하다. Lua/hrtfs 등 비DLL 자료도 같은 검토에 포함한다.

## 확보한 소스·고지와 남은 대응 관계

`source-inputs.json`의 URL·bytes·SHA256를 확인한 **원문 archive 4개(총 43,450,258 bytes)**를 sources/에 보존했다. VLC archive는 3개 part 파일을 순서대로 이어 붙이면 원래 tar.xz가 된다. source-inputs.json의 parts 순서와 전체 SHA256로 검증한다. 이 목록 전체가 완전한 corresponding source라는 뜻은 아니다.

| 구성 | 확보/대조 | 잔여 |
|---|---|---|
| libVLC 3.0.23 | 공식 tar.xz SHA256 `e891cae6aa3ccda69bf94173d5105cbc55c7a7d9b1d21b9b21666e69eff3e7e0`가 공식 .sha256와 일치. tar의 src/revision.txt는 바이너리와 같은 `3.0.23-2-0-g79128878dd`; Git tag 3.0.23-2의 commit은 `79128878ddb2c280bbb6c89c76a46b31a80ade1c`. 전체 VLC 코드·contrib recipes/SHA512SUMS/patches·Windows build scripts 포함 | 실제 contrib source archives, 선택된 dependency graph, compiler/toolchain·config 로그·환경 및 아키텍처별 patches/build 대응 미완료. 타사 링크 소스 누락으로 full source 제공 완료 아님 |
| LibVLCSharp/WPF 3.10.1 | 두 nuspec의 repository commit `4896d0e06d19ac40b46802cf7dc167d432f3fd63`를 archive URL로 확보. LGPL 원문 별도 보존 | 공식 source archive를 재빌드/교체 시험한 것은 아님. 앱 라이선스/EULA에서 library 수정·debugging 목적 reverse engineering 제한 여부는 사용자 선택 후 검토 |
| FFmpeg 4.4.5 | 공식 source tar.xz. SHA512가 **VLC tar의 contrib/src/ffmpeg/SHA512SUMS와 일치**. VLC가 적용할 patch 파일은 VLC archive에 포함. 원문 LICENSE.md/COPYING 보존 | 실제 바이너리의 적용 patches/외부 링크 라이브러리와 configure 전체가 맞는 patched source 묶음 미완료. upstream tar 하나로 대응 소스 완료 선언 금지 |
| SQLite native | DLL 내부 SQLITE_SOURCE_ID `2026-06-26 20:14:12 d4c0e51e4aeb96955b99185ab9cde75c339e2c29c3f3f12428d364a10d782c62`가 공식 3530300 amalgamation의 sqlite3.c와 일치. copyright/public-domain 원문 확보 | SQLitePCLRaw 2.1.13 nuspec에는 source commit 없음. upstream에 v2.1.13 tag 없음(2026-10-12 원격 확인). wrapper/native package의 정확한 commit/compiler/옵션은 미확정; v2.1.12나 현재 main으로 대체 주장 안 함 |
| SkiaSharp Win32 4.151.2 | nuspec commit `a0a2142b86627ec1a9c90b9beff0068ce567ee77`와 Git v4.151.2 일치. submodule Skia `098aeb5e4aa2e7f74e120e78200bbe0412ed57c5`, depot_tools `8fecc592a290769242d5098666cee8d29b7f0523`. 정확한 Windows build.cake와 revision 자료 보존 | Skia submodule/DEPS 및 외부 소스 전체 묶음·release toolchain 미확보. MIT wrapper를 native 전체의 라이선스로 단정하지 않음. NuGet의 LICENSE/THIRD-PARTY-NOTICES 원문을 유지 |
| .NET / Windows Desktop 10.0.12 | exact NuGet metadata commit `95017c711e6afc1085133d440e42b4bd78155701` 및 두 runtime의 license 원문. NETCore third-party notices 보존 | upstream repo 링크를 full source 확보로 표시 안 함. Desktop package에 제공된 THIRD-PARTY-NOTICES 원문은 없으며 존재하는 자료만 보존. NETCore notice로 이를 전부 대체했다고 주장 안 함 |

`notices/`에는 exact ZIP NuGet metadata·Skia/runtime 원문, VLC source COPYING/COPYING.LIB/AUTHORS, SQLite 공식 copyright 원문을 보존한다. 새 `packaging/licenses/` 고지는 기존 Package-Windows.ps1의 licenses 폴더 복사로 **향후** 패키지에 들어간다. GPL/FFmpeg 고지는 제3자 코드에만 적용하며 앱 LICENSE가 아니다.

VLC x64 configure 원문은 `upstream/vlc-x64-configure.txt`, Skia build 원문/commit은 `upstream/skia-*`에 있다. source build 실행이나 byte 동일 재빌드 검증은 하지 않았다. 제공된 recipes는 실제 필요한 출발점이며 누락 toolchain/patch 대응을 대신하지 않는다.

## 재현·제공 절차

1. 위 artifact에서 내부 ZIP을 가져와 SHA256/bytes를 확인한다. 만료 후 artifact를 읽을 수 없으면 기존 payload 검증을 재수행했다고 기록하지 않는다. 공개 승인 전 동결 ZIP을 별도 보존하거나 새 exact SHA의 CI artifact로 검토 기준을 다시 지정한다.
2. sources/의 archive를 source-inputs.json의 hash·bytes로 대조한다. 외부에서 다시 받을 때도 version URL만 믿지 말고 hash 불일치를 실패로 취급한다. Git archive 재압축의 byte가 달라지면 원래 보존한 archive를 사용하고 임의로 hash를 갱신하지 않는다.
3. VLC archive의 part01/part02/part03을 순서대로 이어 붙여 source-inputs.json의 file 이름으로 저장하고 전체 hash를 확인한 뒤 source를 풀어 src/revision.txt, contrib/src/*/rules.mak·SHA512SUMS·patches, extras/package/win32/configure.sh 및 configure를 읽는다. dependency별 실제 binary-linked version/configuration을 확인한 뒤 필요한 원문 archive를 확보하고 hashes/patches/build instructions/원문 licenses를 대응표에 추가한다. configure에 기록된 GPL FFmpeg를 단순 LGPL로 재표기하지 않는다.
4. 아래 차단 항목이 닫히면 바이너리 ZIP과 **검증된 전체 source sidecar**를 같은 배포 위치에서 동시에 제공하는 방식으로 준비한다. sidecar에는 source-inputs, native-inventory, 해당 archive·contrib 소스·patches·빌드 지시·원문 고지를 넣고 source-sidecar.files.json과 SHA256를 생성한다. 이는 향후 승인 후 절차이며 이번 PR에서 완전한 sidecar를 만들거나 공개 업로드하지 않았다. CI artifact의 14일 만료 링크나 일반 upstream 링크를 source 제공 수단으로 사용하지 않는다.
5. 소스 제공·고지·DLL 교체 가능한 연결과 앱 권리 부여 문구를 검토하고 깨끗한 PC 점검까지 수행한 뒤 별도 공개 배포 판단을 받는다. 이번 작업에서는 앱/EULA/업데이트 서버/엔진이나 배포 정책을 바꾸지 않는다.

복사한 자료를 검증하는 예(저장소 루트, Python 3):

```python
import csv, hashlib, json
from pathlib import Path
p = Path('packaging/release-readiness')
for row in json.loads((p / 'source-inputs.json').read_text()):
    data = (b''.join((p / part).read_bytes() for part in row['parts'])
            if 'parts' in row else (p / row['file']).read_bytes())
    assert len(data) == row['bytes']
    assert hashlib.sha256(data).hexdigest() == row['sha256']
# review-files.json은 동결 ZIP의 manifest이고 아래 inventory는 그 부분집합이다.
manifest = {x['path']: x for x in json.loads((p / 'review-files.json').read_text())}
rows = list(csv.DictReader((p / 'native-inventory.csv').open()))
assert len(rows) == 1285
for row in rows:
    expected = manifest[row['path']]
    assert int(row['bytes']) == expected['bytes']
    assert row['sha256'] == expected['sha256']
```

## 공개 배포 차단과 선택지 인계

| 항목 | 상태 / 재개 조건 |
|---|---|
| 제3자 증거·부분 소스·고지 정리 | 이번 PR 완료. manifest/native 목록·원문·확보한 archive·하위 GPL 증거를 검증/보존 |
| GPL FFmpeg와 앱 라이선스 | **차단**. 사용자 앱 라이선스 미선택. 현 번들의 GPL 조건을 충족하는 배포 방식 검토 또는 검증된 LGPL native 번들로 별도 전환 검토가 필요. 엔진/라이브러리 교체나 직접 빌드는 승인된 후속 작업 없이 하지 않음. app license를 어느 쪽으로도 임의 지정하지 않음 |
| exact plugin/contrib 전체 대응 소스·원문 고지 | **차단**. 아키텍처별 링크 소스/패치/build provenance 및 per-file 조건을 채운 후 sidecar/고지 검토. source archive 확보와 source 의무 충족을 구별 |
| 깨끗한 사용자 Windows x64 | **미검증**. 사전 설치 없는 지원 OS VM/PC 접근 시 T18_DISTRIBUTION_VALIDATION.md 기존 점검표 실행. 새 계정만으로 machine-wide SDK/runtime 미설치를 입증하지 못함. 사용자 설치 제거 요청 없음 |
| comic.cbz 잠금 | 원인 미확정 보존. 통합 run 38142117377 attempt 1 실패 → 동일 SHA attempt 2 성공. 새 Windows 실행 없음, 재현/수정/해결 처리 없음 |
| NAS/RaiDrive·원격 휴지통·AVI·수동 미검증 | 기존 상태 유지. 원격 휴지통 Unknown/차단, AVI 보류·기존 사용자 승인 생략·실제 IO drain/정상 종료 상한 없음 |

현재 공개 배포 **차단 유지**. 자료 정리는 사용자 앱 라이선스 결정 전에 가능한 부분까지 끝냈다. 직접 병합·공개 Release/tag·T19 VSR은 수행하지 않는다.
