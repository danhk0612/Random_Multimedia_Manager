# T18 Windows 패키징 및 배포 검증

## 범위와 기준

main `7c16295` / PR #28 통합 `e850a64`, AI_WORKFLOW/TASKS T18/D08 및 T18A-6 인계를 기준으로 `task/t18-windows-packaging`에서 패키징·기존 통합 검사·사용자 안내만 준비한다. 제품 src/DB/API/감상/삭제/종료 정책과 기존 tests를 보존한다. 앱 자체 라이선스는 미확정이다. 공개 Release/tag·병합·VSR은 범위 밖이다.

## 재현 절차

Windows x64, PowerShell 7, SDK **10.0.401**, runtime **10.0.12**, NuGet.org 단일 source를 사용한다. 폴더형 self-contained publish이며 single-file/trim/ReadyToRun은 끈다. exe·managed DLL·Desktop runtime·SQLite·SkiaSharp·libVLC x64/plugins가 필요하다. SDK는 빌드용, 실행 프로세스는 압축 해제 폴더의 host/runtime를 사용한다.

```powershell
./scripts/Package-Windows.ps1
./scripts/Package-Windows.ps1 -OutputDirectory artifacts/t18-repeat
./scripts/Verify-WindowsPackage.ps1
```

`packages.lock.json`이 없는 최초 CI만 `-BootstrapLock`으로 정확한 resolve/content hash를 수집한다. 최종 제출에서는 잠금 파일을 저장하고 locked restore로 검증한다. 출력 폴더는 존재하지 않아야 하며 새 목적지를 지정한다. ZIP 이름에 commit SHA, `build-info.json`에 설정, `dependencies.json`에 전체 NuGet 버전/content SHA512와 고지 파일 경로, `files.json`에 전체 payload 경로/크기/SHA256를 기록한다. ZIP은 파일 정렬·고정 entry timestamp로 만들고 같은 SHA의 두 publish 결과 ZIP SHA256 일치를 실제 검사한다. 다른 OS/SDK 버전까지 byte 동일을 보장하지 않는다.

## 검증 설계와 판정 경계

- ZIP을 별도 한글/공백 폴더에 풀고 모든 파일의 hash를 대조한다. isolated LocalAppData에서 앱 exe를 직접 실행→정상 종료→재시작하며 실제 DB 생성과 bundled coreclr/SQLite load 경로를 확인한다. 사용자 DB는 사용하지 않는다.
- 별도 ZIP 복사본에 기존 테스트 harness entry assemblies/config/fixtures만 추가한다. 원래 패키지 파일 hash는 모두 유지되어야 한다. 기존 Core/Data/Comic/Scanner(10 시나리오)/Video/Subtitle/Viewing 전체 검사를 **ZIP dependencies**로 실행한다. 테스트 앱은 exe 직접 실행, PATH에서 SDK/VLC 경로 제외·DOTNET_ROOT를 빈 위치로 둔다. coreclr/SQLite/Skia/libVLC/core 실제 모듈 load 경로가 해당 ZIP 복사본 아래인지 확인한다.
- 만화/영상/자막·랜덤→감상→정리·실패 보존·DB 재열기·삭제/저널·숨김/정상 종료는 기존 검사 내용을 유지한다. 제품 exe의 실제 사용자 클릭/물리 키/청취 전체 수동 시나리오와 동일하다고 표시하지 않는다. 테스트 harness에서 쓰는 fixture는 운영 데이터가 아닌 임시 복사본이다.
- CI에는 SDK/런타임이 설치되어 있다. packaged host/runtime 실제 경로와 직접 실행은 별도 설치에 의존하지 않는 증거지만 **사전 설치가 전혀 없는 깨끗한 사용자 Windows PC 검사**를 대체하지 않는다. 실제 Windows 10/11 사용자 PC, NAS/RaiDrive/Explorer/로그오프/원격 휴지통/단절/캐시는 미실시다.
- 테스트 timeout 이후 kill은 폐기 가능한 CI fixture process 정리 전용이며 제품 정상 종료 시간 상한/강제 종료 정책을 추가하지 않는다.

## 결과

Windows CI 검증 SHA·실행 URL·ZIP 해시·파일 수·native 버전/고지는 실행 후 아래에 기록한다. 아직 실행 결과는 대기이며 성공으로 간주하지 않는다. artifact는 `t18-windows-review-<SHA>`(14일 보존)로 ZIP/ZIP hash·manifest·dependency metadata·locks·검증 로그를 남긴다. 실패 실행의 evidence도 보존한다.

## 지원 및 공개 배포 판단

.NET 10 공식 OS 표(2026-10-11 접근 확인)는 Windows 11의 현재 지원 에디션과 Windows 10 일부 Enterprise/LTSC·IoT만 열거한다. 앱 사용자 대상은 Windows x64 Desktop이고 Server CI 성공을 Windows 10/11 전체 보장으로 해석하지 않는다. 현재는 **CI 검토 패키지 준비**이며 공개 배포 준비 완료를 선언하지 않는다.

공개 배포 전 남은 항목: 앱 자체 라이선스 선택, exact native 번들/플러그인 고지·corresponding source 제공 방식 확정, 깨끗한 사용자 PC 압축 해제 실행 및 실제 기본 흐름 확인. dependencies metadata/추출 license 원문과 이 표의 조건을 대조해야 하며 metadata만으로 재배포 완료 선언을 하지 않는다. [의존성 고지](../packaging/THIRD_PARTY_NOTICES.md), [사용자 안내](../packaging/USER_GUIDE.md)가 패키지에 포함된다.

원격 휴지통 Unknown/실행 차단·임의 영구삭제 전환 없음·actual IO drain/정상 종료 시간 상한 없음·AVI 조사 보류 및 기존 수동 미검증/사용자 승인 생략은 그대로다. 실제 NAS/RaiDrive는 [T18A-6 N01~N12 실사용 점검표](T18A_6_INTEGRATION_VALIDATION.md)로 이관하고 반복 조사하지 않는다. T11 최초 native dialog 실패/수정 없는 재실행 통과 이력도 보존한다.
