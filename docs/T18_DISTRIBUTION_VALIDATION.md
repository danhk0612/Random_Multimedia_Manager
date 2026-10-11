# T18 Windows 패키징 및 배포 검증

## 범위와 기준

main `7c16295` / PR #28 통합 `e850a64`, AI_WORKFLOW/TASKS T18/D08 및 T18A-6 인계를 기준으로 `task/t18-windows-packaging`에서 패키징·기존 통합 검사·사용자 안내만 준비한다. 제품 소스 코드/DB/API/감상/삭제/종료 정책을 보존한다. src 변경은 App/Core packages.lock.json 추가뿐이다. 기존 검증 의미는 유지하고 T18에서 T13 상태 대기와 T17 후보 ID 확인만 보강한다. 앱 자체 라이선스는 미확정이다. 공개 Release/tag·병합·VSR은 범위 밖이다.

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

최종 패키징/검증 코드 SHA `2599f48d96d5ca18ab389b719926424894a999b8`의 Windows 자동 검증은 최초 실행 성공했다. 아래 정확한 결과를 기준으로 하며 깨끗한 사용자 PC 및 원격 실물을 성공으로 추정하지 않는다. artifact는 `t18-windows-review-<SHA>-attempt-<n>`(14일 보존)로 ZIP/ZIP hash·manifest·dependency metadata·locks·검증 로그를 남긴다. 실패 실행의 evidence도 보존한다.

## 지원 및 공개 배포 판단

.NET 10 공식 OS 표(2026-10-11 접근 확인)는 Windows 11의 현재 지원 에디션과 Windows 10 일부 Enterprise/LTSC·IoT만 열거한다. 앱 사용자 대상은 Windows x64 Desktop이고 Server CI 성공을 Windows 10/11 전체 보장으로 해석하지 않는다. 현재는 **CI 검토 패키지 준비**이며 공개 배포 준비 완료를 선언하지 않는다.

공개 배포 전 남은 항목: 앱 자체 라이선스 선택, exact native 번들/플러그인 고지·corresponding source 제공 방식 확정, 깨끗한 사용자 PC 압축 해제 실행 및 실제 기본 흐름 확인. dependencies metadata/추출 license 원문과 이 표의 조건을 대조해야 하며 metadata만으로 재배포 완료 선언을 하지 않는다. [의존성 고지](../packaging/THIRD_PARTY_NOTICES.md), [사용자 안내](../packaging/USER_GUIDE.md)가 패키지에 포함된다.

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

## 최종 정확한 SHA 결과 — PR #29

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

native/runtime 라이선스 원문·metadata는 ZIP의 dependency-notices와 licenses에 포함된다. runtime pack LICENSE/THIRD-PARTY-NOTICES 원문과 SHA512도 수집했다. 전체 경로/크기/hash는 files.json, 정확한 NuGet graph/content hash는 dependencies.json 및 App/Core locks가 단일 증거다. 최종 제출 후속 변경은 결과·상태 문서뿐이며 검증된 제품 코드/테스트/패키징·CI/패키지 입력을 보존한다.

판정: **CI 검토용 패키지와 안내·PR 제출 준비 완료, T18의 공개 배포 준비는 보류**. 앱 라이선스/정확한 native 소스 제공·플러그인 조건/깨끗한 사용자 PC가 남아 있다. 실제 NAS/RaiDrive 및 기존 수동 미검증/승인 생략·AVI 보류·원격 휴지통 Unknown 차단·실제 IO drain/종료 상한 없음은 유지한다. 직접 병합·공개 Release/tag·VSR 착수는 없다.
