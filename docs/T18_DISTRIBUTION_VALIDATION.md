# T18 Windows 패키징 및 배포 검증

## 범위와 기준

main `7c16295` / PR #28 통합 `e850a64`, AI_WORKFLOW/TASKS T18/D08 및 T18A-6 인계를 기준으로 `task/t18-windows-packaging`에서 패키징·기존 통합 검사·사용자 안내만 준비한다. 제품 소스 코드/DB/API/감상/삭제/종료 정책을 보존한다. src 변경은 App/Core packages.lock.json 추가뿐이다. 기존 검증 의미는 유지하고 T18에서 T13 상태 대기와 T17 후보 ID 확인만 보강한다. 이 절은 최초 T18 패키징 범위다. 현재 자체 코드·작성 문서는 PR #32에서 MIT 적용 완료이며 제3자/전체 번들의 배포 조건 검토는 남아 있다. 공개 Release/tag·병합·VSR은 범위 밖이다.

## 재현 절차

Windows x64, PowerShell 7, SDK **10.0.401**, runtime **10.0.12**, NuGet.org 단일 source를 사용한다. 폴더형 self-contained publish이며 single-file/trim/ReadyToRun은 끈다. exe·managed DLL·Desktop runtime·SQLite·SkiaSharp·libVLC x64/plugins가 필요하다. SDK는 빌드용, 실행 프로세스는 압축 해제 폴더의 host/runtime를 사용한다. VideoLAN의 기본 publish 콘텐츠에는 win-x86/win-arm64도 포함되지만 실행 앱/runtime와 검증 대상은 x64이며 다른 아키텍처 지원을 뜻하지 않는다.

```powershell
./scripts/Package-Windows.ps1
./scripts/Package-Windows.ps1 -OutputDirectory artifacts/t18-repeat
./scripts/Verify-WindowsPackage.ps1
```

최초 CI에서 `-BootstrapLock`으로 resolve/content hash를 확보한 뒤 App/Core 잠금 파일을 저장했다. 현재 CI와 기본 스크립트는 `--locked-mode`이며 dependency graph/content 변경은 실패한다. 출력 폴더는 존재하지 않아야 하며 새 목적지를 지정한다. ZIP 이름에 commit SHA, `build-info.json`에 설정, `dependencies.json`에 전체 NuGet 버전/content SHA512와 고지 파일 경로, `files.json`에 전체 payload 경로/크기/SHA256를 기록한다. ZIP은 파일 정렬·고정 entry timestamp로 만들고 같은 SHA의 두 publish 결과 ZIP SHA256 일치를 실제 검사한다. 다른 OS/SDK 버전까지 byte 동일을 보장하지 않는다.

## 검증 설계와 판정 경계

- ZIP을 별도 한글/공백 폴더에 풀고 모든 파일의 hash를 대조한다. 기존 앱 데이터 폴더가 없는 폐기 가능한 Windows 계정의 실제 LocalAppData에서 앱 exe를 직접 실행→정상 종료→재시작하며 실제 DB 생성과 bundled coreclr/SQLite load 경로를 확인한다. 기존 데이터 폴더가 있으면 검증을 시작하지 않는다. 앱은 Windows Known Folder API를 사용하므로 LOCALAPPDATA 환경변수 변경으로 격리를 주장하지 않는다. 사용자 DB는 사용하지 않는다.
- 별도 ZIP 복사본에 기존 테스트 harness entry assemblies/config/fixtures만 추가한다. 원래 패키지 파일 hash는 모두 유지되어야 한다. Core/Data/Comic/Scanner(10 시나리오)/Video/Subtitle/Viewing 전체 검사를 **ZIP dependencies**로 실행한다. 테스트 앱은 exe 직접 실행, PATH에서 SDK/VLC 경로 제외·DOTNET_ROOT를 빈 위치로 둔다. coreclr/SQLite/Skia/libVLC/core 실제 모듈 load 경로가 해당 ZIP 복사본 아래인지 확인한다.
- 만화/영상/자막·랜덤→감상→정리·실패 보존·DB 재열기·삭제/저널·숨김/정상 종료는 기존 검사 조건을 유지하며 T13/T17의 아래 보완을 포함한다. 제품 exe의 실제 사용자 클릭/물리 키/청취 전체 수동 시나리오와 동일하다고 표시하지 않는다. 테스트 harness에서 쓰는 fixture는 운영 데이터가 아닌 임시 복사본이다.
- CI에는 SDK/런타임이 설치되어 있다. packaged host/runtime 실제 경로와 직접 실행은 별도 설치에 의존하지 않는 증거지만 **사전 설치가 전혀 없는 깨끗한 사용자 Windows PC 검사**를 대체하지 않는다. 실제 Windows 10/11 사용자 PC, NAS/RaiDrive/Explorer/로그오프/원격 휴지통/단절/캐시는 미실시다.
- 테스트 timeout 이후 kill은 폐기 가능한 CI fixture process 정리 전용이며 제품 정상 종료 시간 상한/강제 종료 정책을 추가하지 않는다.

## 결과

최초 제출의 패키징/검증 코드 SHA `2599f48d96d5ca18ab389b719926424894a999b8`의 Windows 자동 검증은 최초 실행 성공했다. 아래 정확한 결과를 기준으로 하며 깨끗한 사용자 PC 및 원격 실물을 성공으로 추정하지 않는다. artifact는 `t18-windows-review-<SHA>-attempt-<n>`(14일 보존)로 ZIP/ZIP hash·manifest·dependency metadata·locks·검증 로그를 남긴다. 실패 실행의 evidence도 보존한다.

## 지원 및 공개 배포 판단

.NET 10 공식 OS 표(2026-10-11 접근 확인)는 Windows 11의 현재 지원 에디션과 Windows 10 일부 Enterprise/LTSC·IoT만 열거한다. 앱 사용자 대상은 Windows x64 Desktop이고 Server CI 성공을 Windows 10/11 전체 보장으로 해석하지 않는다. 현재는 **CI 검토 패키지 준비**이며 공개 배포 준비 완료를 선언하지 않는다.

공개 배포 전 남은 항목: exact native 번들/플러그인 고지·corresponding source 제공 방식 확정, 깨끗한 사용자 PC 압축 해제 실행 및 실제 기본 흐름 확인. dependencies metadata/추출 license 원문과 이 표의 조건을 대조해야 하며 metadata만으로 재배포 완료 선언을 하지 않는다. [의존성 고지](../packaging/THIRD_PARTY_NOTICES.md), [사용자 안내](../packaging/USER_GUIDE.md)가 패키지에 포함된다.

원격 휴지통 Unknown/실행 차단·임의 영구삭제 전환 없음·actual IO drain/정상 종료 시간 상한 없음·AVI 조사 보류 및 기존 수동 미검증/사용자 승인 생략은 그대로다. 실제 NAS/RaiDrive는 [T18A-6 N01~N12 실사용 점검표](T18A_6_INTEGRATION_VALIDATION.md)로 이관하고 반복 조사하지 않는다. T11 최초 native dialog 실패/수정 없는 재실행 통과 이력도 보존한다.

## 깨끗한 사용자 PC 재개 조건

SDK/.NET Desktop Runtime/VLC를 설치하지 않은 지원 대상 Windows x64 Desktop의 새 계정 또는 폐기 가능한 VM이 필요하다. 공개 배포 승인 전 검토 ZIP을 전부 압축 해제하고 아래 결과·OS 에디션/빌드·ZIP SHA를 기록한다. 현재 환경은 Linux이며 사용자 PC/VM 접근은 제공되지 않았다.

| 확인 | 절차와 기대 결과 |
|---|---|
| 실행 의존성 | 별도 런타임/VLC 설치 없이 exe 실행. 분류 화면·DB 생성, 오류 없이 정상 종료·재시작 |
| 기본 흐름 | 테스트 CBZ/영상/한글 SRT·SMI 복사본 등록→수동 스캔→랜덤 감상→다음/이전→즐겨찾기/이번 제외→정상 닫기. 이력·진행·상태 재시작 보존 |
| 실패/정리 | 테스트 파일 잠금/부재에서 기존 감상·기록 보존. 별도 복사본만 휴지통·명시적 영구삭제 확인. 실패/불명은 성공 처리되지 않아야 함 |
| 교체/종료 | 정상 종료 후 새 ZIP을 새 폴더에 풀어 같은 계정으로 실행, 기존 DB/저널 유지. 숨김/복원·정상 종료는 실제 입력으로 확인. 지연 IO의 종료 상한을 보장하지 않음 |

네트워크 장비는 이 PC 기본 검사에 포함시켜 통과로 추정하지 않는다. NAS/RaiDrive는 기존 N01~N12 점검표의 장비/버전/권한 기록이 확보될 때 별도로 수행한다.

## 검증 실패 보존과 검사 보완

| SHA / CI | 관찰 결과 및 조치 |
|---|---|
| `6e5426e` / [38139554398](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/38139554398) | publish/동일 ZIP/실제 앱 시작·종료 후 테스트의 DB 경로 assertion 실패. Windows Known Folder API를 LOCALAPPDATA 환경변수로 격리할 수 있다고 잘못 가정한 검증 스크립트를 수정했다. 제품 DB 경로는 변경하지 않음 |
| `1238be1` / [38139716289](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/38139716289), `9eaae4c` / [38139923552](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/38139923552) | ZIP 생성/byte 일치·셸 2회 및 Viewing 이전 suite 통과, T13 고정 3.5초 후 전체화면 자동 숨김 assertion 실패. cleanup의 DB 잠금 오류도 로그 보존. 제품 수정으로 해결했다고 선언하지 않음 |
| `004cdae` / [38140254928](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/38140254928) | 새 콘솔을 띄우지 않도록 harness 실행만 보완. ZIP·셸·Core/Data/Comic/Scanner/Video/Subtitle 통과, T17 `Program.ClickOnAndWait`의 전환 조건 45초 timeout. 네이티브 파일 선택창 실패로 분류하지 않음. 원인은 미확정 |
| `210cf9b` / [38140647609](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/38140647609) | 당시 정확한 SHA에서 ZIP 생성/byte 일치·셸 2회·전체 ZIP 의존성 회귀와 실제 native load 경로 검사 최초 실행 성공. 앞선 실패가 없어진 것으로 기록하지 않음 |
| `6675d5e`, `391abfb` | 보완 테스트의 중복 지역 변수 CS0128로 Viewing 컴파일 실패. 원래 제품 결함이 아닌 검증 코드 오류이며 만화/영상 대기 변수와 대상 컨트롤을 분리해 수정 |

T17의 실패 재현용 absent 항목은 원래 검사에서도 후속 UI 전에 `ApplyObservedItems([], [absent.Id])`로 Missing 처리된다. 실패 원인을 잔류 후보로 단정한 추정은 철회했고 중복 제외 처리는 최종 코드에 남기지 않는다. 대신 후속 random fixture가 정상 영상 **정확한 두 ItemId 집합**인지 직접 검사한다. T17 timeout을 해결한 제품 수정으로 표현하지 않는다.

T13은 기존 3.5초 sleep을 실제 공통/만화 또는 영상 컨트롤의 Collapsed 상태를 최대 8초 관찰하는 대기로 바꾼다. 타이머의 **정확한 3초 Interval** assertion을 별도로 추가하며 마우스로 다시 표시·기존 전체화면/숨김 복원 검사도 유지한다. 늦은 레이아웃/입력 이벤트를 고려하는 검증 보완이고 제품 타이머나 UX 정책 변경이 아니다. 정상 종료 정책에도 영향이 없다.

같은 `9eaae4c`의 기존 통합 CI [38139923459](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/38139923459) 최초 실행은 `T16 native dialog restored False`로 실패했다. 동일 SHA의 T11 suite 및 이후 코드 SHA의 통합 성공을 해당 실패 삭제의 근거로 쓰지 않는다. 기존 T11 최초 파일 선택창 실패/재실행 이력은 T18A-6 문서에 그대로 있다. 이 통합 CI는 코드 변경 없는 동일 SHA 재실행 attempt 2에서 success(job `114472485340`)였다. 최초 실패는 보존한다. 이번 실패들은 정확한 SHA와 원본 CI/아티팩트로 보존한다.

## 최초 제출의 정확한 SHA 결과 — PR #29

- 코드/테스트/패키징 검증 SHA: `2599f48d96d5ca18ab389b719926424894a999b8`. Windows Server 2025 x64 / OS 10.0.26100 / SDK 10.0.401 / runtime 10.0.12. 글로벌 VLC 실행 파일 없음. SDK/runtime가 설치된 CI이며 깨끗한 사용자 PC는 아니다.
- [T18 CI 38140925671](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/38140925671), job `114474462116`, attempt 1 **success**. locked restore, self-contained folder publish, 두 번 publish의 ZIP byte 일치, 한글/공백 경로 압축 해제 및 실제 셸 시작→정상 종료→재시작 2회, Core/Data/Comic/Scanner 10개 시나리오/Video/Subtitle/Viewing 전부 통과. T13 만화·영상 Interval 3초/실제 숨김 상태와 T17 정확한 두 영상 ItemId 검사도 PASS다.
- [검토 artifact 11703302128](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/38140925671/artifacts/11703302128): `t18-windows-review-2599f48d96d5ca18ab389b719926424894a999b8-attempt-1`, 2026-10-25 만료. ZIP/ZIP SHA256, build-info/files/dependencies JSON, App/Core 잠금 파일, 16회 harness 실행 stdout/stderr 및 loaded-native-modules.txt 포함. 재실행 artifact는 attempt별 다른 이름을 사용하여 이전 실패 증거를 덮어쓰지 않는다.
- 패키지: `RandomMultimediaManager-win-x64-2599f48d96d5.zip`, **231,919,233 bytes**, SHA256 **`75c341ff13ca6441c96657954d2cc3c166a34a555622904a86541125e1acc348`**. ZIP 내부 1,719 파일, manifest 1,718행(자기 자신인 files.json 제외). `build-info.json`의 commit/SDK/runtime/selfContained/publicReleaseApproved(false)를 직접 대조했고 artifact 다운로드 후 manifest의 모든 파일 SHA256와 ZIP SHA256도 재검증했다.
- 실제 로드: 셸의 hostfxr/hostpolicy/coreclr/SQLite, ZIP 복사본 harness의 동일 runtime과 SQLite/SkiaSharp/libVLC/libvlccore 모두 해당 압축 해제 루트 아래 경로로 확인. PATH에서 dotnet/VLC 설치 경로 제외·DOTNET_ROOT를 빈 위치로 둔 exe 직접 실행이다. **클린 PC 검사 대체가 아니며**, runtime가 전혀 없는 사용자 Windows 10/11은 미실시다.
- 동일 SHA의 [통합 38140925655](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/38140925655), [T03 38140925663](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/38140925663), [T06 38140925657](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/38140925657), [T09 38140925672](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/38140925672), [T11 38140925660](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/38140925660)도 전부 success. 기존 통합의 솔루션/추가 4개 Release 빌드 경고 0·오류 0을 확인한다.

| 실제 native payload | 버전 근거 | SHA256 |
|---|---|---|
| `e_sqlite3.dll` | SQLitePCLRaw.lib.e_sqlite3 2.1.13 / PE FileVersion 3.53.3.0 | `b7385d722c83fb52142a00477a726723745916d22a555711ee89834c1111fb2e` |
| `libSkiaSharp.dll` | SkiaSharp.NativeAssets.Win32 4.151.2; 고정 FileVersion resource는 발견하지 못함 | `09fb4b1afee9810f127672baff0e050ed076b6d889b90adec7271914c3894125` |
| `libvlc/win-x64/libvlc.dll` | native 3.0.23.0 / VideoLAN NuGet 3.0.23.1 | `8ae9f16a72441f43fb4ae8f72c843736726e067ea4a8def2646748631cc4e872` |
| `libvlc/win-x64/libvlccore.dll` | native 3.0.23.0; 실행 로그 `3.0.23-2-0-g79128878dd` / plugins 436 modules | `d3475b834dd3eb77910f37f71b0341d358bcbdda5b9f04cc4a3a8e2be1bc8e35` |

native/runtime 라이선스 원문·metadata는 ZIP의 dependency-notices와 licenses에 포함된다. runtime pack LICENSE/THIRD-PARTY-NOTICES 원문과 SHA512도 수집했다. 전체 경로/크기/hash는 files.json, 정확한 NuGet graph/content hash는 dependencies.json 및 App/Core locks가 단일 증거다. 최초 제출 SHA 이후 `5bdf748`까지는 결과·상태 문서만 변경했다. 이후 SDK/CI 보완은 아래 별도 SHA 결과를 따르며 최초 패키지·검증 이력을 보존한다.

판정: **CI 검토용 패키지와 안내·PR 제출 준비 완료, T18의 공개 배포 준비는 보류**. 앱 라이선스/정확한 native 소스 제공·플러그인 조건/깨끗한 사용자 PC가 남아 있다. 실제 NAS/RaiDrive 및 기존 수동 미검증/승인 생략·AVI 보류·원격 휴지통 Unknown 차단·실제 IO drain/종료 상한 없음은 유지한다. 직접 병합·공개 Release/tag·VSR 착수는 없다.


## PR #29 SDK/CI 정합성 보완 — 재검토 근거

최신 main `8628e81b0705af8e5cce73edd4ec68b3de32be1b`의 보완 지시를 기존 브랜치에 통합했다. 수정 SHA는 `61a98bb5f51a0aa997ab37429e00cee3be18a4cf`다. T03/T05/T06/T07/T09/T10/T11/T18A-6의 `10.0.x`와 T18의 중복 고정값을 모두 제거하고 9개 workflow의 setup-dotnet을 **`global-json-file: global.json`**으로 통일했다. global.json의 SDK 10.0.401 / rollForward disable과 runtime/라이브러리 버전은 그대로다.

모든 workflow에 `global.json`, `NuGet.Config`, 솔루션, `**/*.csproj`, `**/packages.lock.json` paths를 포함해 공통 SDK·복원·프로젝트 입력 변경이 필요한 CI를 누락하지 않게 했다. 기존 source/test 및 자기 workflow 필터는 보존했다. 현재 추가 props/targets/Directory.Build 파일은 없다. 이후 공통 빌드 입력을 도입하면 paths에 포함해야 한다.

기존 suite의 restore/build/run 명령·assertion·timeout은 변경하지 않았다. 모든 job은 PR head를 명시적으로 checkout하고 restore 전 `git rev-parse HEAD` 및 `dotnet --version`을 PR head/global.json과 직접 비교한다. 정적 YAML 대조에서도 9개 setup 입력·공통 paths·기존 검증 단계/조건 보존을 확인했다. 이번 보완은 workflow와 상태/검증 문서만 변경하며 src/tests/scripts/packaging/global.json/NuGet.Config는 `5bdf748`와 동일하다. App/Core 잠금 파일과 패키징 기본 `--locked-mode`도 유지한다.

새 T18 [run 38142117345](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/38142117345), job `114477931879`, attempt 1 success. SDK 선택·locked restore/self-contained publish·ZIP byte 일치·압축 해제 셸 정상 종료/재시작 2회·ZIP 의존성 전체 16회 harness 실행/native 로드 경로가 통과했다. Windows Server 2025 x64 / SDK 10.0.401 / runtime 10.0.12이며 깨끗한 사용자 Windows PC 검증은 아니다.

- [새 artifact 11703665480](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/38142117345/artifacts/11703665480): `t18-windows-review-61a98bb5f51a0aa997ab37429e00cee3be18a4cf-attempt-1`, 2026-10-25 만료. artifact ZIP 자체 SHA256는 `e945111bb4615e6df597d4eab403a5e6727899a7497de0ee024d99119c98f0e2`다.
- 새 패키지 `RandomMultimediaManager-win-x64-61a98bb5f51a.zip`: **231,919,246 bytes**, SHA256 **`53f8128461bc3153b84708340b20888d4794672c520ba97e781bde46ee3cb045`**. payload 1,719 파일 / manifest 1,718행. artifact 다운로드 후 ZIP 해시·build-info의 exact SHA/SDK/runtime 및 모든 manifest 파일의 크기/해시를 다시 대조했다. 새 SHA를 포함한 metadata/assembly 때문에 기존 ZIP과 다른 해시이며 동일 SHA 두 publish의 byte 일치는 별도 PASS다.
- 최초 `2599f48`의 artifact `11703302128` / run `38140925671`, ZIP SHA256 `75c341ff13ca6441c96657954d2cc3c166a34a555622904a86541125e1acc348` 및 위 성공/실패 이력은 그대로 보존한다. 새 결과가 이전 실패를 해결했다는 선언은 하지 않는다.
- 통합 [run 38142117377](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/38142117377) attempt 1, job `114477932059`: SDK 선택/restore·솔루션 및 추가 4개 Release build 경고 0·오류 0, Core/Data/Scanner/Comic/Video/Subtitle 성공 후 Viewing 실패. 마지막 T17 manual insert PASS 후 finally의 `Directory.Delete`에서 `comic.cbz` 파일 잠금 IOException을 기록했다. finally가 앞선 예외를 가릴 수 있어 이전 단계의 실패 원인은 단정하지 않는다. 제품/검증 코드를 바꾸지 않고 동일 SHA 실패 job을 1회 재실행했다. 최초 실패는 보존하며 T11/T18의 별도 성공으로 대체하지 않는다.


| 영향 workflow / run | job | 정확한 `61a98bb` 결과 |
|---|---|---|
| [T03 38142117342](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/38142117342) | `114477932386` | attempt 1 success |
| [T05 38142117337](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/38142117337) | `114477931925` | attempt 1 success |
| [T06 38142117347](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/38142117347) | `114477931971` | attempt 1 success |
| [T07 38142117330](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/38142117330) | `114477932092` | attempt 1 success |
| [T09 38142117348](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/38142117348) | `114477931869` | attempt 1 success |
| [T10 38142117339](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/38142117339) | `114477932036` | attempt 1 success |
| [T11 38142117321](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/38142117321) | `114477931934` | attempt 1 success |
| [T18 38142117345](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/38142117345) | `114477931879` | attempt 1 success |
| [T18A-6 38142117377](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/38142117377) | `114477932059` → `114478797035` | attempt 1 failure → 동일 SHA attempt 2 success |

9개 job 로그에서 `Verified checkout SHA: 61a98bb5f51a0aa997ab37429e00cee3be18a4cf`, `Verified global.json SDK: 10.0.401`와 실제 restore/build/publish 성공을 확인했다. 기존 8개 검증 workflow의 명시적 Release build는 경고 0·오류 0이며 통합 재실행의 솔루션/추가 4개 빌드도 각각 0이다. 통합 attempt 2는 전체 suite와 셸 정상 종료 2회까지 성공했지만 최초 파일 잠금 실패의 해결/원인 규명은 아니다. 이후 결과 확정 커밋은 CURRENT_STATE/TASKS/이 검증 문서만 변경한다.

새 manifest와 최초 manifest의 payload 경로 집합 및 네 개 native 파일 SHA256가 동일하다. 변경 payload는 build-info와 App/Core의 exe/DLL/PDB이며 소스/의존성 버전 변경이 아닌 새 SourceRevisionId를 기록하는 재빌드다. 두 SHA의 ZIP byte 동일을 주장하지 않는다.

판정: **SDK 설치/paths 정합성 보완 확인·PR #29 main 통합 완료 (09eb08f)**. 공개 배포의 앱 라이선스·native corresponding source/플러그인 조건·깨끗한 사용자 PC 잔여 항목과 NAS/RaiDrive 실물 미검증, 원격 휴지통 차단·수동 미검증/승인 생략·AVI 보류·종료 시간 상한 없음은 유지한다. 직접 병합·공개 Release/tag·T19는 수행하지 않는다.

## T18 공개 배포 준비 잔여 작업 — 자료 PR

최신 main `163f88f` / PR #29 통합 `09eb08f`에서 `task/t18-release-readiness`를 시작했다. 제품·DB/API/감상/삭제/종료 정책, SDK/패키지 버전·workflows·tests·패키징 스크립트를 변경하지 않았다. 자료 검증은 기존 검토물 대조/제3자 자료와 누락 정리다. PR #30의 정확한 자료 SHA `748eddd244ae3749c589b3ac4bc90a0b76ed074e`에서 별도로 시작된 [T18 CI 38194679543](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/38194679543), job `114634629592`는 attempt 1 success다. 새 결과/검토 artifact는 아래와 같이 기존 동결 evidence와 구별한다.

- 2026-10-12 artifact `11703665480`을 다시 내려받아 위 artifact ZIP 및 내부 ZIP SHA256·bytes, build-info와 1,718개 manifest 파일 크기/hash 전부를 확인했다. 결과 PASS. 새 원문 자료는 기존 ZIP에 들어 있지 않으며 이전 검증 이력을 덮어쓰지 않는다.
- 실제 native/지원 파일 1,285행, plugin DLL x64 323 / x86 323 / ARM64 310의 경로·hash를 [자료](../packaging/release-readiness/README.md)에 보존했다. 기존 436 modules는 실행 로그 값이며 plugin DLL 파일 개수와 구별한다. 공식 VLC ZIP의 해당 파일 1,256개 byte 일치, x64 import .lib 2개는 공식 7z와 일치. x86/ARM64 import .lib 4개는 공식 파일 대조 미완료다.
- **새 배포 차단 근거:** LGPL nuspec와 달리 세 아키텍처의 avcodec/swscale 6개 바이너리에서 GPL FFmpeg license/configure 증거를 확인했다. 정확한 hash·문자열은 GPL_FFMPEG_EVIDENCE.json이다. `.GPL` 패키지 미참조·upstream GPL 필터 통과를 LGPL-only 판정으로 사용하지 않는다. 앱 라이선스 선택이나 엔진/번들 변경은 수행하지 않았다.
- 공식 VLC source revision은 native와 같은 3.0.23-2 / `79128878ddb2c280bbb6c89c76a46b31a80ade1c`; 공식 checksum과 일치한 tar를 보존했다. LibVLCSharp 두 nuspec의 exact commit source, SQLite native와 동일 source ID의 amalgamation, VLC contrib SHA512SUMS와 일치한 FFmpeg source도 확보했다. archive 4개·43,450,258 bytes 및 hashes는 source-inputs.json을 따른다. 원문 licenses/고지·정확한 Skia submodule/build 정보도 보존한다.
- **미완료 구분:** source tar/recipes 확보는 full corresponding source 제공 완료가 아니다. 결합된 contrib 전체 소스·패치/빌드 provenance·per-file 고지/라이선스 대응·완전한 source sidecar·앱 라이선스/EULA 검토가 남았다. 현재 source build/교체 검증은 미실행이다. 후속 선택지는 같은 자료의 차단/선택지 표를 따른다.
- Linux 작업 환경이며 사전 설치 없는 Windows PC/VM 접근은 없다. 기존 사용자 PC 점검표의 미검증 상태를 유지한다. 새 계정으로 machine-wide SDK/runtime 제거를 가정하지 않으며 사용자에게 설치 제거를 요구하지 않았다.
- 통합 `38142117377` attempt 1의 comic.cbz cleanup 잠금 실패와 동일 SHA attempt 2 성공은 그대로 보존한다. 이번 작업에서 새로 재현되지 않았고 조사/수정/원인 해결을 선언하지 않는다. NAS/RaiDrive·원격 휴지통 Unknown/차단·AVI 보류·기존 수동 미검증/사용자 승인 생략·종료 상한 없음도 유지한다.

판정: **가능한 제3자 자료 정리와 누락/제공 절차 인계 완료, 공개 배포는 차단 유지**. PR 검토만 요청하며 직접 병합·공개 Release/tag·VSR 착수는 하지 않는다.

### 자료 SHA 748eddd의 Windows 검증과 원문 byte 보존 보완

- run `38194679543` / job `114634629592`, exact SHA `748eddd244ae3749c589b3ac4bc90a0b76ed074e`, attempt 1 success. Windows Server 2025 x64 / SDK 10.0.401 / runtime 10.0.12. 두 publish ZIP byte 일치·압축 해제 셸 정상 종료/재시작 2회·전체 ZIP 의존성 16회 harness/native 경로 검사가 최초 실행 통과했다.
- [artifact 11723171100](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/38194679543/artifacts/11723171100), 만료 `2026-10-26T02:13:44Z`. artifact ZIP 231,509,000 bytes / SHA256 `cdf1217d7ecdb87880bf982e1302b00910464a6c67cc75448499499322c3d92e`. 내부 ZIP `RandomMultimediaManager-win-x64-748eddd244ae.zip` 232,000,245 bytes / SHA256 `709f052a9228b70390ee77b9f31e94460fceb3c570ccbc7db3a405125d6461a2`. 다운로드 후 build-info 및 manifest 1,726행 모두 검증했다(payload 1,727 파일).
- 이전 ZIP 대비 라이선스 원문 8개 추가. 기존 파일 변경은 새 SourceRevisionId의 App/Core assembly/PDB·apphost, build-info, THIRD_PARTY_NOTICES다. 동결 inventory 1,285개 중 apphost만 새 revision으로 달라지고 나머지 1,284개 native/지원 파일 해시는 동일하다. archive 소스는 runtime ZIP에 포함되지 않으며 full source sidecar로 오인하지 않는다.
- 추가 QA에서 Windows Git checkout이 LF license를 CRLF로 바꾸는 것을 확인했다. 해당 텍스트는 줄바꿈을 정규화하면 원문과 같지만 byte 동일이 아니므로 원문 byte 보존을 완료 처리하지 않았다. packaging/.gitattributes에서 licenses 및 수집 notices/upstream/sources에 `-text`를 지정해 checkout 변환을 막는다. 원문 자체를 수정한 것은 아니다. 압축 해제 ZIP에 없는 상대 자료 링크도 exact `748eddd` 저장소 링크로 바꿨다. 이 보완은 packaging 고지/원문 보존만 바꾸며 제품·SDK/의존성·테스트/스크립트 변경은 없다.
- 이 보완의 새 exact SHA/CI/artifact는 PR #30에서 별도로 확인한다. 위 success/ZIP은 보완 전 증거이며 보완 후 원문 byte 일치 검사와 혼동하지 않는다. CI success를 라이선스 조건/깨끗한 PC·이전 comic.cbz 원인 해결의 근거로 사용하지 않는다.

### 원문 byte 보존 보완의 정확한 SHA 검증 — PR #30

- 보완 SHA `2b9ad561590ac6e97887980b26277398a8e3b424`의 [T18 CI 38195206459](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/38195206459), job `114636222826`, attempt 1 **success**. Windows Server 2025 x64 / SDK 10.0.401 / runtime 10.0.12. 두 ZIP byte 일치, 압축 해제 셸 시작·정상 종료·재시작 2회, 전체 ZIP 의존성 16회 harness 및 native/runtime load 경로 검사가 최초 실행 통과했다. 깨끗한 사용자 PC 검증은 아니다.
- [artifact 11722139575](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/38195206459/artifacts/11722139575), `t18-windows-review-2b9ad561590ac6e97887980b26277398a8e3b424-attempt-1`, `2026-10-26T02:21:49Z` 만료. artifact ZIP **231,508,693 bytes**, SHA256 `4c24204e4fdd1f802f5f4875d6b3c5f6742dc16a85920e783d936a23c322c8f8`.
- 내부 `RandomMultimediaManager-win-x64-2b9ad561590a.zip`: **231,999,650 bytes**, SHA256 `c9e488ab078dc354f5572edc8c52e36383af4dfae4a55545f4e58898c7089d04`. 다운로드 후 artifact/내부 ZIP 해시·build-info의 exact SHA/SDK/runtime/publicReleaseApproved false, manifest 1,726개 크기/hash를 전부 대조했다(payload 1,727 파일).
- `packaging/licenses` 11개가 이번 ZIP에 **원본 byte 그대로** 포함되는 것을 확인했다. `core.autocrlf=true` checkout의 수집 원문·소스/빌드 자료 51개도 byte 일치다. 패키지 안내의 자료 링크는 압축 해제 폴더에 없는 상대 경로가 아닌 exact `748eddd` 저장소 자료로 연결된다.
- 동결 native/지원 inventory 1,285개 중 새 SourceRevisionId의 apphost만 달라지고 나머지 1,284개 hash가 일치한다. GPL FFmpeg 6개 DLL과 native 버전은 바뀌지 않았다. 최초 `748eddd`의 CI 성공/줄바꿈 변환 관찰을 보존하며 이번 결과와 구별한다. 원래 `61a98bb` 검토 artifact `11703665480`과 source 대응표는 계속 동결 기준이다.
- 이후 결과 확정은 CURRENT_STATE/TASKS/이 검증 문서만 변경한다. packaging 고지·원문·소스, 제품 및 검증 입력은 이 검증 SHA와 동일하며 후속 문서 SHA에서 새 패키지를 검증했다고 주장하지 않는다. 자료 정리/PR #30 검토 인계 완료, **공개 배포 차단 유지**다. 앱 라이선스·GPL FFmpeg 처리/전체 contrib source 제공·깨끗한 PC, 기존 미검증/잠금 원인 미확정은 해결 처리하지 않는다. 직접 병합·공개 Release/tag·T19는 수행하지 않았다.

## T18-D1 — MIT 적용·소스 제공 검토 PR #32

기준 main 690ac0b, task/t18-d1-license-source. 자체 소유 코드·작성 문서의 MIT를 root LICENSE에 적용하고 전체 ZIP/제3자의 원문 조건과 구별했다. 권리/graph 판단·binary별 제공과 누락·재개 조건은 [자료 README](../packaging/release-readiness/README.md#t18-d1-라이선스-적용과-검토용-소스-묶음)를 따른다. LICENSE를 전체 번들 재라이선스로 사용하지 않는다. 제품·DB/API·SDK/엔진/번들은 바뀌지 않았다.

- source-inputs의 기존 4개 archive byte/hash 보존, GSM/OpenJPEG/LAME/zlib 4개 후보 archive 추가. VLC contrib SHA512SUMS와 대조하고 원문 고지 6개를 포함했다. 총 8개 source archive 48,212,560 bytes. pthreads mirror 404, 실제 linked versions/patch 분기와 전체 build provenance·per-file graph 양립은 미완료다.
- sidecar는 exact Git 원문 blob의 application.tar, 8개 archives, 모든 contrib recipe/patch 원문(116 recipes), 1,285개 native/support 행의 source 후보·원문 headers/link assignments, evidence·LICENSE·627-entry manifest를 포함한다. plugin target 956행 전부 후보가 있으며 그중 953행에 literal source 파일이 있다. 조건/변수 기반 3행을 포함한 후보는 확정 build attestation이 아니다. completeCorrespondingSource/publicReleaseApproved는 false다.

### 최초 검증과 플랫폼 차이 보완

SHA aade32d2c21950dc23bddf68abb1fbe35632a0a7의 [run 38337004925](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/38337004925), job 115099254567, attempt 1 success. source와 runtime repeat byte 일치·앱 셸 2회·전체 ZIP 회귀·라이선스 포함 검사가 통과했다. [artifact 11784538980](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/38337004925/artifacts/11784538980): 281,862,706 bytes, SHA256 82af9528248228bb90d100079a24cfb2476a96b7e1b7917b49fdbb7c5b65ceea.

Windows source ZIP 58,727,716 bytes/hash a9fc9f02221548e4b8c20bb1166dacdd51f1696df4fb9875699763f569f439fb와 Linux source ZIP 58,707,236 bytes/hash 04d8837bfdefe1b55b8fe3761d060e7fd10c39a0d6b83c383db192aea9d8d913는 달랐다. 직접 entry 대조에서 application.tar/manifest만 달랐고, tar 내부 143개 파일이 Git archive의 checkout 줄바꿈 변환으로 달랐다(LF 정규화 후 동일). runtime ZIP 232,017,120 bytes/hash 858cf10db664a75e26306e92198e3e655b96ef8ed2d40e01d08bb16305c5f0d6의 1,733개 manifest·root LICENSE·17개 upstream licenses 및 기존 native/support 1,284개 해시는 검증됐다. 이 최초 성공을 플랫폼 간 byte 일치로 해석하지 않는다.

보완은 git cat-file --batch로 원문 blob을 읽어 고정 순서/mtime/mode/uid/gid의 tar를 만드는 변경이다. 제3자 archives·고지·제품 입력은 바꾸지 않았다.

### 보완한 exact SHA Windows 검증과 산출물 재대조

- SHA **450cbb4fe2e73fb7f622c0b08a80a8c9e3342239**, [run 38337573828](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/38337573828), job **115101033114**, attempt 1 **success**. Windows Server 2025 x64, SDK 10.0.401/runtime 10.0.12. source와 runtime repeat ZIP byte 일치, root LICENSE/원문 licenses/안내 포함, 압축 해제 셸 시작·정상 종료·재시작 2회, 전체 ZIP 의존성 16회 harness/native load 경로가 통과했다. 클린 사용자 PC 검증은 아니다.
- [artifact 11785329450](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/38337573828/artifacts/11785329450), 만료 2026-10-27T02:38:55Z: **281,859,611 bytes**, SHA256 **3d7a1e206b87c2cc5e151b3fcb3e63f9e82efc51a63ea82e7071bd8bf4d12141**. 내려받아 hash/크기를 재대조했다.
- runtime `RandomMultimediaManager-win-x64-450cbb4fe2e7.zip`: **232,017,137 bytes**, SHA256 **10ba65e0db4dd04c23fff3ad0f35e5ec79daea83cb73654332f7f103302cdae1**. 1,734 files/manifest 1,733행의 경로 집합·크기/hash, build-info exact commit/publicReleaseApproved false를 검사했다. root LICENSE와 17개 upstream license 원문은 byte 그대로다. 자체 안내 2개는 Windows CRLF checkout이 적용되며 LF 정규화 후 Git 원문과 같고 CI는 checkout 원문과 byte 일치를 검사했다.
- source `RandomMultimediaManager-source-review-450cbb4fe2e7.zip`: **58,686,756 bytes**, SHA256 **10a0207286134471a6de1636cf40901496880620b136832256e27cdec87b3fea**. artifact의 Windows ZIP이 Linux 동일 SHA 반복 생성물과 **전체 byte 일치**한다. 627-entry manifest·entry 집합/중복·size/hash·status와 application LICENSE를 다시 검증했다. 8개 source archives/input hashes를 대조했다. 최초 SHA에서 별도로 LICENSE entry를 변조한 ZIP은 verifier가 거부했다.
- 동결 native/support inventory 1,285개 중 SourceRevisionId의 apphost 하나만 달라지고 나머지 **1,284개 hash 동일**이다. 기존 GPL FFmpeg 6개 DLL, 엔진/버전/코덱을 그대로 보존했다. src/tests·global.json·locks·기존 evidence/archives도 변경하지 않았다.

결과 문서 커밋은 CURRENT_STATE/TASKS/이 문서만 변경한다. LICENSE·packaging·scripts/workflow·원문 소스·제품/검증 입력은 위 검증 SHA와 동일하며 이후 문서 SHA에서 새 ZIP을 검증했다고 주장하지 않는다.

판정: **MIT 자체 소스 적용 및 검토용 소스 제공물·패키징 검증을 준비해 PR #32 재검토로 제출, 전체 대응 소스·공개 배포는 미완료/차단**. native-selected contrib graph·archives·patch/build/toolchain attestation·per-file 고지/라이선스 양립을 채운 후 공개 가능 여부를 별도로 검토한다. 기존 클린 PC/NAS/RaiDrive·원격 휴지통 Unknown/차단·AVI 보류·수동 미검증/승인 생략·comic.cbz 잠금 원인 미확정·종료 상한 없음은 유지한다. 직접 병합·공개 Release/tag·T19 VSR 착수는 하지 않았다.

## PR #32 보완 — Python 최적화에도 유지되는 무결성 검사

main b9f9bdb의 보완 지시를 기존 task/t18-d1-license-source에 통합했다. 기존 MIT/고지·원문 archives·제품/DB/API·SDK/엔진/번들 및 450cbb4/최초 줄바꿈 검증 이력을 보존했다. 변경은 source sidecar 검사 9개의 assert를 명시적인 조건 검사/ValueError로 교체, 좁은 Python 회귀 스크립트와 T18 CI step/그 파일 paths 필터 추가다. 오류는 exit 1이며 실패 검증에서 PASS를 출력하지 않는다. incomplete/publicReleaseApproved false의 기존 의미는 유지한다.

### 변경 전 재현과 변경 후 회귀

0fc0c82와 동일한 변경 전 스크립트에 임시 합성 ZIP의 같은 크기 payload를 바꿔 대조했다. 일반 Python은 exit 1/AssertionError, python -O와 PYTHONOPTIMIZE=1은 각각 exit 0/PASS였다. 이는 기존 CI 성공이 검사하지 않았던 거짓 PASS이며 이전 successful artifact를 소급 무효화하거나 이 실패가 기존 실사용 자료를 변조했다는 의미가 아니다.

`python scripts/Verify-SourceSidecar.py`는 일반 Python / python -O / PYTHONOPTIMIZE=1 각각에서 아래 14개 사례, 총 **42개**를 검사한다. 회귀 자체도 assert 없이 exit/PASS/ValueError 원인을 명시적으로 판정한다.

| 영역 | 각 모드 사례 | 결과 |
|---|---|---|
| ZIP verify | 정상, payload 변경, size/hash 불일치, entry 누락/중복, completeCorrespondingSource true, publicReleaseApproved true, application LICENSE 불일치 (9개) | 정상 exit 0/PASS, 나머지 exit 1/지정 ValueError·PASS 없음 |
| build | 정상, source-input size/SHA256/제공된 SHA512 불일치, Git 응답이 blob 아님 (5개) | 정상 exit 0/PASS, 나머지 exit 1/지정 ValueError·PASS 없음 |

fixture는 TemporaryDirectory의 작은 synthetic tar/ZIP/archive만 사용한다. build fixture에서 Git 질의만 합성 응답으로 바꾸며 실제 build/input/verify 코드를 실행한다. 실제 저장소 source archives를 손상시키지 않는다. 로컬에서는 실제 8개 archives로 세 모드의 source sidecar build도 성공·전체 byte 동일을 확인했다. Git blob 타입 실패는 오류 주입이며 실제 Git 객체 손상 검증으로 표시하지 않는다.

### 정확한 수정 SHA의 Windows CI와 실제 artifact

- 수정 SHA **a420a39a62a554c9e0427b0a273a6dec7eb81d18**의 [run 38344541619](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/38344541619), job **115122761895**, attempt 1 **success**. Windows Server 2025 x64, SDK 10.0.401/runtime 10.0.12. Python 42개 회귀, source/runtime repeat ZIP byte 일치, LICENSE·고지 원문 포함, 압축 해제 셸 시작/정상 종료/재시작 2회, 기존 ZIP 의존성 16회 harness/native load 경로가 통과했다. 클린 사용자 PC 검증이 아니다.
- [artifact 11788208112](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/38344541619/artifacts/11788208112): **281,867,959 bytes**, SHA256 **28705d1d78e88376a8bad8991c8ac82a5b60fee3913ce6964acc2bcdbb9fcd75**, 만료 2026-10-27T04:07:15Z. 내려받아 실제 hash/크기를 대조했다.
- runtime `RandomMultimediaManager-win-x64-a420a39a62a5.zip`: **232,017,104 bytes**, SHA256 **f743c212706bf0bf31c5e54a5f0ba61fb3568b8853deeeca52c752ae735724e6**. 1,734 files/manifest 1,733행의 경로 집합·크기/hash, build-info exact SHA/SDK/runtime/publicReleaseApproved false를 확인했다. MIT LICENSE·upstream licenses 17개 byte를 원문과 대조하고 자체 안내 2개는 LF 정규화 후 내용 동일을 확인했다.
- source `RandomMultimediaManager-source-review-a420a39a62a5.zip`: **58,707,236 bytes**, SHA256 **a38c163f3e14ecf6b8ce19a24832fb185cf78c2f869bd7bfb526ff47a0f9493c**. Windows artifact가 Linux 동일 SHA의 일반/-O/환경 최적화 build와 전체 byte 일치한다. 내려받은 ZIP의 627-entry manifest·상태·application LICENSE를 세 모드로 재검증하고 source-input archive 8개의 크기/SHA256/제공된 SHA512를 대조했다.
- 동결 native/support 1,285개 중 revision apphost 하나만 달라지고 나머지 **1,284개 hash 동일**이다. 이전 source/runtime/artifact 해시와 새 SHA 결과를 구분한다. 원문 archives·licenses·src/tests·SDK/locks/native 설정은 이전 PR head와 동일하다.

이후 결과 확정 커밋은 CURRENT_STATE/TASKS/이 문서만 변경한다. 실제 Python/패키징/workflow·제품/검증/원문 입력은 위 SHA와 동일하며 후속 문서 SHA에서 새 ZIP을 검증했다고 주장하지 않는다. main의 보완 지시와 기존 CURRENT_STATE/TASKS 본문, 이 문서의 기존 성공/줄바꿈 이력을 손실 없이 보존했다.

판정: **최적화 모드 무결성 검사 누락 보완·회귀/Windows CI 확인, PR #32 재검토 대기**. 전체 대응 소스·license graph·클린 PC 및 기존 NAS/RaiDrive/원격 휴지통 Unknown/차단·AVI 보류·수동 미검증·comic.cbz 잠금 원인 미확정·종료 상한 없음은 유지한다. MIT 적용을 재승인 대기로 바꾸지 않았고 직접 병합·공개 Release/tag·다음 Task/VSR 착수는 하지 않았다.


## T18-D1 source-closure — 잔여 자료와 외부 증거 경계

기준 main `a7c5881`, 브랜치 `task/t18-d1-source-closure`. 기존 MIT·최적화 42개 회귀 보완은 변경하지 않았다. 이전 run/artifact·줄바꿈 보완·comic.cbz 실패/동일 SHA 재실행과 실물 미검증을 유지한다.

- 기존 8개 archive bytes/SHA256/SHA512를 보존하고 MinGW-w64 10.0.0 및 libbluray 1.4.0만 추가했다. 원본 모두 VLC recipe SHA512 일치, 원문 고지 포함. actual linked version/patch 완료는 아니다.
- 동결 native/support 1,285개 path/hash를 source-closure-inventory.csv와 대조. plugin 956개/323개 고유 target의 source/Makefile 후보, native core 6개, import 6개, Java JAR 6개, Lua bytecode 108개를 분리했다. 지원 84개 source hash 그대로, 96개 LF→CRLF 변환 hash 일치. actual linked graph는 아니며 외부 source/notice 누락을 명시한다.
- exact 공식 Windows build.sh/gitlab-ci/get-contrib-sha 원문·blob/hash와 release pipeline 674296/job 2403066/7/8 metadata를 확보했다. trace는 401, win64 archive endpoint는 404. HTTP 200 anti-bot HTML은 source로 거부했다. checksum이 맞는 후보·build instructions와 actual selected graph/실행 로그를 구별한다.
- source sidecar의 evidence/archives와 status 잔여 분류만 갱신. 제품/src/tests/DB/API·SDK/엔진/번들 및 무결성 로직 변경 없음. 전체 corresponding source/publicReleaseApproved false 유지.

검증 입력 SHA와 로컬/Windows 결과는 아래 최종 결과 절에 기록한다. 필수 소스/patch/build 지시·고지 및 단순 compiler/wrapper provenance의 차이, 정확한 누락·재개 조건/미발송 문의 초안은 release-readiness README의 source-closure 절이 단일 기준이다.

판정: 잔여 조사·검토용 자료 제출, 전체 대응 소스·license graph 미완료/공개 배포 차단. 클린 PC/NAS/RaiDrive·원격 휴지통 Unknown/차단·AVI 보류·기존 수동 미검증·comic.cbz 잠금 원인 미확정·종료 상한 없음 유지. 직접 병합·공개 Release/tag·VSR 미착수.


### source-closure 최종 검증 결과 — PR #33

검증 입력은 **e27c80d3440f544f6f06694d9286415d71611254**다. Windows Server 2025 x64/.NET SDK 10.0.401/runtime 10.0.12의 [run 38350407460](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/38350407460), job **115141274710**, attempt 1이 최초 성공했다. 이후 결과 커밋은 CURRENT_STATE/TASKS/이 검증 문서 3개뿐이며 패키징 입력을 바꾸지 않는다.

- 기존 42개 무결성 회귀(일반 Python/`-O`/`PYTHONOPTIMIZE=1`) 및 source/runtime ZIP 반복 byte 일치 통과. 무결성 로직을 재구현하지 않았다.
- 압축 해제 후 LICENSE/원문 고지 byte 검사, 셸 시작·정상 종료·재시작 2회, ZIP 의존성 경로/manifest 및 Core/Data/Comic/Scanner basic~t18a6/Video/Subtitle/Viewing 기존 회귀 통과. 클린 사용자 PC 또는 실제 NAS/RaiDrive 검증은 아니다.
- [artifact 11789979837](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/38350407460/artifacts/11789979837)의 실제 ZIP을 다운로드했다. outer **292,227,189 bytes / SHA256 `0965d00ba6a2f9cdc541a20700f23c2ebd090e8adc1108d9a652fe1558aa2482`**, GitHub metadata digest와 일치. 만료 예정 **2026-10-27**, 영속 공개 소스 제공 경로로 사용하지 않는다.
- 내부 source ZIP **71,000,823 bytes / SHA256 `c41e83c1c00c2c7e9327c93c820a06dca3db631813cb69b34d6600de7985e29f`**. Linux 동일 SHA 생성물과 전체 byte 일치. manifest 641개 payload(자기 manifest 포함 ZIP entry 642개)의 크기/hash/entry 집합을 실제 검사했다. 10개 archives·closure CSV/JSON·새 원문 고지와 application source가 포함됐고 source/public 승인 false다.
- 내부 runtime ZIP **232,034,356 bytes / SHA256 `8be9d64370ff0feec06b409131b1053d01c6dab6620ebd42e1d43df04e2a9bc0`**. build-info의 exact SHA·SDK/runtime·self-contained·publicReleaseApproved false, manifest 1,739개 payload(총 파일 1,740개), LICENSE와 원문 licenses **23개**의 size/hash/byte를 대조했다.
- 동결 native-inventory의 제3자 native/support **1,284개**는 hash/size가 모두 동일하다. 자체 App.exe만 162,816 bytes로 크기가 같고 새 SHA 버전 정보 때문에 hash가 다르다. 새 App.exe SHA256 `2dc9569058406710970382a41c0018003f264f07b1a6b48375baf9a1ced22a96`, UTF-16 버전 정보의 exact `e27c80d...`를 확인했다. 제품 src/tests/locks/SDK/엔진 변경은 없다. 1,285개 전부가 옛 artifact와 byte 동일하다고 기록하지 않는다.
- 초기 cross-platform 대조에서는 작성 문서 THIRD_PARTY_NOTICES의 LF/CRLF 차이와 재생성 App.exe를 차이로 검출했다. 내용의 LF/CRLF 정규화 일치·exact Git SHA 버전 정보를 확인해 위 구분으로 기록했다. 원문 23개/LICENSE의 검사는 완화하지 않았으며 CI에서는 실제 Windows checkout과 runtime 문서의 byte 일치도 통과했다. 문서 line ending/자체 apphost 차이를 대응 소스 완료나 native bundle 교체로 해석하지 않는다.
- archive 10개/기존 8개 입력의 byte/hash 보존, 공식 script Git blob 3개, closure 1,285개 path/hash coverage·그룹 합계, 기존 문서 heading/길이·diff/local 링크를 확인했다. 기존 성공/실패/실물 미검증은 삭제하지 않았다.

최종 판정은 **PR #33의 잔여 조사·검토 자료 및 영향 검증 제출 완료**다. S01~S03/N01~N03/G01의 공급자/링크 증거·전체 대응 소스·license graph·LGPL 교체·클린 PC 및 기존 실물 잔여는 미완료다. `completeCorrespondingSource/publicReleaseApproved:false`를 유지하고 MIT를 다시 승인 대기로 돌리지 않는다. 직접 병합·공개 Release/tag·제품/번들 변경·VSR은 없다.


## T18-D1 local-evidence — 확보 패키지 잔여 검사

기준 main **2a395f3**의 문서 정리/병렬 클린 PC 인계와 기존 실패/미검증을 보존했다. 미push 이전 로컬 작업은 자동 정리로 복구되지 않았으며 이번 검사는 이전 성공 승계가 아닌 같은 exact artifact/확보 archive에서의 자료 복원·검증이다. 공급자 웹 조사나 원본 archive 재수집은 하지 않았다.

입력 artifact **11789979837** outer 292,227,189 bytes/SHA256 `0965d00ba6a2f9cdc541a20700f23c2ebd090e8adc1108d9a652fe1558aa2482`, runtime 232,034,356 bytes/SHA256 `8be9d64370ff0feec06b409131b1053d01c6dab6620ebd42e1d43df04e2a9bc0`를 직접 대조했다. 1,739개 manifest payload와 제3자 native/support 1,284개 크기/hash가 동결 자료와 일치한다. apphost hash 차이는 PR #33의 exact SHA 구분을 유지한다.

Python ZIP reader로 JAR 6개의 모든 entry hash/manifest를 읽고 CAFEBABE·constant pool·this_class·SourceFile을 파싱해 entry/className과 package/SourceFile 후보를 대조했다. 총 class 2,364개가 Java 722개 단일 source 후보에 대응하며 class partition/build-only 제외도 검사했다. header/version/자기표시 compiler와 원문 build 규칙의 일관성은 실제 source equivalence/patch 적용이나 compiler 실행 attestation이 아니다. Lua 12-byte header·size_t/source chunk 및 source hash·기존 share/configure/recipe/32-bit patch 후보를 대조했다. 지원 파일은 source bytes와 LF→CRLF만 적용한 bytes를 비교했고 dependencies.json의 noticeFiles 존재·hash·sha512 파일/metadata를 별도로 대조하고 저장 원문 bytes를 확인했다. runtime 2개 hash 값은 일치, NuGet 12개는 불일치해 auxiliary 비교를 통과 처리하지 않았다. dependencies/lock과 원래 동결 metadata 값은 동일하며 실제 archive/hash 산출 의미 없이 원인을 단정하거나 값을 바꾸지 않는다. 고지 byte 통과와 provenance advisory를 구별한다. 실행/디컴파일/native 또는 Java/Lua 재빌드는 하지 않았다.

검사 결과는 기존 source-closure JSON/CSV와 README에 통합했고 고지 4개 원문을 보완했다. support author/license와 full dual-license 조건, actual source 변경·selected linked graph/Desktop/Skia scope는 외부 증거 대기로 분리했다. S01 401/404 조회/공급자 연락·제품/번들/SDK/엔진/무결성 로직 변경·직접 병합·공개 배포·VSR 없음. completeCorrespondingSource/publicReleaseApproved false와 기존 클린 PC/NAS/RaiDrive/AVI/잠금/수동 미검증을 유지한다. 로컬/정확한 SHA Windows CI·새 artifact 결과는 아래에 기록한다.


### local-evidence 제출 전 로컬 검증·원격 차단 이력

검증 자료 SHA **c8bfd72222054ef05d83e8131037b5ebc0e337c9**. 기존 무결성 회귀 **42개**가 일반 Python/`-O`/`PYTHONOPTIMIZE=1`에서 통과했다. 10개 기존 archive bytes/SHA256/제공 SHA512를 모두 검증했고 source sidecar를 두 번 생성해 전체 byte 일치했다. Source ZIP **74,037,284 bytes / SHA256 `f606597f74a33567b84a61d5755761400be715d44ebfd52592cf0a33fecbf552`**, manifest 645개 payload/ZIP entry 646개. 실제 ZIP은 세 Python 모드 verify 통과, 원문 고지 4개 byte 포함 및 source/public 승인 false 확인.

기존 source-closure JSON의 모든 이전 key/value, CSV 1,285개 path/architecture/hash/group/sourcePaths/remaining은 유지된다. main 대비 src/tests/scripts/workflows/SDK/MIT/source-inputs 및 source archives 변경 없음. 추가 source 헤더 판독은 Java 47종/헤더 없는 후보 2개, Lua 36개, 웹 지원 18개(16 GPL 설명·2 dual-license 설명)를 보존했다. 고지의 정확한 적용 잔여는 README의 local-evidence 표를 따른다. 이전 ac828d5의 sidecar hash/성공을 복구했다고 표시하지 않는다.

**제출 전 당시 상태는 PR 미제출·Windows CI 미실행**이었다. automatic approval review가 공개 저장소 작업 브랜치 push도 사용자의 공개 배포 금지에 포함된다고 해석해 거절했다. 다른 API나 우회 경로로 전송하지 않았다. 공개 저장소의 branch push/PR 제출은 허용하고 Release/tag 배포 금지는 유지한다는 범위 확인 후 exact SHA의 T18 CI·새 artifact runtime/source manifest 및 원문 고지 포함을 검증해 PR 결과를 갱신한다. 이전 PR #33 Windows 성공을 이번 자료의 성공으로 대체하지 않는다.

이 결과 커밋은 CURRENT_STATE/TASKS/이 검증 문서만 변경한다. 검증 자료/고지/패키징 입력은 위 SHA 그대로이며 결과 문서 SHA의 새 ZIP 검증을 주장하지 않는다. 전체 소스/고지 graph 미완료·공개 배포 보류와 클린 PC/NAS/RaiDrive/AVI/잠금 및 기존 실물 잔여를 유지한다.


### PR #34 제출 및 exact SHA Windows 검증

2026-10-13 사용자가 공개 저장소 branch push/PR 및 CI 검토 artifact를 명시 승인했다. Release/tag·최종 사용자 배포 금지는 유지했다. 자동 승인 차단은 재발하지 않았지만 Git CLI 인증이 없어 직접 push는 실패했다. 연결된 GitHub 기능으로 f69b20b의 변경 blob 11개 및 **전체 tree SHA 동일**을 확인해 cff8956으로 제출했다. 로컬 원본은 preserve/t18-d1-local-f69b20b에 보존했고 원격을 fetch해 full-tree diff 0을 확인했다. commit author/metadata 차이 때문에 SHA를 동일하다고 주장하지 않는다.

검증 입력 **cff89563d362005c4bf281cd8463f120077ed831**. [Windows T18 CI 38396037357](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/38396037357), job **115291371488**, attempt 1 최초 성공. Windows Server 2025 x64/.NET SDK 10.0.401/runtime 10.0.12에서 exact checkout/SDK, 기존 Python 무결성 42개, source/runtime ZIP repeat byte, 압축 해제 셸 시작·정상 종료·재시작 2회, 기존 전체 ZIP dependency harness/native 경로 회귀가 통과했다. 사전 설치 없는 사용자 Desktop PC 검증은 아니다.

[검토 artifact 11812502022](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/38396037357/artifacts/11812502022), 만료 **2026-10-27T12:20:36Z**를 실제 다운로드해 outer bytes/hash를 GitHub digest와 대조했다. 이전 artifact 11789979837과 구분하며 영속 source 제공 경로나 공개 Release가 아니다.

| 다운로드·직접 검증한 ZIP | bytes | SHA256 |
|---|---:|---|
| outer artifact | 292814279 | `80437d36904b0197efc1c59c9f7a2223ec4e79f3f2814b012e18bbc5f670e59b` |
| source-review-cff89563d362 | 74037284 | `167bdc3c0c501561ac78b50868498d2b04a451f430cd44d49c3598c28078d77c` |
| win-x64-cff89563d362 | 232050352 | `e260cecb72b67793a1c05202edea7df73b79c75572bddda05dba1f4992d59f70` |

Windows source ZIP은 Linux 동일 SHA의 두 번 생성물과 **전체 byte 동일**이다. 645개 manifest payload/646개 entry, commit/incomplete/source release blocked status 및 원문 source/고지 포함을 확인했다. Linux 실제 ZIP의 일반/-O/PYTHONOPTIMIZE 세 모드 verify도 통과했다.

runtime manifest **1743개 payload/전체 1744개 파일**의 entry 집합/중복·size/hash, build-info의 exact SHA/SDK/runtime/public 승인 false, MIT LICENSE와 원문 license **27개** byte 일치, 기존 dependency notice **36개 hash 보존을 확인했다. 새 고지 4개는 byte 그대로 포함됐고 기존 제3자 native/support **1284개** size/hash는 동결 inventory와 같다. 자체 apphost는 162816 bytes/SHA256 `a33644eff8ebf4c8517a5b09ea267d636f8623f395979b92d81438773d476a40`, UTF-16 exact commit version을 확인했다. apphost SHA 차이는 제품 변경/native bundle 교체가 아니다.

결과 커밋은 CURRENT_STATE/TASKS/이 검증 문서 3개만 변경한다. 자료·고지·패키징/검증 입력은 cff8956과 동일하고 결과 문서 SHA의 새 ZIP 검증을 주장하지 않는다. PR #34는 검토 대기이며 직접 병합/Release/tag/최종 사용자 배포·제품/번들 변경·공급자 연락 없음. S02/S03 내부 검사와 N02/N03 고지 보존 부분만 완료다. actual source/patch/selected graph·author/license/Skia/Desktop 범위, NuGet hash 표기 advisory·전체 대응 소스/라이선스 양립 및 기존 클린 PC/실물 잔여는 유지한다.


## PR #34 NuGet hash 보완 — 원인 재현·비교 기준 수정

main 2ae0113의 보완 지시를 기존 task/t18-d1-local-evidence에 통합했다. JAR/Lua/support/원문 고지 및 cff8956의 CI/artifact 이력을 보존한다. 원인/파일별 근거는 release-readiness README의 NuGet hash 보완 절 및 source-closure.json.localEvidence.nugetHashReconciliation을 따른다.

SDK 10.0.401 Linux 원본을 공식 release metadata SHA512로 확인한 뒤 실제 SDK NuGet.Packaging API를 사용했다. exact NuGet.org nupkg 14개를 독립 다운로드해 이전 artifact의 whole archive SHA512와 제공 원문 고지 byte가 모두 일치함을 확인했다. 별도 새 cache에 실제 PackageDownload restore를 수행했고 signature-aware content hash와 signed archive integrity/identity를 확인했다. 14개 모두 signed이고 12개 lock hash는 content hash와 같다. runtime 2개는 기존 추출이 raw hash를 metadata로 복사했기 때문에 비교 기준 오류였다. 원본은 수정하지 않고 새 패키징 metadata만 content/archive를 분리했다.

helper는 SDK assembly만 참조하며 앱/NuGet dependency를 추가하지 않는다. Linux Release helper 빌드 경고 0/오류 0, 정상 exact archive 14개 검증, 정상/정규화 2개와 invalid content/raw hash·identity·DLL payload 및 rehashed payload 5개 거부를 확인했다. 마지막은 NuGet SignatureException의 package integrity 실패다. 첫 diagnostic의 runtime content mismatch는 정상 원본이 변조됐다는 판정이 아니며 기존 raw field를 잘못 비교한 재현이다. 기존 42개 sidecar 회귀는 그대로 유지한다.

Package-Windows.ps1은 매번 cached nupkg를 실제 계산해 검증하고 contentHash/archiveSha512/signed를 별도 기록한다. runtime도 .nupkg.metadata로부터 독립 content 기준을 읽는다. 새 Windows CI는 기존 cache를 보존하고 별도 cache/HTTP cache 미사용 locked restore 후 14개 원본 hash를 비교하며 mismatch 회귀를 실행한다. package-hashes report는 CI artifact에만 포함하고 runtime payload에 SDK/helper 또는 nupkg를 넣지 않는다. 실제 bundle/SDK/버전/제품/원문 고지·10개 source archive·MIT와 source/public 승인 false는 보존했다.

exact 수정 SHA의 Windows CI·새 artifact 검증은 결과 확정 절에서 이전 결과와 구분한다. NuGet archive/content provenance 해소를 native selected graph/고지·license graph·클린 PC 완료로 확대하지 않는다. 실제 공급자 연락·직접 병합·공개 Release/tag·최종 사용자 배포·VSR 없음.


### NuGet 보완 exact SHA Windows 검증과 실제 artifact 대조

검증 입력 **e4500fcc1879dbe968d078aa76a00c4a2ba652f3**, tree **15c4b24b39ce80c44951ad7eb9d9b74aaabb5bf4**. main 2ae0113의 지시/기존 PR head 91865d6을 보존한 보완이다. 중단 전에 업로드된 변경 blob 10개를 같은 tree로 복구해 해당 작업 브랜치에 반영했으며 pruned 로컬 541b07b와 commit metadata/SHA를 혼동하지 않는다. 결과 확정 커밋은 CURRENT_STATE/TASKS/이 문서 3개만 변경하며 검증 입력 SHA와 구분한다.

[Windows T18 run 38489221298](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/38489221298), job **115610185694**, attempt 1 **success**. exact head checkout / SDK 10.0.401 / runtime 10.0.12 확인, 기존 sidecar 무결성 회귀 **42개**(일반/-O/PYTHONOPTIMIZE), source/runtime ZIP 반복 byte 일치, 셸 시작·정상 종료·재시작 2회와 기존 전체 ZIP dependency/native load 경로 회귀를 통과했다. NuGet 14개 실제 cached archive 검사 및 새 별도 cache/HTTP cache 미사용 locked restore 결과가 일치한다. 정상/표현 정규화 **2개**, 잘못된 content/raw hash·identity·DLL payload·raw hash 재계산 DLL payload **5개 거부**를 확인했다. 마지막은 NuGet signed archive integrity 실패로 거부됐으며 raw hash만 재계산해도 통과하지 않는다. T03 38489221278 / T05 38489221283 / T06 38489221432 / T07 38489221367 / T09 38489221415 / T10 38489221270 / T11 38489221311 / T18A-6 38489221250도 같은 SHA에서 최초 실행 success다.

[검토 artifact 11861271580](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/38489221298/artifacts/11861271580), 만료 **2026-10-28T00:22:44Z**를 실제 다운로드했다. outer bytes/SHA256는 GitHub artifact digest와 일치하며 이전 artifact 11812502022는 변경하지 않았다.

| 직접 검증한 ZIP | bytes | SHA256 |
|---|---:|---|
| outer artifact | 292844685 | `6a4b842cc6189bf0fc96d791f197f111603f72354325529b4b30d652e5f0f2fd` |
| source-review-e4500fcc1879 | 74110745 | `0c38bd6535bd84c7fd2327c2d17d6ead7dfe8d21203ff31ab066128bf1952232` |
| win-x64-e4500fcc1879 | 232051411 | `2ad513bc071b24861e2b2d3cfef645473c64b9fb3c370240ac046354d8178a83` |

Source ZIP 645 payload/646 entry의 size/hash·집합/중복·exact commit/incomplete/public false와 Linux 동일 SHA의 두 생성물 전체 byte 일치를 확인했다. helper 3개는 application.tar의 검토용 소스에 포함된다. Runtime 1743 payload/1744 files의 manifest 전체 size/hash·집합/중복·exact build-info, MIT LICENSE와 원문 license 27개, 이전 dependency notice 36개 및 동결 native/support 1,284개 bytes/hash 보존을 확인했다. 자체 apphost의 UTF-16 exact commit version은 바뀌며 native bundle 변경으로 분류하지 않는다. runtime ZIP에 SDK/helper/nupkg 또는 hash report를 추가하지 않았다.

artifact의 warm/cold report 14개는 package identity·archive bytes/SHA256/raw SHA512·contentHash·signed integrity 및 SDK NuGet informational version이 localEvidence.nugetHashReconciliation의 독립 원본 증거와 모두 같다. dependencies.json의 sha512/contentHash는 실제 NuGet content이고 archiveSha512는 제공 .nupkg.sha512의 raw 값이다. 동봉 lock의 모든 contentHash(타깃 그룹 합계 15개)도 대응 package content와 일치한다. 12개 기존 lock hash는 보존되고 runtime 2개 legacy raw 값만 content 기준으로 바로잡았다. 이 판정은 패키지 byte/identity/content provenance 범위이며 서명 인증서 신뢰 전체나 native selected graph/전체 라이선스 양립을 승인한 것은 아니다. 이전 packageHashComparison은 당시 관측 이력으로 보존되고 새 reconciliation 판정이 현재 의미를 설명한다.

판정: **NuGet 12개 차이 원인/정상 비교 기준 해소, runtime 2개 고지 metadata 추출 수정, 실제 불일치 거부·Windows CI/artifact 검증 완료, PR #34 재검토 대기**. JAR/Lua/support/원문 고지 검사 및 cff8956/이전 실패·검증 이력은 유지한다. 공급자 증거가 필요한 S01/S02/S03 actual source/patch/build·selected graph 및 N02/N03 author/license/적용 범위, 전체 대응 소스·license graph/LGPL 교체/클린 PC·기존 실물 잔여는 미완료다. completeCorrespondingSource/publicReleaseApproved false 유지, 공급자 연락·제품/번들/SDK/버전/MIT 변경·직접 병합·Release/tag·최종 사용자 배포·VSR 없음. PR 병합 전 후속 검토 기준은 task/t18-d1-local-evidence다.
