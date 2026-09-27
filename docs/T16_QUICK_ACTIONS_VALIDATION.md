# T16 모두 숨김·음소거·빠른 정상 종료

## 구현과 경계

- 시작 기준은 T15 PR #17이 통합된 main `5b611c1`. 작업 브랜치는 `task/t16-quick-hide-exit`, PR #18이다.
- `Ctrl+Shift+H`는 모두 숨김/복원, `Ctrl+Shift+Q`는 빠른 정상 종료다. 각각 전역 등록하며 `MOD_NOREPEAT`를 사용한다. 등록 충돌은 해당 키만 비활성화하고 메인 상태에 안내한다. 트레이 메뉴는 계속 사용할 수 있다.
- `PrivacyWindows`는 UI 스레드의 앱 HWND만 관리한다. WPF `Hide()`로 `ShowDialog()`를 끝내지 않고 네이티브 표시를 숨겨 원래 대화상자와 명령 Task를 보존한다. 늦은 WPF/네이티브 창에도 표시 제어를 적용한다. 트레이 팝업은 복원 경로로 예외 처리한다.
- 숨기기 전 표시된 창과 숨김 중 표시를 요청한 창만 복원한다. 창 위치·최소화·전체화면과 모달 소유자의 비활성 상태를 변경하거나 영속 저장하지 않는다. 화면 합성 가림은 표시 전 적용하고 명시적 복원 시 해제한다.
- `PreparedVideo`의 기존 앱 mute 의도와 숨김용 mute를 분리한다. 활성/늦은 Ready/삭제 실패 후 재열기 모두 같은 출력 제한을 거친다. 숨김 자체는 pause, SuppressHistory, 감상 확정, 강제 checkpoint가 아니다. Windows 시스템·외부 프로그램 음소거 API는 호출하지 않는다.
- `AppLifecycle.ExitAsync`는 즉시 숨김+음소거 후 T15의 단일 종료 Task를 재사용한다. 준비 취소와 기존 UI/세션/삭제/스캔·DB 경계 대기, Leave 저장, 리소스 해제 순서를 보존한다. SaveFailed/CommitUnknown/현재 Unknown/미완료 성공/해제 오류를 성공 또는 NoOp으로 바꾸지 않는다.
- 종료 실패 시 자동으로 창을 노출하지 않는다. 사용자가 키/트레이로 명시적으로 복원한 뒤 기존 저장·삭제 복구 UI를 사용한다. Closing 중에도 열린 모달을 취소할 수 있도록 명시적 복원을 허용하되 신규 명령 차단은 유지한다. 복원은 삭제 동의나 종료 취소가 아니다.
- Explorer 재등록 실패 시 비공개 창을 자동 복원하지 않는다. 정상 해제에서 키·트레이·창 후크를 정리한다. 강제 Kill/timeout/Pending 폐기는 없다.

## Windows 자동 검증

검증 코드 `b74babc43fbdca5736b3df6ff24914db1f5655ff`, Windows x64 10.0.26100 / .NET SDK 10.0.401. Release 빌드 경고 0·오류 0. 아래 검사가 [Windows 실행 36301497300](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/36301497300)에서 통과했다. T03/T09/T10 별도 회귀 링크는 CURRENT_STATE.md의 T16 절을 따른다. 이후 변경은 문서만이다.

영상 검사는 실제 libVLC의 상태와 native mute를 확인했으나 러너의 `AudioUnavailable=True` 경로다. 실제 오디오 장치에서의 청취 및 외부 음소거 프로그램과의 조합은 아래 수동 미검증으로 남긴다.

- 실제 WPF: 모든 창/전체화면·원래 숨긴 창·늦은 창, 삭제 확인창의 숨김/복원·종료 대기, 네이티브 파일 선택창과 숨김 중 메시지창.
- 전역 키: 실제 등록/충돌/해제·재등록, WM_HOTKEY 연결. 트레이의 기존 메뉴/더블클릭 callback 및 모두 숨기기/복원 연결.
- 실제 libVLC: 앱 mute 의도·일시정지 보존, 여러 플레이어, 늦은 활성화, 실제 세션의 삭제 실패 후 동일 방문 재열기와 정상 Leave.
- T15 회귀: Opening/Active/SaveFailed/CommitUnknown, 반복 종료, 현재/무관한 Unknown, Succeeded 정리 실패, Deleting 성공·실패·취소, 스캔 종료.
- Core/Data 및 Scanner/Comic/Video/Subtitle 기존 회귀. 모든 미디어와 DB·저널은 테스트 생성물 또는 fixture 복사본이다.

## 사용자 Windows 수동 확인 — 미검증

T15 사용자 확인 대기와 T09/T10/T11 생략, T12 실사용 이관 상태를 그대로 보존한다. 자동 HWND/미디어 상태 검사는 실제 화면의 순간 노출·실제 스피커 출력·키보드 입력 확인을 대신하지 않는다.

1. 메인·감상·보조 창/전체화면을 함께 연 뒤 Ctrl+Shift+H. 화면·작업표시줄/Alt+Tab 노출과 실제 소리 차단을 확인한다. 같은 키와 트레이로 각각 복원한다.
2. 원래 mute/일시정지한 영상, 외부 자동 음소거 프로그램을 사용하는 상태에서 앱이 원래 상태를 유지하는지 확인한다.
3. 삭제 확인창/자막 파일 선택창이 열린 상태에서 숨김·복원한다. 빠른 종료 후 명시적으로 복원하여 취소했을 때 원본 파일을 삭제하지 않고 종료하는지 테스트 복사본으로 확인한다.
4. 준비 중·스캔 중 Ctrl+Shift+Q, 반복 키 입력, 종료 후 프로세스/전역 키/트레이 잔류를 확인한다.
5. 키 충돌 안내, Explorer 재시작 후 숨김 유지·복원 경로, 다중 모니터/DPI/실제 전체화면 영상의 순간 노출 여부를 확인한다.

저장/삭제/해제 실패를 사용자 원본 DB 권한 변경이나 원본 파일 삭제로 재현하지 않는다. 자동 오류 주입 결과와 수동 미검증을 구분한다.

## 확인한 Windows API

2026-09-27 Microsoft 공식 문서 접근 확인: [RegisterHotKey](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey), [CBTProc](https://learn.microsoft.com/en-us/windows/win32/winmsg/cbtproc), [SetWindowSubclass](https://learn.microsoft.com/en-us/windows/win32/api/commctrl/nf-commctrl-setwindowsubclass), [DWMWA_CLOAK](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute). 실제 사용자 화면/소리 확인과 특정 Windows 버전 지원 범위는 별도 검증이다.
