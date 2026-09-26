# T15 트레이·일반 정상 종료

## 구현

`AppLifecycle`이 메인 X·트레이 복원·트레이/메인 종료의 단일 진입점이다. 시작 시 메인을 표시하고 일반 최소화는 작업표시줄에 유지한다. 메인 X는 닫기 이벤트가 끝난 뒤 메인 창을 숨긴다. 감상 창 X는 기존 방문의 정상 Leave다. 트레이 생성/재등록 확인 실패 시 숨기지 않으며 메인 종료 버튼을 사용할 수 있다.

`TrayIcon`은 WPF 메시지 창과 Windows `Shell_NotifyIconW`를 사용한다. 메뉴는 열기/복원, 비활성 ‘모두 숨기기/복원 (미구현)’, 종료다. 더블클릭도 같은 복원 경로다. Explorer 재시작의 TaskbarCreated에서 재등록 실패 시 메인을 복원한다. 새로운 패키지·설정·DB 스키마는 없다.

일반 종료는 신규 명령과 checkpoint 차단 → 준비 취소 → 스캔/감상/모달 확인/삭제 명령 Task 대기 → 삭제 복구 경계 확인 → 기존 Leave 저장 → 소유 미디어·보조 창 해제 → DB → 트레이 → WPF 종료 순서다. 중복 요청은 같은 종료 Task를 반환한다. T15는 종료 시작 시 모든 창을 숨기거나 음소거하지 않는다. T16 전역 키/Quick Hide는 구현하지 않았다.

- `ViewingWindow.RequestCloseAsync`는 UI command와 SessionCoordinator의 실행 Task를 모두 기다린다. 모달 삭제 확인도 명령 Task를 먼저 공개하므로 트레이 종료가 우회하지 않는다. 확인 중 종료를 요청하면 확인창을 닫은 뒤 종료하며 새 OS 삭제는 시작하지 않는다.
- 현재 Pending의 격리 및 SaveFailed/CommitUnknown은 기존 계약대로 종료를 막고 FrozenCommit/방문을 보존한다. 종료 차단 시 창을 복원하고 기존 저장 재시도/감상 복귀·삭제 복구 UI를 활성화한다. 복구 후 다시 종료할 수 있다.
- Deleting phase뿐 아니라 명령의 완료와 `DeletionService.Pending`/`HasIncompleteSuccess`를 확인한다. 성공 결과의 durable 저장 자체가 실패해 Prepared로 남은 경우도 현재 프로세스의 미완료 성공 표식으로 종료를 차단한다. 저널 형식과 OS 삭제·DB 정리 의미는 바꾸지 않았다.
- 현재 방문과 무관한 durable Prepared/Unknown은 저널과 격리를 유지하고 종료 가능하다. 전역 손상 격리 또는 Succeeded의 DB/저널 정리 실패는 복구 전 종료를 차단한다.
- Completed 결과의 Error를 성공으로 취급하지 않는다. 미디어 해제 예외는 조정자에 소유권/오류를 유지해 다음 종료가 NoOp으로 우회하지 못한다. 부분 해제된 네이티브 리소스를 임의로 다시 해제하지 않으므로 실제 해제 오류가 지속되면 앱은 ExitBlocked로 남는다. 자동 강제 종료는 없다.
- 단독 만화 창도 준비·페이지 작업 완료 후 해제한다. 영상 검증 창의 기존 비동기 Shutdown을 기다린다. 감상 어댑터의 해제 Task는 성공/실패 결과 모두 재사용한다.

## 자동 검증

Windows x64/.NET 10에서 기존 T03/T05/T06/T11 및 만화·영상·자막 워크플로를 사용한다.

- Data.Tests/T15Verification: Opening 전 종료, 취소 후 늦은 Ready의 1회 해제, 이미 저장된 방문의 해제 오류 보존·중복 종료, 성공 결과 저널 쓰기 실패와 복구.
- Viewing.Tests/T15NativeVerification: 실제 WPF 시작/최소화/X/복원, 트레이 불가 시 표시 유지, 중복 종료, 실제 만화 Active 종료·파일 잠금 해제, 감상 X, 저장 실패/CommitUnknown·기존 복구 버튼 접근·재시도 후 원래 닫기 완료, 삭제 확인 중 종료, Opening 종료, Deleting 성공/실패/취소 대기, 현재 Unknown, Succeeded 정리 실패·재시도, 무관한 Unknown 저널 보존, 스캔 중 종료.
- 실제 Shell_NotifyIcon 등록/갱신, 메뉴 이벤트·네이티브 더블클릭 메시지 연결과 해제는 별도 결과를 출력한다. 자동 실행 환경에 알림 영역이 없으면 미검증으로 표시한다. 실제 사용자 트레이 클릭 검증과 같지 않다.
- T03 셸 검사는 X 대신 UI Automation으로 명시적 종료 버튼을 눌러 앱 프로세스 종료·재시작을 확인한다. 제품에는 강제 Kill/timeout을 추가하지 않았다.
- 모든 DB·미디어·저널은 임시 생성물/테스트 fixture이며 사용자 파일을 삭제하지 않는다.

실행 결과와 코드 SHA는 CURRENT_STATE.md의 T15 절을 따른다. 아직 실행하지 않았거나 실패한 항목은 통과로 표시하지 않는다.

## 사용자 Windows 확인 — 대기, 미검증

과거 T09/T10/T11의 생략 승인 및 T12 실사용 이관을 T15에 확대하지 않는다.

1. T15 PR #17이 통합된 최신 `main`에서 Release 실행. 시작 시 메인과 알림 영역의 아이콘 확인.
2. 최소화는 작업표시줄에 유지. 메인 X는 숨김. 트레이 더블클릭/열기·복원으로 같은 창이 복원되는지 확인.
3. 트레이의 모두 숨기기/복원 메뉴가 미구현으로 비활성인지 확인.
4. 만화/영상 감상 중 감상 X는 세션만 종료하고 메인을 유지하는지 확인.
5. 감상 중 트레이 종료 및 스캔 중 종료 후 프로세스/트레이 잔류가 없고, 재시작 시 진행과 기록이 저장되었는지 확인.
6. 열려 있는 삭제 확인창에서 트레이 종료 후 확인창을 취소하면 테스트 파일을 삭제하지 않고 종료하는지 확인.
7. 전체화면/보조 창이 있는 상태의 복원과 정상 종료, Explorer 재시작 후 트레이 복원 경로를 확인.

DB/삭제 저널/해제 실패는 자동 주입 검사를 근거로 하며 사용자 DB 권한을 바꿔 재현하지 않는다. 미디어 엔진의 실제 예외 이후 완전한 복구는 보장하지 않으며 종료 차단과 데이터 보존만 확인한다.

공식 API 확인(2026-09-21): [Shell_NotifyIconW](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shell_notifyiconw), [NOTIFYICONDATAW](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/ns-shellapi-notifyicondataw).
