# T18A-2 스캔·생명주기 검증

기준 main `7b90722` (T18A-1 PR #23 통합), 작업 브랜치 `task/t18a-2-scan-lifecycle`, PR #24. 정책은 DATA_AND_RANDOM_POLICY의 T18A와 D10~D12/D14~D16을 따른다. 이 문서는 오류 주입, Windows 로컬 실파일, 실제 NAS/RaiDrive를 구분한다.

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

최종 실행 결과는 검증 완료 후 아래에 기록한다.

## 실제 환경 미검증

- NAS/SMB 서버와 실제 매핑 해제·재연결·다른 로그온 세션, 인증/권한 변경은 장비 미제공으로 미검증.
- RaiDrive backend/version별 캐시·메타데이터 전송·seek·재연결은 미검증. 오류 주입 성공을 provider 호환성 성공으로 바꾸지 않는다.
- 사용자 실제 Windows 조작, Explorer UI/UNC 등록 UI, 원격 휴지통/v2 실행·복구, 감상 엔진 지연 IO는 각각 후속 T18A-3~6 범위다. 기존 수동 미검증·승인 생략·AVI 무음 조사 보류를 유지한다.
- T18A-2는 원격/Unknown 최초 확인과 정책 저장 API만 제공한다. 확인/UNC/설정 UI는 T18A-5까지 미연결이다. 미확인 원격 소스는 기존 DB 목록을 보존하고 스캔을 거부한다. 후보 정책/감상 바인딩 검사는 T18A-4에 남긴다.
