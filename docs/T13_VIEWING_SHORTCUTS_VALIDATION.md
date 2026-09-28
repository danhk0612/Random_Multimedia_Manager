# T13 일반 감상 단축키와 전체화면 검증

## 기준과 상태

- 최신 main 기준 커밋: `db6ed3f93ff13ef67a780153c9c314ab9a06657c`.
- T17 PR #19는 merge commit `1df5560ce356f1a01fe718cc92b8ef90c3512767`로 main에 통합된 것을 확인했다.
- 작업 브랜치: `task/t13-viewing-shortcuts`.
- 검증한 제품 코드 커밋: `2e633372211839cc55abc7e60178ab3f443c1d97`.
- PR #20: https://github.com/danhk0612/Random_Multimedia_Manager/pull/20 (검토 대기, main 미통합).
- 키 배정과 충돌/반복 정책: docs/SHORTCUTS_AND_TRAY.md의 T13 표. 제품 코드 변경은 이 범위 및 기존 명령 경로에 한정했다.

## Windows 자동 검증

Windows Server 2025 10.0.26100 x64, .NET SDK 10.0.401에서 Release 구성을 실행했다. 빌드 및 전체 viewing runner가 성공했다.

| Workflow | 실행 | 확인 범위 |
|---|---|---|
| [T11 common viewing verification](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/36445632653) | 성공 (최종 job 재실행) | Release 빌드, Core/Data/Viewing, 스캐너, 만화·영상·자막 회귀와 WPF shortcut/fullscreen 회귀 |
| [T07 comic archive verification](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/36445633001) | 성공 | Release 빌드 및 만화 압축/페이지 회귀 |
| [T03 storage verification](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/36445632784) | 성공 | Release 빌드, Core/Data, WPF 셸 시작/정상 종료 회귀 |
| [T09 video native verification](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/36445632610) | 성공 | Release 빌드 및 영상 네이티브 수명 회귀 |

T11 최초 시도는 T13에 도달하기 전 기존 T16 네이티브 파일 선택기 복원 검사에서 실패했다. 같은 workflow의 실패 job만 재실행했고, 최종 job `109008053684`는 T13을 포함해 성공했다. 더 이른 검증 커밋에서 발견한 부모 감상창 모달 키 소비와 영상 입력 포커스 불안정을 고친 코드가 이 최종 실행에 포함됐다.

T13 WPF 자동 시나리오에서 다음을 확인했다.

- 즐겨찾기·이번 기록 제외·이전/다음·삭제 확인·재생/음소거/전체화면 키 자동 반복 억제; 처리 키만 소비하고 미지원 키/조합은 그대로 전달.
- 만화 이전/다음 페이지(1·2페이지 모드), Ctrl+Plus/Minus/numpad Add, Ctrl+0 맞춤; Busy에서 중복 페이지 작업을 큐에 넣지 않음.
- TextBox, ComboBox, ListBox, video volume Slider, IME composition 중 자체 입력을 유지하고 감상 명령을 실행하지 않음.
- 삭제 확인 모달에서 부모 창의 F/N 키가 처리되지 않음. 실제 모달을 여는 삭제/복구, 라이브러리, 자막 선택의 modal 구간에 로컬 키 차단을 둠.
- VisitCommit 실패를 주입해 SaveFailed를 만들고 N/F11이 frozen visit/창 상태를 바꾸지 않으며 기존 복귀 절차가 방문을 유지함.
- F11 borderless fullscreen, 3초 유휴 자동 숨김, 마우스/키 활동에 따른 컨트롤 재표시; 공통·만화·영상 컨트롤 모두 확인.
- T16 PrivacyWindows가 전체화면 창을 숨겼다가 이전 상태로 복원하고, 이어지는 Esc가 감상 창만 전체화면에서 빠져나오며 일반 창에서는 닫기/숨김을 하지 않음.
- 영상 Space 재생/일시정지, 방향키 5초 범위 탐색, 볼륨 ±5, 앱 mute 토글 및 반복 억제.
- Delete는 quarantine/disabled 게이트를 우회하지 않으며 확인창 취소 시 테스트 복사본/방문을 유지하고, 확인 시 반복 입력에도 테스트 복사본을 한 번만 처리함.

## 미실시 항목

이 검증은 GitHub Windows runner에서 실행한 WPF synthetic input과 임시 파일/테스트 복사본을 사용했다. 실제 사용자 Windows 데스크톱에서 물리 키를 눌러 확인하는 작업, 화면에서의 사용성 확인, 청취/실제 오디오 출력 검증은 수행하지 않았다. 자동 검증을 사용자 수동 확인으로 간주하지 않는다. T15/T16/T17의 기존 수동 미검증과 T11/T12 문서에 남긴 항목도 별도로 유지된다.
