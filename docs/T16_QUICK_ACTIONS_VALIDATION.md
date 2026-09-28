# T16 모두 숨김·음소거·빠른 정상 종료

## 구현과 경계

- 시작 기준은 T15 PR #17이 통합된 main `5b611c1`. 작업 브랜치는 `task/t16-quick-hide-exit`, PR #18이다.
- `Ctrl+Shift+H`는 모두 숨김/복원, `Ctrl+Shift+Q`는 빠른 정상 종료다. 각각 전역 등록하며 `MOD_NOREPEAT`를 사용한다. 등록 충돌은 해당 키를 비활성화하고 메인 상태에 안내한다. 모두 숨기기는 실제 복원 키 등록과 트레이 가용성이 함께 확보되어야 하며, 미충족 시 메뉴 비활성·이유를 표시하고 창/mute를 변경하지 않는다. 트레이 열기/복원·일반 종료는 계속 사용할 수 있다.
- `PrivacyWindows`는 UI 스레드의 앱 HWND만 관리한다. WPF `Hide()`로 `ShowDialog()`를 끝내지 않고 네이티브 표시를 숨겨 원래 대화상자와 명령 Task를 보존한다. 늦은 WPF/네이티브 창에도 표시 제어를 적용한다. 트레이 팝업은 복원 경로로 예외 처리한다.
- 숨기기 전 표시된 창과 숨김 중 표시를 요청한 창만 복원한다. 창 위치·최소화·전체화면과 모달 소유자의 비활성 상태를 변경하거나 영속 저장하지 않는다. 화면 합성 가림은 표시 전 적용하고 명시적 복원 시 해제한다.
- `PreparedVideo`의 기존 앱 mute 의도와 숨김용 mute를 분리한다. 활성/늦은 Ready/삭제 실패 후 재열기 모두 같은 출력 제한을 거친다. 숨김 자체는 pause, SuppressHistory, 감상 확정, 강제 checkpoint가 아니다. Windows 시스템·외부 프로그램 음소거 API는 호출하지 않는다.
- `AppLifecycle.ExitAsync`는 두 복원 수단이 모두 확인되면 즉시 숨김+음소거 후, 아니면 화면·mute를 유지하고 이유를 표시한 후 T15의 동일한 단일 종료 Task를 재사용한다. 준비 취소와 기존 UI/세션/삭제/스캔·DB 경계 대기, Leave 저장, 리소스 해제 순서를 보존한다. SaveFailed/CommitUnknown/현재 Unknown/미완료 성공/해제 오류를 성공 또는 NoOp으로 바꾸지 않는다.
- 숨긴 종료의 실패 시 자동으로 창을 노출하지 않는다. 숨김 없이 일반 종료를 시작한 경우 기존 창/모달·복구 UI를 유지한다. 사용자가 키/트레이로 명시적으로 복원한 뒤 기존 저장·삭제 복구 UI를 사용한다. Closing 중에도 열린 모달을 취소할 수 있도록 명시적 복원을 허용하되 신규 명령 차단은 유지한다. 복원은 삭제 동의나 종료 취소가 아니다.
- Explorer 재등록 실패 시 비공개 창을 자동 복원하지 않는다. 키·트레이 초기화는 독립 시도한다. 숨김 진입 전에 실제 등록한 복원 키는 Hidden/Closing/ExitBlocked와 재시도 동안 보유한다. 실패 가능한 저장·삭제·미디어/DB 및 트레이·후크 정리 후 최종 WPF OnExit에서 키를 해제한다. 강제 Kill/timeout/Pending 폐기는 없다.

## Windows 자동 검증

검증 코드 `b74babc43fbdca5736b3df6ff24914db1f5655ff`, Windows x64 10.0.26100 / .NET SDK 10.0.401. Release 빌드 경고 0·오류 0. 아래 검사가 [Windows 실행 36301497300](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/36301497300)에서 통과했다. T03/T09/T10 별도 회귀 링크는 CURRENT_STATE.md의 T16 절을 따른다. 이는 최초 구현의 검증 기록이며 병합 전 보완 검증은 다음 절에서 별도로 기록한다.

영상 검사는 실제 libVLC의 상태와 native mute를 확인했으나 러너의 `AudioUnavailable=True` 경로다. 실제 오디오 장치에서의 청취 및 외부 음소거 프로그램과의 조합은 아래 수동 미검증으로 남긴다.

- 실제 WPF: 모든 창/전체화면·원래 숨긴 창·늦은 창, 삭제 확인창의 숨김/복원·종료 대기, 네이티브 파일 선택창과 숨김 중 메시지창.
- 전역 키: 실제 등록/충돌/해제·재등록, WM_HOTKEY 연결. 트레이의 기존 메뉴/더블클릭 callback 및 모두 숨기기/복원 연결.
- 실제 libVLC: 앱 mute 의도·일시정지 보존, 여러 플레이어, 늦은 활성화, 실제 세션의 삭제 실패 후 동일 방문 재열기와 정상 Leave.
- T15 회귀: Opening/Active/SaveFailed/CommitUnknown, 반복 종료, 현재/무관한 Unknown, Succeeded 정리 실패, Deleting 성공·실패·취소, 스캔 종료.
- Core/Data 및 Scanner/Comic/Video/Subtitle 기존 회귀. 모든 미디어와 DB·저널은 테스트 생성물 또는 fixture 복사본이다.

## 병합 전 복원 수단 보완 자동 검증

- 보완 코드: `bea0b04066d9e6fa8a0a867e7064ae9f2d475a97`. Windows x64 10.0.26100 / .NET SDK 10.0.401 Release 빌드 경고 0·오류 0. 2026-09-28 아래 자동 검증이 모두 통과했다. 이후 변경은 지정한 다섯 문서뿐이다.
- [T11/T12/T15/T16 및 Core/Data·스캔·만화·영상·자막 통합 회귀 36362827110](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/36362827110)
- [T03 저장·앱 시작/정상 종료 36362827122](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/36362827122), [T09 영상 36362827097](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/36362827097), [T10 자막 36362827106](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/36362827106)
- 실제 RegisterHotKey 충돌과 트레이 가용성 실패 주입을 결합한다. Quick Hide의 표시/mute 무변경·메뉴 비활성 이유, 화면 유지 종료의 동일 Task·정상 방문 저장, 두 수단 실패 시 SaveFailed Pending/재시도·복귀 UI 및 삭제 모달 취소 접근을 검사한다.
- 숨김 후 트레이 재등록 실패는 생명주기 callback/가용성 실패 주입으로 검사한다. Hidden/ExitBlocked/Closing 재시도 동안 실제 등록 상태와 WM_HOTKEY 복원을 확인한다. 실제 Explorer 프로세스 재시작이나 사람의 키보드 입력을 수행한 것으로 해석하지 않는다.
- DB 종료 뒤 트레이 정리 예외를 주입하여 키 보존·복원을 검사한다. 정상 재시도 후 트레이 제거·후크 dispose/서브클래스 해제·키 해제/재등록을 검사한다. 기존 T15/T16 저장·삭제·미디어 회귀도 통과했다. 키 등록과 트레이 생성의 독립 try 구문은 코드 검토로 확인했으며, 실제 OS 자원 부족으로 생성자가 예외를 내는 상황까지 재현한 것은 아니다.

## 사용자 Windows 수동 확인 — 미검증

T15 사용자 확인 대기와 T09/T10/T11 생략, T12 실사용 이관 상태를 그대로 보존한다. 자동 HWND/미디어 상태 검사는 실제 화면의 순간 노출·실제 스피커 출력·키보드 입력 확인을 대신하지 않는다.

1. 메인·감상·보조 창/전체화면을 함께 연 뒤 Ctrl+Shift+H. 화면·작업표시줄/Alt+Tab 노출과 실제 소리 차단을 확인한다. 같은 키와 트레이로 각각 복원한다.
2. 원래 mute/일시정지한 영상, 외부 자동 음소거 프로그램을 사용하는 상태에서 앱이 원래 상태를 유지하는지 확인한다.
3. 삭제 확인창/자막 파일 선택창이 열린 상태에서 숨김·복원한다. 빠른 종료 후 명시적으로 복원하여 취소했을 때 원본 파일을 삭제하지 않고 종료하는지 테스트 복사본으로 확인한다.
4. 준비 중·스캔 중 Ctrl+Shift+Q, 반복 키 입력, 종료 후 프로세스/전역 키/트레이 잔류를 확인한다.
5. 다른 앱의 Ctrl+Shift+H 점유 시 Quick Hide 비활성·이유와 화면/mute 무변경을 확인한다. 트레이를 사용할 수 없으면 명시적 종료가 화면을 유지하고 이유를 안내하며, 저장 실패/모달 대기에서 복구·취소 UI에 접근되는지 확인한다.
6. 정상 숨김 후 Explorer 재시작/트레이 재등록 실패에서 자동 노출 없이 Ctrl+Shift+H 복원되는지, 종료 차단·재시도 중에도 복원되는지 확인한다. 다중 모니터/DPI/실제 전체화면 영상의 순간 노출과 실제 청취는 별도 확인한다.

저장/삭제/해제 실패를 사용자 원본 DB 권한 변경이나 원본 파일 삭제로 재현하지 않는다. 자동 오류 주입 결과와 수동 미검증을 구분한다.

## 확인한 Windows API

2026-09-27 Microsoft 공식 문서 접근 확인: [RegisterHotKey](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey), [CBTProc](https://learn.microsoft.com/en-us/windows/win32/winmsg/cbtproc), [SetWindowSubclass](https://learn.microsoft.com/en-us/windows/win32/api/commctrl/nf-commctrl-setwindowsubclass), [DWMWA_CLOAK](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute). 실제 사용자 화면/소리 확인과 특정 Windows 버전 지원 범위는 별도 검증이다.
