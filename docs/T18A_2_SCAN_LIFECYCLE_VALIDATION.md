# T18A-2 스캔·생명주기 검증

최초 기준 main `7b90722` (T18A-1 PR #23 통합), 병합 전 보완 기준 main `5e82e9d`, 작업 브랜치 `task/t18a-2-scan-lifecycle`, PR #24. 정책은 DATA_AND_RANDOM_POLICY의 T18A와 D10~D12/D14~D16을 따른다. 이 문서는 오류 주입, Windows 로컬 실파일, 실제 NAS/RaiDrive를 구분한다.

## 구현 경계

- `MainWindow.Scans`가 앱의 단일 `ScanCoordinator`를 소유한다. 시작 복구 후 첫 화면을 표시한 다음 `Start`한다. 생성/목록 조회는 DB만 읽으며 원격 열거를 기다리지 않는다.
- `WindowsStorageProbe`는 worker에서 WNetGetConnection, QueryDosDevice, 드라이브 종류와 볼륨 근거를 수집한다. 매핑 조회 실패나 Fixed 하나만으로 Local로 분류하지 않는다. UNC는 공유 루트 문법 근거만 제공한다. 알 수 없는 장치/provider에는 원격 보수 정책을 적용한다.
- `LibraryScanner.CollectAsync`는 한 SourceId 범위의 메타데이터만 관찰한다. 스캔 전후 binding 근거, DB revision, admission generation을 확인한 뒤 기존 ApplyObservedItems로 반영한다. 원격은 authoritative 부재를 증명하는 provider API가 없으므로 Missing을 자동 확정하지 않는다. 성공 Present는 동일 바인딩일 때만 반영하고 부분 실패는 완료 시각을 갱신하지 않는다.
- Local Missing은 지원 조상·준비 상태·개별 경로 부재를 worker에서 재확인한다. UNC 조상 검사는 공유 루트에서 멈춘다. reparse/명시적 case-sensitive 경로와 대소문자 충돌은 보존한다. 미디어 본문/ZIP 목록/영상 probe/자막을 색인에서 읽지 않는다.
- SourceId별 dirty version과 bounded pending 상태를 사용한다. Local Events는 watcher를 기준 스캔 전에 설치하고 2초 debounce/최대 30초, overflow는 소스 재관찰이다. 이벤트가 실행 중 발생하면 다음 관찰에 남긴다. 수동 우선·소스 순환, 소스별 성공 시각 기반 주기, 실패 30초→2분→10분 backoff, 권한/바인딩 문제는 명시적 조치 대기다. 취소한 수동 소스는 즉시 자동 재시작하지 않는다.
- `EnterExclusiveAsync`는 먼저 신규 스캔 admission을 닫고 취소를 요청한 뒤 실제 worker 완료를 기다린다. 감상 창 생성 전부터 정상 Leave/해제 후 ShowDialog 반환까지 lease를 보유한다. Empty/Opening/Active/checkpoint/SaveFailed/Hidden도 예외가 없다. 소스 저장과 삭제 복구도 이 경계를 사용한다. 기존 durable 삭제 격리는 관련 분류 전체를 보류하며 v2 저널의 기존 전역 차단을 유지한다.
- Closing에서 타이머/watcher와 신규 admission을 차단한다. 실제 Task를 끝까지 기다리며 dirty 큐를 소진하지 않는다. 감상·삭제 복구·목록 읽기·보조 창 해제의 기존 대기 뒤에 DB를 해제한다. Restore/SetExitRequested(false)만으로 스캔을 재개하지 않는다. ExitBlocked에서는 기존 저장 재시도·삭제 복구·명시적 복원·종료 재시도를 유지한다. 새 감상/스캔/편집/새 삭제는 열지 않는다. 강제 종료·timeout 성공 처리는 없다.

## 자동 검증과 실행

Windows x64에서 실행한다. Linux에서 통과한 것으로 표시하지 않는다.

```powershell
dotnet build RandomMultimediaManager.sln -c Release
dotnet run --project tests/RandomMultimediaManager.Scanner.Tests -c Release -- lifecycle
dotnet run --project tests/RandomMultimediaManager.Viewing.Tests -c Release
```

| 계약 | 검사 | 구분 |
|---|---|---|
| N03/N04 | 다른 공유로 재매핑, 오래된 revision 결과 폐기, Unknown 시작/재연결 재확인 | 연결 근거 오류 주입 + 실제 SQLite |
| N04 | 같은 분류의 원격 수동/예약 분리, 시작 끔, catch-up 합침, backoff, 수동 우선·취소, 정책 끄기 | 가상 시계/열거 대역 |
| N04 | 실제 장치·볼륨 확인, watcher 생성/이름변경 수렴, dirty 폭주 합침 | Windows 로컬 테스트 폴더 실파일 |
| N05 | 빈 캐시 목록/원격 Missing 입력 무시, 부분 Present/완료 시각 보존, 권한 실패 재시도 억제 | 오류 주입 |
| N09/N11 | 취소 무시 IO가 반환할 때까지 admission 유지, 중복 종료 Task 공유, Closing 신규 호출 0, DB 해제 뒤 재개 0 | 지연 worker + 실제 WPF/AppLifecycle/DB |
| N10 | 전체 감상 lease, Empty/Active/checkpoint/Hidden/SaveFailed와 정상 닫기 뒤 해제 | 조정자 오류 주입 및 실제 WPF/만화/저장 실패 trigger |
| 영향 회귀 | T03 저장/migration/저널, T05 스캔, T06 세션, T11 감상/T12~17, 만화·영상·자막 | 기존 Windows workflow |

### 실행 결과

검증 코드 `093a3fc1082c726b5114a09cfb38f0e0881f30c9`, 2026-10-06 Windows x64 러너(OS 10.0.26100), .NET SDK 10.0.401. Release 빌드 경고 0·오류 0. 2026-10-07 최종 결과를 확인했다. 이후 완료 문서 반영은 제품/테스트 코드를 변경하지 않는다.

| Windows workflow | 결과 |
|---|---|
| [T03 저장/셸](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/37394119744) | 통과 |
| [T05 스캔/N04~N05/N09~N11 조정자](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/37394119813) | 통과 |
| [T06 세션/스캔 회귀](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/37394119906) | 통과 |
| [T09 영상](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/37394120169) | 통과 |
| [T11 감상/T12~17·만화·영상·자막 회귀](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/37394119964) | 통과 |

실제 WPF 검사에서 Empty/Active/checkpoint/Hidden/SaveFailed 동안 스캔 차단, 정상 Leave 후 허용, 지연 worker 동안 DB 유지, 중복 종료 공유, Closing/Restore 후 신규 스캔 0, 실제 IO 완료 후 DB 해제 및 해제 후 재개 차단을 확인했다. 연결 실패/재매핑/원격 캐시는 대역 오류 주입이며 실제 NAS/RaiDrive 결과가 아니다. 이전 실패는 임시 창 닫기와 앱 Closing 차단의 분리 및 새 UNC fixture의 공통 정규화 적용으로 수정한 뒤 재검증했다. 러너의 native 영상 장치/thumbnail 진단 및 기존 임시 fixture 정리 메시지는 사용자 화면/청취·실장비 호환성 검증을 대신하지 않는다.

## PR #24 병합 전 보완과 재현 검사

main `5e82e9d`의 검토 지시와 기존 구현/검증 내용을 함께 보존했다. 최신 main은 작업 브랜치에만 통합하며 PR #24를 main에 병합하지 않는다.

- App의 제어 가능한 직접 종료 요청은 `RequestShutdownAsync`로 연결한다. Scans 시작 뒤 SessionEnding은 Cancel을 동기 설정하고 기존 AppLifecycle의 비동기 종료를 시작한다. OnExit async void/Dispatcher 동기 대기는 없다. 감상 저장·삭제 복구·ExitBlocked·복원 조건은 기존 계약을 따른다.
- 같은 source/설정 generation의 수동 요청은 automatic probe부터 현재 worker에 합류한다. discovery-only에도 필요한 collection을 한 번 연결하며, 수집하지 않는 discovery의 종료 결정 후 도착한 새 요청은 별도 대기한다. 취소된 probe/worker와 다른 source는 공유하지 않는다.
- active 요청과 다음 요청의 완료/Progress 소유권을 구분한다. 완료/취소/제거/Closing 때 해당 콜백만 해제하고 새 요청은 보존한다. 이전 collector의 늦은 Report는 worker token으로 차단하며 이미 UI에 게시된 Progress<T>는 UI 요청 소유권을 전달 시 확인한다.

수정 전 제품 코드를 그대로 둔 재현 헤드 `b513565`의 [T05 37666349091](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/37666349091)에서 automatic probe 중 수동 요청이 collection을 2회 실행하고 완료된 Progress가 후속 자동/늦은 Report에 전달됨을 확인했다. discovery-only는 이미 1회였으며 수정 후에도 유지한다. 최초 WPF 재현 fixture의 비공개 이벤트 인자 생성 방식은 테스트 컴파일 오류로 수정했으며 제품 문제 재현으로 계산하지 않는다. SessionEnding 재현은 fixture 수정 헤드 `785aed3`의 [T11 37666726205](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/37666726205) 재실행에서 Cancel 미설정 검사 실패로 확인했다. 첫 실행은 기존 T16 native dialog 복원 검사에서 먼저 실패했으므로 해당 종료 재현 근거로 쓰지 않으며 제품 수정 없이 같은 job을 재실행했다.

| 보완 검사 | 구분 | 결과 |
|---|---|---|
| Logoff/Shutdown 모의 SessionEnding과 앱 내부 종료 요청, 중복 종료 Task 공유 | 실제 WPF App override + 지연/취소 무시 worker 오류 주입 | 통과 |
| 종료 중 DB 생존·신규 IO 0·실제 worker 완료 후 DB 해제 | 실제 SQLite/WPF + 오류 주입 | 통과 |
| automatic probe 수동 합류·discovery 수집 1회 | probe 대기 오류 주입 | 통과 |
| 취소된 probe 뒤 새 요청·다른 SourceId 결과 분리 | 오류 주입 | 통과 |
| 완료 후 자동 검사/늦은 Report·취소 후 새 요청 콜백 보존 | collector 오류 주입 | 통과 |
| 완료/취소 후 이미 게시된 Progress<T>의 늦은 UI 전달 | 전달 순서를 제어한 SynchronizationContext 대역 + 실제 ViewModel | 통과 |

검증 코드 `d563e0ae6e1c630de62b1e79d138b6a642664627`. Windows Server 2025 x64(OS 10.0.26100), .NET SDK 10.0.401. 2026-10-08 확인: Release 빌드 경고 0·오류 0, 영향 CI 5개 모두 성공. 이후 main 통합 커밋 `3f5cff6`은 `d563e0a`와 tree가 동일하다. 기존 자동 검증 근거는 위 표에 보존한다.

| 보완 Windows workflow | 결과 |
|---|---|
| [T03 저장/셸 37666842460](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/37666842460) | 통과 |
| [T05 스캔/새 재현·회귀 37666842380](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/37666842380) | 통과 |
| [T06 세션 37666842424](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/37666842424) | 통과 |
| [T09 영상 37666842476](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/37666842476) | 통과 |
| [T11 감상/모의 SessionEnding·만화/영상/자막 37666842437](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/37666842437) | 통과 |

SessionEnding 이벤트 인자의 비공개 생성자는 재현 fixture에서만 reflection으로 호출한다. [WPF 공식 소스](https://source.dot.net/presentationframework/System/Windows/SessionEndingCancelEventArgs.cs.html)와 [SessionEnding 공식 문서](https://learn.microsoft.com/en-us/dotnet/api/system.windows.application.sessionending?view=windowsdesktop-10.0)는 2026-10-08 접근 확인했다. 모의 호출은 실제 WM_QUERYENDSESSION/사용자 로그오프를 검증하지 않는다. Cancel=true는 OS 요청을 거부할 수 있으므로 앱 종료 뒤 사용자가 OS 종료/로그오프를 다시 요청해야 할 수 있다. 강제 OS 종료·전원 차단·프로세스 제거까지 정상 저장/실제 IO drain을 보장하지 않는다.

## 추가 검토 두 항목 보완 (2026-10-08)

main `e06cbea`의 추가 지시를 기존 구현/이전 세 보완 문서와 함께 통합했다. 수정 전 제품 코드에 재현 테스트만 추가한 `564e171`의 [T05 37709695891](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/37709695891)에서 다음 세 검사가 실패했다: 대기 B 취소가 실행 A를 취소, 공유 대기자 하나의 취소가 worker/다른 대기자에 전파, 삭제된 로컬 소스의 watcher 생성 실패 후 3초만에 재관찰. 기존 basic/state/cancel/access/reparse/case 검사는 통과했다.

- 요청별 완료/취소/Progress를 분리한다. 소스별 worker는 공유하지만 취소 토큰은 자신의 대기자 또는 분류 요청 그룹만 취소한다. 자동 작업/다른 대기자가 있으면 실제 worker를 중단하지 않는다. 마지막 수동 소유자가 취소한 worker는 실제 IO 완료까지 소유권을 유지한다. 기존 Closing/배타 진입은 여전히 전체 worker 취소와 실제 완료 대기다.
- watcher 실패 횟수/재시도 시각을 메타데이터 스캔 성공과 분리한다. 승인된 30초→2분→10분 backoff를 재사용하고 권한 실패는 명시적 조치 대기다. 실제 watcher attach 성공으로 복구하며 수동 요청은 재시도 시각 전에 가능하다.
- 검사 구분: 대기 A/B·동일 소스 공유·분류 그룹·자동 소유자·진행 콜백·Closing은 지연 worker 오류 주입이다. 로컬 폴더 제거/재생성·watcher 재연결/파일 관찰은 Windows 실파일과 실제 연결 근거를 사용하고 시계만 제어한다. 30/120/600초·권한 실패·수동 복구는 watcher factory 오류 주입이다. 실제 NAS/RaiDrive 및 사용자 로그오프 검증은 아니다.

검증 코드 `ac2a4d6e8a2c0ead0fa98d4a56e98440540a51ed`. Windows Server 2025 x64(OS 10.0.26100), .NET SDK 10.0.401, Release 빌드 경고 0·오류 0. 새 검사 7개와 이전 probe/진행/전체 조정자 검사가 통과했다. T11 최초 실행은 기존 T16 native file picker 복원 검사(`native dialog restored False`)에서 실패했다. 직전 `fbfd836`은 같은 T11 37709893937을 통과했고 이전 검증에도 같은 실패가 기록되어 있어 제품/테스트 변경 없이 실패 job만 재실행했다. 1회 재실행 job `113094504890`에서 해당 복원 검사와 감상·스캔·만화·영상·자막 전체가 통과했다. 최초 실패는 삭제하거나 통과로 덮어쓰지 않는다.

| 추가 보완 Windows workflow | 결과 |
|---|---|
| [T03 저장/셸 37710101629](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/37710101629) | 통과 |
| [T05 스캔/추가 7개 회귀 37710101585](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/37710101585) | 통과 |
| [T06 세션 37710101596](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/37710101596) | 통과 |
| [T09 영상 37710101605](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/37710101605) | 통과 |
| [T11 감상/모의 SessionEnding·만화/영상/자막 37710101599](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/37710101599) | 1회 재실행 통과 (최초 T16 복원 실패) |

## 실제 환경 미검증

- 실제 Windows 로그오프/시스템 종료/강제 종료는 미실시이며 위 결과는 모의 SessionEnding이다.
- NAS/SMB 서버와 실제 매핑 해제·재연결·다른 로그온 세션, 인증/권한 변경은 장비 미제공으로 미검증.
- RaiDrive backend/version별 캐시·메타데이터 전송·seek·재연결은 미검증. 오류 주입 성공을 provider 호환성 성공으로 바꾸지 않는다.
- 사용자 실제 Windows 조작, Explorer UI/UNC 등록 UI, 원격 휴지통/v2 실행·복구, 감상 엔진 지연 IO는 각각 후속 T18A-3~6 범위다. 기존 수동 미검증·승인 생략·AVI 무음 조사 보류를 유지한다.
- T18A-2는 원격/Unknown 최초 확인과 정책 저장 API만 제공한다. 확인/UNC/설정 UI는 T18A-5까지 미연결이다. 미확인 원격 소스는 기존 DB 목록을 보존하고 스캔을 거부한다. 후보 정책/감상 바인딩 검사는 T18A-4에 남긴다.
