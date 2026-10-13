# T18 네이티브 재배포 자료 — 공개 배포 차단

## 최신 사용자 결정 — 2026-10-12

MIT 우선, 불가능하면 적절한 오픈소스 라이선스 선택·적용이 위임됐다. 정확한 범위는 [DECISIONS.md](../../docs/DECISIONS.md)의 “T18 라이선스 선택 위임”을 따른다. T18-D1에서 자체 소스의 MIT를 적용했으며 최신 실행 계약은 [TASKS.md](../../TASKS.md)의 T18-D1 지시문이다. 아래 비교안의 사용자 재선택/문구 별도 확정 조건보다 이번 위임을 우선한다. 제3자 권리·소스 제공·배포 적합성 검토와 공개 배포 차단은 유지한다.


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

## T18 배포 방식 선택안 — 사용자 결정 전 검토

검토 기준은 main `8b9ce03`(PR #30 통합 `951d7cd`), 브랜치는 `task/t18-distribution-options`다. 아래는 **추천과 조건부 후속 작업**이며 D08·앱 라이선스·제품을 변경하는 결정이 아니다. 기존 자료를 재사용했고 대량 소스 수집·native 빌드·번들 교체는 하지 않았다.

### 공식 원문과 후보의 확인 수준

2026-10-12 공식 URL을 다시 확인했다. 웹 조회에서 실패한 페이지는 Python HTTPS 직접 요청으로 재확인했다. HTTP 200은 원문 접근 확인이며 바이너리의 적합성이나 법적 승인과 다르다.

| 공식 근거 | 이번 확인 / 적용 범위 |
|---|---|
| [VideoLAN 법적 안내](https://www.videolan.org/legal.html) | 직접 HTTP 200, 30,470 bytes, 페이지 SHA256 `3dd16518a6778cd4108514d5944658870e7eec7c192c6f8796b685794b364bb9`. 소프트웨어별 조건 준수와 재배포 소스 제공의 일반 안내; libVLC/plugin별 조건은 아래 exact 코드/고지 근거로 구분하며 이 페이지로 판정을 대체하지 않음 |
| [FFmpeg 법적 안내](https://ffmpeg.org/legal.html) | 웹 본문 접근 성공. GPL 옵션 사용의 의미와 LGPL 배포 체크리스트 확인. 체크리스트의 권장 제공 방식을 라이선스의 유일한 이행 방식으로 확대하지 않음 |
| [LGPL 2.1 원문](https://www.gnu.org/licenses/old-licenses/lgpl-2.1.txt) | 직접 HTTP 200, 26,419 bytes, SHA256 `20e50fe7aae3e56378ebf0417d9de904f55a0e61e4df315333e632a4d3555d95`. §4/5/6의 library 배포·결합·교체 조건을 구분. 기존 SPDX 고지의 표현/줄바꿈과 새 GNU 파일의 byte 동일을 주장하지 않음 |
| [GPL 2 원문](https://www.gnu.org/licenses/old-licenses/gpl-2.0.txt) | 직접 HTTP 200, 17,984 bytes, SHA256 `edaef632cbb643e4e7a221717a6c441a4c1a7c918e6e4d56debc3d8739b233f6`. §2/3의 결합물·단순 집합·대응 소스 제공 조건. 앱 전체 적용은 결합 관계/다른 조건과 함께 별도 검토 |
| [Core.Initialize API](https://docs.videolan.me/libvlcsharp/api/LibVLCSharp.Shared.Core.html) | 웹 본문 접근 성공. Windows custom directory와 major version 검사 지원. 현재 사용한 **3.10.1 exact source archive**의 `src/LibVLCSharp/Shared/Core/Core.cs`, `Core.Desktop.cs`, `docs/versioning.md`도 직접 읽어 대조 |
| [공식 VLC 3.0.23 win64 목록](https://download.videolan.org/pub/videolan/vlc/3.0.23/win64/) 및 [ZIP checksum](https://download.videolan.org/pub/videolan/vlc/3.0.23/win64/vlc-3.0.23-win64.zip.sha256) | 목록 접근 성공, checksum 직접 HTTP 200. ZIP hash가 기존 `official-vlc-comparison.json`과 일치. 이번에 전체 VLC ZIP을 다시 내려받거나 Windows에서 실행한 것은 아님 |

| 경로 / 실제 후보 | 버전·출처·hash 및 판정 |
|---|---|
| A 현재 번들 유지 | VideoLAN.LibVLC.Windows **3.0.23.1**, native **3.0.23-2-0-g79128878dd**, LibVLCSharp/WPF **3.10.1**. 동결 artifact `11703665480`와 전체 hash는 위 고정 검토물/manifest가 기준. 고지 보완 artifact `11722139575`의 내부 ZIP SHA256는 `c9e488ab078dc354f5572edc8c52e36383af4dfae4a55545f4e58898c7089d04`; 정확한 SHA/CI는 `docs/T18_DISTRIBUTION_VALIDATION.md` 참조. GPL FFmpeg가 포함된 **확인된 후보**, LGPL-only 아님 |
| B 검증된 LGPL native 번들 | 현재 확보한 **적격 후보 없음**. exact version/공식 출처/archive hash/per-file hash/전체 링크 조건은 미확인. NuGet 검색에 3.0.24 표시도 있었으나 버전 페이지의 직접 웹 확인 실패·실제 archive/플러그인 검사 없음. 신규 버전의 존재/라이선스 적합성 모두 이번 비교의 확정 근거로 사용하지 않으며 업그레이드를 권하지 않음. GPL 표식 6개를 지우거나 플러그인 2개를 삭제하는 것도 검증된 LGPL 번들 제작이 아님 |
| C 외부 libVLC 설치/연결 | 구체적인 대조 후보는 공식 **VLC 3.0.23 win64 ZIP**, URL은 위 공식 목록. 79,893,405 bytes, SHA256 `992d19dbd0b8a7cde9167d2f7780b1ef6f92acc8a71acfa736101a21f35181e1`. 기존 byte 대조에서 현재 번들의 x64 파일 423개와 동일. installer EXE/MSI hash·사용자 실제 설치 경로/모듈 로드는 이번에 검증하지 않음. 전체 VLC 설치와 사용에 따른 앱의 법적 관계는 별도이며 이 ZIP의 동일 FFmpeg 플러그인은 LGPL-only 대체 증거가 아님 |

정확한 x64 주요 DLL hash: libvlc `8ae9f16a72441f43fb4ae8f72c843736726e067ea4a8def2646748631cc4e872`, libvlccore `d3475b834dd3eb77910f37f71b0341d358bcbdda5b9f04cc4a3a8e2be1bc8e35`, avcodec `ded0566aef7b4e9a527cdfeb12f92b9a3b2736b824b687071359244979ee02c7`.swscale 및 다른 아키텍처/지원 파일은 `GPL_FFMPEG_EVIDENCE.json`과 `native-inventory.csv`가 단일 기준이다. 후보 B에 이를 전용하지 않는다.

### 세 경로 비교

| 비교 항목 | A 현재 번들 | B 검증된 LGPL 번들 | C 외부 설치/연결 |
|---|---|---|---|
| 사용자 설치 부담 | 앱 ZIP 전체 압축 해제. 별도 VLC 설치 없음 | 적격 번들을 확보하면 A와 같은 흐름 | 지원하는 x64 native 일체를 사용자가 별도 설치/준비하고 경로 연결. 버전/업데이트 관리 부담 증가 |
| 오프라인 실행 | 기존 CI는 ZIP 내 native/runtime 사용을 확인. 클린 PC 미검증 | 필요한 payload 확보 후 가능 예상; 실제 미검증 | 외부 DLL/plugins와 앱 runtime가 모두 준비된 뒤 가능 예상. 최초 준비에는 별도 다운로드 또는 오프라인 매체 필요 |
| LibVLCSharp 3.10.1 호환 | exact 번들에서 기존 Windows 회귀 근거 있음 | libVLC 3.x가 첫 후보 조건. major 일치만으로 plugin·코덱·출력 호환 완료 아님 | 3.0.23 win64는 기존 파일 동일 근거만 있음. 외부 경로 초기화·실제 로드는 미검증; 임의 4.x/32비트 연결 제외 |
| 코덱·자막·HW | 현재 기능/기존 미검증 보존; AVI는 보류 | 빌드 옵션과 제거 모듈에 따라 코덱·HW·자막 기능 감소 가능. SRT/SMI 및 avformat 유지 확인 필요 | 같은 버전이라도 설치 구성·plugin 탐색·사용자 업데이트로 달라짐. 공식 VLC player에서 재생된다는 사실은 자체 WPF 회귀 통과 아님 |
| 고지·대응 소스 잔여 | GPL FFmpeg 포함 exact contrib·patches·빌드 지시·per-file 조건과 제공 수단 미완료. x86/ARM64도 ZIP에 들어 있으므로 현 상태에서는 검토 대상 | 새로운 번들의 library·linked contrib 소스/조건과 LGPL 교체 가능성·고지 검토 필요. 기존 소스가 새 바이너리와 맞는다고 가정 금지 | 앱에서 직접 배포하지 않는 native의 재배포 소스 부담은 줄일 수 있음. 앱에 남는 LibVLCSharp/WPF LGPL 및 다른 의존성 의무 유지. native를 앱 ZIP/자동 다운로드/미러로 함께 공급하면 해당 공급 행위를 별도 검토 |
| 앱 라이선스 선택 관계 | GPL 조건과 양립 가능한 권리 부여를 검토하는 경로. 앱에 임의 GPL/MIT 지정 금지; 라이선스 문구만 바꿔도 전체 소스 미확보는 해결되지 않음 | LGPL 조건을 충족하는 결합이면 앱 자체 라이선스 선택의 폭을 넓힐 수 있음. 선택·권리 확인은 여전히 필요 | 설치 위치 분리로 결합 관계가 사라지지 않음. 같은 프로세스에서 GPL 코드가 포함된 plugin을 사용하는 관계를 검토해야 하며 MIT/비공개 앱의 자동 허용을 선언하지 않음 |
| 유지보수 비용(상대 추정) | 초기 자료/권리 검토 높음, 제품 변경 낮음. 업데이트마다 binary/source/plugin 대응 재검토 | 초기 후보 확보·구성 검증 가장 큼; 직접 빌드 선택 시 toolchain/보안 업데이트 비용 추가 | 최초 연결 UX·검증 변경 중간 이상, 사용자별 경로/버전/설치 상태 지원 비용 지속 |
| 현재 공개 준비 | 차단 | 적격 후보 없음으로 차단 | 연결 미구현·조건 미검토로 차단 |

### 실제 코드와 패키징 경계

`PreparedVideo.CreateNative`는 경로 없이 `Core.Initialize()` 후 `new LibVLC(true, options)`를 호출한다. 앱에는 외부 설치 검색/경로 선택/저장 기능이 없다. exact wrapper의 기본 탐색은 앱/assembly 주변 `libvlc/<arch>`와 DLL 경로이며 **Program Files 또는 registry의 설치된 VLC 자동 검색이 아니다**. custom path는 libvlccore와 libvlc를 순서대로 로드하고 major를 검사한다. native handle/loaded 상태는 static이므로 재생 중 엔진 경로 변경을 승인된 기능으로 가정하지 않는다. 외부 방식은 최초 native 사용 전에 경로를 결정하고 이후 변경은 재시작 기준으로 설계 검토해야 한다.

| 경계 / 현재 파일 | A의 후속 영향 | B의 후속 영향 | C의 후속 영향 |
|---|---|---|---|
| `src/RandomMultimediaManager.App/RandomMultimediaManager.App.csproj`, App/Core `packages.lock.json` | 현재 버전/graph 유지 가능 | 적격 공급 방식에 따라 native reference/content 공급과 영향 locks 검토. wrapper 버전 변경은 별도 승인 | native PackageReference 제거와 transitive graph/locks 확인 필요. managed wrappers 유지; 테스트 프로젝트 직접 참조도 조사 |
| `src/RandomMultimediaManager.App/Video/PreparedVideo.cs` | 기존 준비/해제/옵션 유지 | 동일 layout/ABI 후보라면 변경 불필요할 수 있음; 후보 확인 전 확정 금지 | 최초 초기화에 승인된 absolute native directory 전달, 잘못된 구조/architecture/버전 결과를 기존 준비 실패 경계로 연결 |
| native 경로 UI/저장 | 새 기능 불필요 | 동일 bundled layout이면 새 기능 불필요 | 경로 선택·보관·설치 없음의 사용자 안내가 필요. `MainWindow.xaml(.cs)` 및 기존 `Data/LibraryDatabase.Reads.cs`, `LibraryDatabase.Catalog.cs` 및 Core AppSettings의 저장 계약 적용 가능성은 후속 설계에서 확인. DB schema/API 신규 필드 여부는 이번에 승인하지 않음 |
| `scripts/Package-Windows.ps1` | 현 필수 native 검사를 유지. 승인 후 검증된 source sidecar 제공 절차 추가 가능 | 후보 payload·manifest·고지 공급/검사 변경. 전 아키텍처 포함 여부도 승인 범위 안에서 결정 | bundled libvlc 필수 파일/plugins 검사를 외부 방식에 맞게 변경, 최종 ZIP에 native 미포함을 직접 확인. self-contained .NET/SQLite/Skia는 그대로 |
| `scripts/Verify-WindowsPackage.ps1`, `.github/workflows/t18-packaging.yml` | 기존 동일 SHA ZIP/로드 회귀 유지 | 새 exact 후보 hash·버전·동일 ZIP 및 실제 load 경로 검사 | 현재 libvlc/core가 ZIP root 아래여야 하는 assertion과 harness 의존성 공급을 변경해야 함. .NET/SQLite/Skia는 ZIP, VLC는 지정 외부 root를 각각 검증; assertion 단순 삭제 금지 |
| `tests/RandomMultimediaManager.Video.Tests`, `Subtitle.Tests`, `Viewing.Tests` 및 관련 csproj | 현 회귀/미검증 유지 | 후보별 동등 기능과 손실 확인 | 테스트에서도 앱의 외부 초기화와 같은 경계를 사용해야 함. harness publish가 native를 재혼입하지 않는지 확인 |
| `packaging/USER_GUIDE.md`, `THIRD_PARTY_NOTICES.md`, `licenses/`, 이 README 및 T18 검증 문서 | 승인된 권리/소스 제공 위치·exact manifest 대응 | 새 공급자의 원문·contrib/소스·지원 기능 대응 | 별도 설치 단계/지원 버전·경로/변경 시 재시작 안내, 앱에 실제 포함된 것과 외부 공급의 고지 구분 |

파일 목록은 영향 분석이지 수정 승인이 아니다. 특히 C는 단순히 NuGet reference 하나를 제거하면 끝나는 작업이 아니다. A/B/C 모두 랜덤·Pending·삭제·해제·숨김/정상 종료 계약을 유지한다.

### 의무·내부 기준·미확보 정보를 구분

| 구분 | 적용 |
|---|---|
| 라이선스 원문에 따른 의무 | LGPL library 바이너리 자체 배포의 대응 소스 조건(§4), 결합물의 고지·권리/교체 조건(§6), GPL 적용 부분의 고지·변경/전체 대응 소스 및 허용되는 제공 경로(§2/3)를 실제 배포 형태에 맞춰 충족해야 함. LGPL §6(b)의 적합한 공유 library 사용과, 직접 library를 배포할 때의 §4를 혼동하지 않음 |
| 보수적인 내부 검토 기준 | 현재 GPL 포함 native를 in-process 사용하는 결합에 대해 양립 가능한 앱 권리와 라이선스 범위를 확인하기 전 공개 보류. exact per-file/hash 대응표, 검증한 source sidecar를 바이너리와 같은 영속 배포 위치에서 제공하는 방식을 권장. 법이 모든 경우 sidecar/같은 서버/byte 동일 재빌드를 요구한다는 뜻은 아님 |
| 단순 미확보 빌드 정보 | compiler patch version·실제 configure 로그·아키텍처별 provenance 및 SQLitePCLRaw exact commit 미확보. 필요한 컴파일/설치 scripts·patches는 대응 소스 검토 대상이지만, 모든 compiler 바이너리의 수집이나 native byte 동일 재빌드를 일괄 법적 의무로 두지 않음 |
| 다른 의존성의 조건 | MIT wrapper/.NET의 고지, Apache-2.0 SQLitePCLRaw의 라이선스·적용되는 NOTICE/변경 고지, SQLite public-domain 상태는 각각 검토. 동일한 GPL 전체 소스 요건을 일괄 부과하지 않음. Skia native/linked third-party는 MIT wrapper 이름만으로 완료 판정하지 않음 |

동적 연결·별도 폴더·공개 GitHub 저장소라는 사실 하나만으로 조건 충족을 선언하지 않는다. A에서 GPL-compatible 경로를 제안하는 것은 **내부 검토 선택안**이며 현재 문자열 증거만으로 앱 전체의 법적 라이선스를 확정한 결론이 아니다. 예를 들어 GPL-2.0-only를 즉시 선택하는 것도 Apache-2.0 등 타 의존성과의 양립 검토 없이 진행하지 않는다. B/C에도 앱 권리 부여 문구/EULA와 library 수정·debugging 목적 reverse engineering 제한 검토가 남는다.

### 추천 한 개와 대안

**추천: A 현재 번들을 유지하고 GPL 조건과 양립 가능한 공개 소스 배포를 검토하는 경로.** 전제는 사용자가 자신의 앱 코드에 그 권리를 부여하는 방향을 수용하고, 기여 코드/전체 graph의 양립 및 정확한 source 제공을 후속 검토로 닫는 것이다. 추천 이유는 D08의 의존성 포함 우선, exact payload/hash·기존 Windows 회귀·공식 파일 대응 근거가 가장 충분하고 제품 엔진/코덱 변경을 피할 수 있다는 점이다. 소스 잔여 작업량은 크며 빠른 공개를 보장하지 않는다. 전제를 수용하지 않으면 A를 실행하지 않고 선택을 다시 받는다.

대안 **B**는 앱 라이선스 선택 폭과 설치 없는 UX를 우선할 때다. 먼저 적격 x64/libVLC 3.x 번들 하나의 binary/linked-code/source 조건을 확인하는 제한 검토만 승인하고, 찾지 못하면 미확인으로 중단한다. 자체 native 빌드는 별도 비용·범위 승인 전 시작하지 않는다. **C**는 사용자가 별도 설치를 수용하고 실제 연결 관계/대상 배포물 조건 검토가 성립할 때의 대안이다. 공식 3.0.23 외부 ZIP은 기술 대조 후보일 뿐 LGPL 문제의 해결책으로 추천하지 않는다.

사용자에게 필요한 최소 질문(검토 후 선택, 이번 작업에서 답을 가정하지 않음):

1. **앱 소스에 GPL과 양립하는 이용·수정·재배포 권리를 부여하는 방향을 수용하는가?** 수용하면 A의 조건 검토로, 수용하지 않거나 보류하면 다음 질문으로 간다. 정확한 앱 라이선스명은 전체 의존성과 권리 확인 후 별도로 확정한다.
2. **설치 없는 ZIP 유지를 위해 LGPL 후보 확보를 먼저 검토할 것인가(B), 별도 x64 native 설치/경로 연결을 수용할 것인가(C)?** C는 법적 검토가 닫히지 않으면 구현하지 않는다. 둘 다 보류할 수도 있으며 그때 공개 차단을 유지한다.

### 선택 후 작업 계약 — 아직 착수하지 않음

| 후속 작업 | 목적·선행 사용자 결정 | 범위 | 완료 조건 | 금지 영역 |
|---|---|---|---|---|
| T18-D1 현재 번들 권리·소스 제공 준비 | A 선택 및 GPL-compatible 권리 부여 방향 수용. 정확한 앱 LICENSE 문구는 사용자 별도 확정 | 현 exact graph의 양립·기여 권리/per-file 조건 검토, 필요한 contrib archives/patches/scripts 대응·source sidecar 및 고지 제공 계획. 이 경로를 선택한 뒤에만 잔여 소스 수집 재개 | 앱 라이선스 선택에 필요한 graph/권리 근거, 바이너리별 소스/고지 매핑과 제공 방식, 빠진 조건 목록. 공개 가능 여부를 별도 보고 | 엔진/버전 교체·기능/DB 변경·자동 license 지정·직접 병합·Release/tag |
| T18-D2 LGPL 후보 적격성 확인 | B 선택. 조사의 공급 경로/예산·직접 빌드 제외를 사용자 확인 | 기존 버전 조건을 우선하는 x64/libVLC 3.x 후보 한 개를 공급자 자료와 실제 파일로 검토. 후보 hash·linked libraries/license·대응 소스·코덱 구성 기록 | 적격 후보 확보 여부를 명확히 판정. 없음/자료 불충분이면 중단·재개 조건 보고, 후보 선택·제품 반영은 별도 사용자 승인 | 대량 수집·자체 build·제품/locks/native 교체·임의 라이선스·공개 배포 |
| T18-D3 선택한 LGPL 후보 통합 | D2 적격 후보, 그 exact hash/공급 방식 및 변경 범위 승인, 앱 라이선스 사용자 결정 | 위 B 영향 파일에 한정해 native 공급·packaging/고지·회귀 연결 | exact SHA ZIP 재현/manifest·native load·전체 회귀와 아래 미디어 비교, source/권리 조건 검토. 기능 손실은 수용 결정 전 공개 차단 | wrapper/SDK 임의 변경·DB/세션/삭제/종료 정책 변경·기능 손실 은폐·직접 공개 |
| T18-D4 외부 연결 설계·구현 | C 선택, 대상 exact native artifact 및 결합 조건 검토, 경로 UX/저장 방식 사용자 승인 | 최초 초기화의 external root, x64/major/구조 확인·기존 준비 실패 연결, native 미포함 packaging/CI·사용 안내. 저장/API 변경이 필요하면 공통 계약 검토 후 별도 승인 | 외부 exact root 실제 로드·ZIP native 미포함·미설치/잘못된 경로/32비트/4.x/누락 plugin 검사, 최초 설정/재시작·전체 회귀와 지원 버전 고정. source/고지 조건 보고 | 무제한 자동 검색/다운로드·외부 플레이어 실행으로 대체·DB 임의 확대·세션/정상 종료 변경·라이선스 자동 면제·공개 배포 |
| T18-D5 선택 경로의 클린 PC/공개 전 검토 | 선택 경로의 구현·고지/소스 조건 완료 및 exact 검토 ZIP. 실PC 접근 확보 | 기존 T18 점검표, A/B는 SDK/runtime/VLC 미설치 지원 OS, C는 처음 VLC 미설치 상태와 승인 설치 후 상태를 분리. 테스트 복사본만 사용 | OS/설치 상태·ZIP hash·실제 loaded DLL 경로·오프라인 실행·기본 흐름/실패 보존·권리/소스 제공 대조를 기록. 공개 승인 요청은 그 이후 별도 | 기존 앱 데이터 초기화·사용자 설치 제거 요구·새 계정을 클린 PC로 간주·검증 없는 공개/전체 완료 |

B/C의 필수 미디어 비교는 기존 지원 확장자 `.mp4/.mkv/.avi/.webm/.mov/.wmv/.m4v/.ts`의 테스트 복사본과 대표 H.264/HEVC/VP9/AV1·AAC/AC3 등을 대상으로 후보가 실제 지원하는 범위를 기록한다. AVI의 `--demux=avformat` 옵션이 있으므로 avcodec/swscale를 삭제하고 통과로 간주하지 않는다. 한글 UTF-8 SRT 및 UTF-8/CP949 SMI 자동/수동 연결·싱크·끄기, HW on/off와 실제 디코더 경로, WPF 출력/전체화면/음소거/지연 준비·취소/끝 위치/Dispose 후 파일 잠금·삭제/정상 종료 회귀를 비교한다. codec 네 가지 전부를 새 필수 제품 보장으로 승격하지 않고 미지원/기존 대비 손실을 사용자에게 보고한다. fixtures/CI 합성 검증과 실파일/실HW 청취·화면 확인을 구분한다.

### 이번 검토의 검증 및 남은 상태

변경은 이 README와 CURRENT_STATE/TASKS뿐이다. 기존 archive 4개와 inventory/manifest/GPL evidence hash를 로컬 대조했고 exact wrapper 초기화 소스와 제품/패키징 경계를 읽었다. Markdown 링크·diff/문서 보존을 검사한다. `packaging/**`가 T18 CI paths에 포함되므로 이 README만 바뀌어도 PR exact head에서 기존 Windows 패키징 CI가 실행된다. 그 결과는 PR checks로 확인하며 새로운 LGPL/외부 방식 구현 검증이나 라이선스 승인으로 해석하지 않는다.

깨끗한 Windows PC, NAS/RaiDrive·원격 휴지통 Unknown/차단·AVI 조사 보류·기존 수동 미검증/승인 생략·실제 IO drain/정상 종료 상한 없음은 유지한다. 통합 `38142117377`의 최초 comic.cbz cleanup 잠금 실패→동일 SHA 재실행 성공은 원인 미확정이며 이번 문서 검토가 해결한 것이 아니다. 기존 자료/패키지/실패 이력과 앱 라이선스 미확정·공개 배포 차단을 보존한다.

## T18-D1 라이선스 적용과 검토용 소스 묶음

기준 main 690ac0b, 브랜치 task/t18-d1-license-source. 최신 지시는 과거 비교안의 사용자 재선택 대기보다 우선한다. **자체 코드·작성 문서 MIT 적용, 전체 번들 공개 배포 차단**이다.

### 권리와 라이선스 판단

전체 Git 이력의 author는 HAEKWANG DAN (danhk0612) 한 명이다. 사용자 위임과 저장소 소유·개발 이력을 근거로 소유자의 자체 코드·문서에 MIT를 적용했다. author 기록은 모든 파일의 권리를 증명하지 않으므로 제3자 archives/notices/upstream·의존성·원문 인용과 타인 저작물을 재라이선스하지 않는다. src/tests C#/XAML에 copyright/SPDX/copied/adapted 표시는 검색되지 않았다. 외부 코드 부재를 완전히 보증하는 검사는 아니다. fixture README의 합성 패턴/톤 생성 근거를 보존하며 출처가 별도 입증되지 않은 fixture까지 권리를 재부여하지 않는다.

MIT의 수정·sublicense·재배포 허용은 GPL-compatible이며 자체 MIT 권리와 GPL 결합 조건을 구별할 수 있어 다른 앱 라이선스로 바꿀 이유는 확인되지 않았다. LGPL dynamic loading도 MIT가 제한하지 않는다. library 수정/debugging reverse engineering 금지 EULA는 없다. 앱→Core·SQLitePCLRaw/Sqlite·SkiaSharp·LibVLCSharp/WPF→외부 libvlc/libvlccore→plugins/정적 contrib의 graph를 유지한다.

FFmpeg 실제 DLL의 GPL version 2 or later와 optional GPL 코드는 기존 evidence와 공식 설명에 근거한다. Apache-2.0의 GPLv3 호환/GPLv2 비호환을 고려해야 하며 모든 plugin의 only/or-later 검토가 끝나지 않아 전체 결합물의 GPL 버전을 임의 확정하지 않는다. **앱 소스 MIT 적용은 성립하지만 전체 graph 양립·재배포 적합성은 미완료**다.

공식 참조(2026-10-13): https://www.ffmpeg.org/legal.html live 접근 확인. https://www.gnu.org/licenses/license-list.en.html 및 https://www.gnu.org/licenses/gpl-faq.en.html 은 검색으로 MIT/Apache·plugin 조건을 확인했으나 live open은 timeout이었다. 오늘 원문 live 접근 성공으로 기록하지 않는다. 저장된 GPL/LGPL COPYING와 exact binary evidence를 우선하며 소스 제공 미완료를 앱 라이선스 사용자 재승인 대기로 바꾸지 않는다.

### 추가 확보와 바이너리별 대응

source-inputs.json은 8개 archive를 포함한다. 기존 4개의 byte/hash는 그대로다. FFmpeg 6개 DLL configure의 libgsm/openjpeg/mp3lame/pthreads와 contrib FFmpeg recipe의 zlib/gsm 및 조건부 deps를 근거로 GSM/OpenJPEG/LAME/zlib 4개를 확보하여 VLC tar SHA512SUMS와 대조했다. 정확한 linked version이 recipe와 같다는 attestation은 없어 **후보 소스**다. pthreads VideoLAN 경로는 404였고 누락으로 기록했다(contrib-acquisition.json). recipe MinGW 10.0.0을 실제 compiler 버전으로 단정하지 않는다.

| 실제 파일 그룹 | 제공한 소스·고지/빌드 입력 | 남은 확인 |
|---|---|---|
| avcodec/swscale 6개 | path/hash/configure GPL evidence, FFmpeg 4.4.5, ffmpeg recipe/patches/SHA512, GSM/OpenJPEG/LAME/zlib 후보·원문 고지 | 실제 patch 분기·linked versions, winpthreads/MinGW/libunwind·compiler runtime source/예외·링크 명령 |
| VLC core·956 plugin DLL·지원 파일 | exact VLC tar, 1,285 native/support path/hash별 module target 후보/source headers/LIBADD, 전체 contrib recipe/patch | Make 변수/조건 해석·정적 링크 전이 graph·개별 licenses, 미매핑 파일 식별·build provenance |
| LibVLCSharp/WPF | exact nuspec commit source·LGPL·앱 build | modified compatible DLL 교체 검증, 결합 조건 최종 대조 |
| SQLite/SQLitePCLRaw | native source ID 동일 amalgamation·Apache 고지/locks | exact wrapper/native build provenance. Apache/public-domain을 GPL 소스 의무로 처리하지 않음 |
| Skia/.NET/Desktop | exact nuspec·원문 license/third-party notices·Skia revisions/build.cake | 개별 notice/linked license 양립 확인. MIT/BSD attribution 유지, 전체 소스 일괄 의무 없음 |

### 생성·검증·제공 절차

clean tracked checkout에서 `python scripts/Package-SourceSidecar.py --output artifacts/t18-source`를 실행한다. Git HEAD objects의 application.tar에 자체 source/tests/scripts/locks/global.json/workflows/문서와 evidence를 포함한다. source parts는 검증하여 VLC tar로 재조립한다. 8개 archive·contrib recipe/patch 원문·전체 native inventory의 module-source-candidates.json·contrib-recipe-index.json·evidence/고지·LICENSE를 묶는다. Makefile 후보/headers는 라이선스 판정·확정 graph가 아니다. tar/patch 자체를 수정·적용하지 않고 recipe와 원문을 제공한다.

고정 entry 순서/2020 timestamp/ZIP_STORED로 압축기 버전 차이를 피한다. manifest.json은 자신을 제외한 모든 entry의 size/SHA256, 별도 ZIP sha256는 전체 파일을 검사한다. status.json에는 exact app commit·동결 native commit·누락·completeCorrespondingSource:false/publicReleaseApproved:false가 있다. `python scripts/Package-SourceSidecar.py --verify <zip>`은 entry 집합/중복·size/hash·상태·application LICENSE를 검사한다. CI는 두 생성 ZIP byte 일치와 inputs hash 및 runtime ZIP의 LICENSE·고지·모든 upstream licenses byte를 대조한다.

공개 재개 조건은 세 아키텍처 공급자의 build provenance/linked versions·전체 필요한 archives/patch 분기·toolchain/build instructions·파일별 고지와 license graph 양립 확인이다. 공급자와 소스 확보 경로 확인이 필요하지만 이번에 메시지를 보내거나 native 자체 빌드를 수행하지 않았다. 완전한 sidecar를 exact binary와 같은 위치에서 동시에 제공한다. 일반 upstream 링크나 현재 sidecar로 의무 완료를 선언하거나 실현되지 않은 서면 소스 약정을 발행하지 않는다.

클린 PC/NAS/RaiDrive/AVI·기존 수동 미검증·comic.cbz 잠금 원인 미확정·종료 상한 없음은 유지한다. 제품/DB/API/SDK/엔진/번들 변경·직접 병합·공개 Release/tag·VSR 착수는 없다.

## T18-D1 잔여 대응 소스 확인 — source-closure

기준 main `a7c588166dd1be3a90b775a0c807c95e164b015f`, 브랜치 `task/t18-d1-source-closure`. PR #32의 MIT 적용·42개 최적화 무결성 보완은 유지한다. 이번 결과는 **잔여 자료 조사와 검토용 sidecar 갱신 완료, 전체 대응 소스·license graph·공개 배포 차단 유지**다. 아래가 현재 잔여의 기준이며 위 과거 비교안의 앱 라이선스 재선택 조건을 적용하지 않는다.

### 확보·입증 범위

- 기존 8개 archive를 재다운로드하지 않았다. `source-inputs.json`의 기존 bytes/SHA256/SHA512를 보존하고 MinGW-w64 10.0.0, libbluray 1.4.0 원본만 추가했다(총 10개). 두 원본 모두 기존 VLC tar의 `contrib/src/{pthreads,bluray}/SHA512SUMS`와 일치한다. archive 확보가 actual linked version/patch 완료를 뜻하지 않는다.
- pthreads recipe의 `download_pkg(...,winpthreads)`는 `pub/contrib/winpthreads/`를 먼저 사용한다. 이전 `pthreads/` 404는 유지하며 올바른 mirror의 v10.0.0도 404임을 확인했다. recipe의 공식 SourceForge fallback에서 **9,620,291 bytes / SHA256 `ba6b430aed72c63a3768531f6a3ffc2b0fde2c57a3b251450dcf489a894f0894` / SHA512 `3c0827af7c40809a867758f6cd9ef3ff0c988b43082345bf725e03949af95968d634ace99a7ffae323189549221dcb5d861de9e801f1fbc7904e446641b60516`**를 확보했다. v9/v11/v14 또는 pthreads-w32로 대체하지 않았다. winpthreads의 MIT·Lockless BSD 고지 및 MinGW 최상위 ZPL/예외 설명을 원문으로 포함한다. 최상위 ZPL만으로 모든 toolchain/runtime 파일을 판정하지 않는다.
- JAR 이름 `libbluray-{awt-,}j2se-1.4.0.jar`와 VLC recipe 버전이 일치한다. 공식 libbluray 1.4.0 원본 **458,580 bytes / SHA256 `77937baf07eadda4b2b311cf3af4c50269d2ea3165041f5843d96476c4c92777`** 및 COPYING을 추가했다. exact Java compiler/patch·JAR 생성·native 링크를 아직 입증하지 않았다.
- tar에 빠져 있던 공식 `extras/package/win32/build.sh`, `extras/ci/gitlab-ci.yml`, `extras/ci/get-contrib-sha.sh`를 exact revision `79128878ddb2c280bbb6c89c76a46b31a80ade1c`에서 확보했다. 보존 파일/blob/hash·공식 요청은 [vlc-release-evidence.json](upstream/vlc-release-evidence.json)에 있다. GitHub mirror의 Git blob을 대조했고 공식 repository/files API로 실제 source 본문도 확인했다. raw 웹 경로의 HTTP 200 anti-bot HTML은 source로 채택하지 않았다.
- release pipeline [674296](https://code.videolan.org/videolan/vlc/-/pipelines/674296)의 `3.0.23-2` 성공과 Windows release job 2403066/2403067/2403068 metadata를 확인했다. CI는 x86/x64 이미지 `20251208092116`, ARM64 LLVM/UCRT 이미지 `20251223015425`, `build.sh -r -l -i u ...`, contrib tarballs와 extras/tools archives 보존 경로를 명시한다. 이는 **정해진 build instructions**이며 exact DLL의 compiler 버전/실제 실행·링크 attestation은 아니다. release template은 일반/nightly의 prebuilt contrib logic을 우회한다. 2017년 `latest` prebuilt를 이 release의 소스로 사용하지 않는다.

### 바이너리별 필수 잔여와 단순 provenance

[source-closure-inventory.csv](source-closure-inventory.csv)는 동결 `native-inventory.csv` 1,285개 path/SHA256를 하나도 제외하지 않고 그룹·대응 source path·증거 수준·잔여 ID로 연결한다. [source-closure.json](source-closure.json)은 exact source hash와 323개 고유 plugin target의 literal Makefile/source/link 후보를 담는다. `_la_SOURCES`의 조건 분기는 실행하지 않으며 `libscaletempo_pitch`의 명시적 source alias만 원문 대조로 풀었다. 이 파일들은 license scanner의 승인 결과가 아니다.

| exact 파일 그룹 / 수 | 현재 대응 | 필수 잔여 / 분류 |
|---|---|---|
| avcodec/swscale / 6 | binary configure·GPL 증거, FFmpeg 4.4.5, recipe의 USE_FFMPEG patch 14개와 GSM/OpenJPEG/LAME/zlib/MinGW 후보 | S01/N01/G01: actual selected versions·patch 적용 및 필요한 컴파일/설치 scripts, 각 linked code 고지·양립. ARM64 `libunwind.a/libpthread.a/-static-libstdc++`의 실제 LLVM/GCC runtime 구분·예외 확인. `--cc=...gcc` 이름만으로 구현/버전 확정 금지 |
| 다른 plugin / 950, VLC core / 6 | revision-matched VLC 원본, 파일별 source 헤더·LIBADD 후보 | S01/N01/G01: build의 `make list`, `config.mak/config.log`, 링크 `.la/.pc` 또는 link map/log 및 선택된 contrib 원본/patch. GnuTLS→nettle/GMP, live555, libass→freetype/harfbuzz 등은 recipe 후보이며 실제 버전/전이 graph 미확정 |
| Lua bytecode / 108 | 36개 고유 `.lua` 원본 및 `share/Makefile.am` LUAC 명령 | S03/N02: 사용된 source/변경 및 bytecode 생성 지시의 대응 확인. 원본 소스는 제공됐지만 동일 bytecode 재빌드는 미실시. compiler patch 버전·byte 동일 재빌드는 일괄 법적 의무로 두지 않음 |
| HRTF·이미지·web 지원 / 180 | 84개 source SHA256 그대로 일치, 96개는 LF→CRLF 변환 SHA256가 동결 hash와 일치 | N02: 개별 copyright/license 원문 보존·전체 notice coverage 최종 대조. 소스가 없는 180개로 분류하지 않음. CRLF만 다르며 별도 semantic patch를 추정하지 않음 |
| libbluray JAR / 6 | 1.4.0 archive/recipe checksum/COPYING, Java source | S02/N01/G01: 배포된 두 JAR variant의 source·Java build와 dependencies/patch 대응. JAR를 단순 비실행 지원 파일로 제외하지 않음 |
| SDK import `.lib` / 6 | VLC src/lib build 후보, x64 2개 공식 7z byte 대조 이력 | S01/N01: archive members/import stub 및 생성 조건 확인, x86/ARM64 기존 미대조 유지. 모든 compiler binary 수집 요구와 구분 |
| SQLite / 1 | native SOURCE_ID 일치 amalgamation + SQLitePCLRaw Apache 원문 | N03: 적용 NOTICE/변경 고지 검토. exact wrapper commit/compiler 옵션은 advisory provenance이며 public-domain/Apache만으로 GPL 전체 소스 차단 부과 안 함 |
| Skia / 1 | exact package source revision/build.cake 및 제공 notices | N03/G01: 실제 linked third-party 조건과 고지 coverage·graph 양립 검토. MIT만으로 native 전체 승인 금지; 필요 소스가 생기는지 actual linked license에서 판단 |
| .NET/Desktop/apphost 등 / 21 | runtime/app source 및 exact package notice 수집 경로 | N03: NuGet/runtime의 기존 원문·적용 third-party 조건 대조. D3DCompiler/vcruntime/MSQuic/native apphost도 누락하지 않음. compiler 환경 미확보 자체를 GPL 전체 소스 의무로 확대 안 함 |

`N01/N02/N03`는 기존 COPYING/원문을 삭제하라는 뜻이 아니라 파일별 필요한 notice가 모두 포함됐는지 확인해야 한다는 뜻이다. 새 `licenses/contrib/`에 winpthreads/MinGW/libbluray·dkjson copyright, HRTF의 내장 License 문자열 및 GPL module source header 원문을 보완했다. 원본을 relicense하지 않는다.

### 전체 graph 판정

자체 MIT·MIT/BSD류와 GPL 결합 배포 조건을 분리한다. FFmpeg 6개는 기존 실제 binary의 `GPL version 2 or later`가 근거다. 추가로 source 후보 10개 target에 GPL 헤더가 있다: dolby/headphone channel mixer, x26410b, lua, remoteosd, deinterlace, hqdn3d, visual, i420_rgb_mmx, glspectrum. 전체 목록·source hash는 source-closure.json에 있다. **조건부 source/header까지 포함한 후보**이므로 이 목록만으로 모든 target의 실제 GPL 적용/only/or-later를 확정하지 않는다. avcodec/swscale 문자열 부재·NuGet exclude list 통과를 다른 target의 LGPL 증거로 쓰지 않는다.

LGPL source header와 GPL-or-later 후보는 GPLv3 경로를 배제하지 않지만 SQLitePCLRaw Apache-2.0와 GPLv2-only의 결합은 양립한다고 선언할 수 없다. GnuTLS/nettle/GMP/live555·Java/Skia의 실제 linked 조건·버전 및 필요한 예외를 대조하기 전 **전체 graph 양립 미입증**이다. LGPL library 배포의 소스/고지 및 교체·수정 debugging 조건도 별도로 남는다. 앱 권리는 MIT 그대로이며 제3자 권리를 MIT/GPLv3로 재부여하지 않는다. LGPL 교체 실제 시험 미실시·클린 PC 미검증도 유지한다.

### 외부 증거가 필요한 정확한 누락과 재개 조건

| ID | 필요한 증거·질문 | 확인 경로·실패 / 재개 조건 |
|---|---|---|
| S01 | revision 79128878 release-win32/win64/win64-arm의 contrib selected 목록, actual archives/checksums, applied patches와 `config.mak/config.log`, 필요한 build/install scripts·link map 또는 `.pc/.la`, statically copied runtime의 version/license exception | 공식 project 435 pipeline 674296/job 2403066/7/8 metadata는 HTTP 200. 세 `/jobs/<id>/trace`는 **HTTP 401**. job 2403067 `/artifacts`는 **HTTP 404**. metadata의 archive 만료일 2026-01-06 및 trace만 남음을 기록. 공급자의 대응 소스 묶음 또는 이 release 로그/artifact의 권한 있는 접근을 확보하면 재개. 인증 우회/실제 공급자 연락 없음 |
| S02 | `plugins/access/libbluray-{awt-,}j2se-1.4.0.jar` 6개 hash에 대응하는 Java/native source 변경·compiler/build instructions, JAR 내부 고지/제3자 class 목록 | official libbluray 1.4.0 source HTTP 200 + recipe SHA512 match. archive/filename 증거만으로 JAR correspondence 미확정. 공급자 Java build·JAR inventory를 확보하거나 승인된 exact artifact 검사 환경에서 확인하면 재개 |
| S03 | Lua 36개 source/bytecode 생성·원본 변경 여부 및 actual LUAC 조건 | 원본 36개와 build rule 제공. release trace 401로 생성 실행 미확인. 해당 로그/공급자 설명 또는 exact `.luac` 검사로 source 대응 확인 시 재개. source 제공과 byte 동일 재빌드 의무를 구별 |
| N01/N02 | DLL/core/JAR·지원 파일별 copyright/notice 포함, transitive source license/변경 조건 | COPYING/AUTHORS/새 notice는 제공됐지만 모든 linked source의 고지 미입증. S01/S02 selected graph와 existing source headers를 연결하고 최종 runtime ZIP 원문 포함을 검사하면 재개 |
| N03/G01 | Skia/runtime의 실제 linked 예외·per-file notice coverage와 전체 GPL version 양립, LGPL 교체 조건 | exact NuGet 원문 유지. GNU license-list/GPL/LGPL live 직접 요청은 HTTP 502, web open timeout; 완료 근거로 사용하지 않음. 저장한 원문 조건을 actual graph에 대조하거나 공식 설명 확보 시 재개. MIT 선택 재승인 또는 SQLitePCLRaw commit만을 이유로 차단하지 않음 |

동일 조사를 계속 반복하지 않는다. compiler version/환경의 미확보는 provenance advisory다. 대응 소스에 필요한 실제 patch·컴파일/설치 script의 누락과 구별한다. source sidecar의 `completeCorrespondingSource:false`, `publicReleaseApproved:false`는 유지하고 위 ID를 status/evidence에 연결한다. 기존 원문 archives와 literal recipe patches는 제공하되 실제 적용을 주장하지 않는다. 공급자 자료를 확보하면 **해당 ID만** 검증·고지/sidecar에 통합하고 exact SHA의 Windows 패키징을 확인한다. 새 native build·번들 교체·plugin 제거·T18-D2~D5/VSR은 별도 지시 전 착수하지 않는다.

공급자 문의 초안(발송하지 않음):

> For VideoLAN.LibVLC.Windows 3.0.23.1 and the official VLC 3.0.23-2 Windows binaries (commit 79128878), please provide the corresponding source/build materials for release jobs 2403066/2403067/2403068: selected contrib versions and checksums, actual patches, config/link inputs and necessary build/install scripts, plus linked compiler runtime licenses/exceptions. The public traces return 401 and the win64 artifact returns 404. Please also identify the source/build correspondence for libbluray 1.4.0 Java JARs and compiled Lua files. Our per-file SHA256 inventory and the six GPL FFmpeg findings are available for matching. We are not requesting a license change or a replacement bundle.

### 이번 변경의 검증 경계

sidecar는 기존 committed Git snapshot/evidence 복사로 새 source-closure CSV/JSON·공식 scripts·10개 source archives·추가 고지를 포함한다. builder의 무결성 로직은 그대로이며 status의 누락 분류와 evidence 경로만 갱신한다. 현재 파일별 대응 검사와 source hash/recipe checksum, 반복 sidecar manifest/byte 검증 및 exact SHA Windows T18 CI 결과는 [T18 검증 문서](../../docs/T18_DISTRIBUTION_VALIDATION.md)의 이번 결과를 따른다. 이전 성공/실패와 검토 ZIP·자료에 소급 포함됐다고 기록하지 않는다.

클린 Windows PC/NAS/RaiDrive·원격 휴지통 Unknown/차단·AVI 조사 보류·기존 수동 미검증/승인 생략·comic.cbz 잠금 원인 미확정·종료 상한 없음은 유지한다. 제품·DB/API/SDK/엔진/native 번들 변경, 직접 병합·공개 Release/tag·VSR은 없다.
