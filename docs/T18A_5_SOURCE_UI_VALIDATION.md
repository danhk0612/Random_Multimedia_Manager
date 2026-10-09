# T18A-5 소스 설정 UI 및 Explorer 검증

## 범위

`task/t18a-5-source-ui`에서 기존 경로·정책·스캔 조정자·감상 접근 API를 CategoryEditor와 LibraryBrowser UI에 연결한다. DB 스키마, 항목 ID/PathKey, 감상 기록/진행, 삭제 격리, 숨김·종료 동작은 변경하지 않는다.

## 구현

- 소스 입력은 IO 없는 `SourcePathRules.NormalizeFolder`를 사용해 로컬, 매핑 드라이브, 직접 UNC 경로를 정규화한다. UNC 공유 경계 밖으로 나가는 입력은 기존 `WindowsPath.Normalize`가 거부한다.
- 소스 편집 화면에 시작 검사, `Manual/Events/Scheduled`, 1~168시간 설정, 저장된 binding 종류/대상/revision, 실행 중 마지막 접근 상태를 표시한다. Events는 확인된 Local만 선택할 수 있다. Remote/Unknown은 Manual 또는 명시 예약을 사용한다.
- 정책은 `ScanCoordinator.SavePolicyAsync`를 사용한다. 유효성/DB 저장 실패 시 저장 전 정책, 마지막 성공 시각, watcher/admission 상태를 유지하고 편집 화면도 기존 저장값으로 복원한다.
- 바인딩 확인은 버튼에서 `ConfirmBindingAsync`를 호출해 현재 대상 근거를 다시 관찰한다. 성공/실패 상태는 현재 실행의 coordinator 관찰값으로 표시하며, 저장된 확인값만으로 프로세스 접근 허가를 추정하지 않는다.
- LibraryBrowser는 기존 SQLite 목록 조회를 우선한다. Explorer 명령 전 `ViewingAccess.CheckAsync`와 `RecheckAsync`를 worker에서 수행하고 source scan exclusive lease 또는 감상 창의 기존 lease 아래 접근한다. 최종 실행 전 최신 DB 경로·누락·격리·Closing·요청 세대·현재 선택을 다시 확인한다.
- Explorer 비동기 작업은 LibraryBrowser의 기존 pending read drain에 등록한다. 창이 닫히거나 선택이 바뀐 뒤 돌아온 결과는 프로세스 실행 및 화면 갱신에 사용하지 않는다.

## 자동 검증

- `T18A5SourceUiVerification`: UNC 소스 저장, 예약 정책 저장, 원격 Events 거부, 실패 후 이전 정책/감지 상태 복원, 명시적 UNC 바인딩 확인, 확인된 Local 기본값을 검사한다.
- Scanner `unc` 시나리오: 한글·공백·슬래시·dot segment 정규화, 공유 루트 허용, UNC 공유 경계 탈출 거부를 검사한다.
- T17 WPF 브라우저 회귀: 한글/공백 UNC 호환 인수 구성, 접근 검증 중 선택 변경 시 Explorer 호출 억제, DB 인덱싱 이후 제거 파일 거부를 검사한다.
- Windows x64 Release 빌드와 해당 영향 CI 결과는 PR Actions 완료 후 이 문서에 기록한다. 현재 로컬 실행 환경은 Linux이며 Windows 빌드/실행 결과로 간주하지 않는다.

## 실제 환경에서 확인할 항목

- 실제 NAS/RaiDrive 연결, 대상 재매핑, 연결 끊김/재연결 및 provider 캐시 동작은 별도 실물 검증이다.
- 자동 테스트의 Explorer 시작 대리자는 실제 Explorer 창을 표시하지 않는다. UNC·한글·공백 경로의 실제 Explorer UI 표시는 실물 확인 전까지 미검증이다.
- 실제 연결의 원격 휴지통 동작, 로그인/API, 같은 루트의 새 대상 수용은 범위 밖이다.

기존 T18A-1~4 검증 문서의 실물 미검증, T11 재실행 이력, 다른 Task 수동 미검증/승인 생략, AVI 조사 보류는 변경하지 않는다.
