# 현재 구현 상태

## 기반

- 최초 조사 기준: main의 9ebda2c. 원격 브랜치는 main 하나였고 README와 docs 아래 기획 8개, 총 Markdown 9개만 있었다. 소스·솔루션·AGENTS.md는 없었다.
- PR #3 → #2 → #1을 병합하여 초기 기반·사용자 정책·T02 계약을 main에 통합했다. 통합 커밋은 f7f880f이며 다음 작업은 최신 main을 기준으로 한다.
- T01 작업 브랜치: task/t01-windows-shell-validation. PR #1 미병합 상태의 setup/project-foundation에서 분기했다.
- 기준 문서 6개와 상세 사양/미확정 목록으로 정리했다. 기존 중복 기획 4개는 통합 후 제거했다.
- 최소 .NET 10 WPF x64 셸과 솔루션, .gitignore를 준비했다.

## 실제 기능

| 영역 | 상태 |
|---|---|
| WPF 시작/빈 메인창 코드 | Windows 복원·Release 빌드·실행·닫기 정상 (사용자 확인) |
| SQLite·모델·설정·방문 저장·삭제 DB 정리 | T03 구현·Windows 자동 검증 완료 (main 통합 완료) |
| 실제 파일 삭제·저널 복구 | T12 완료 (PR #16 main 통합 완료, 잔여 수동 검증은 기본 완성 후 실사용으로 이관) |
| 분류/소스 폴더 UI | T04 구현·자동 검증·사용자 Windows UI 수동 검증 완료 (PR #9 main 통합 완료) |
| 라이브러리 최초/수동 스캔 | T05 main 통합. T18A-2 PR #24 보완·Windows 자동 재검증 완료, main 통합 완료 |
| 랜덤 후보·Pending 방문 핵심 | T06 구현·Windows 자동 검증 완료, PR #13 main 통합 완료; T11 완료, 기본 동작 사용자 확인·어려운 수동 검사 생략, PR #14 main 통합 완료 |
| 만화 ZIP/CBZ 페이지 읽기 기반 | T07 구현·Windows 자동 검증 완료 (PR #6 main 통합 완료) |
| 만화 표시·조작 | T08 구현·Windows 자동 검증 및 사용자 Windows 수동 검증 완료 (PR #10 main 통합 완료) |
| 만화/영상 이어보기 DB 연결 | T11 완료 — 기본 동작 사용자 확인, 어려운 수동 검사 생략·미검증 (PR #14 main 통합 완료) |
| 외부 SRT/SMI 자막 | T10 완료 (자동 검증 + 사용자 UI 검증, 일부 수동 항목 승인 생략, PR #11) |
| 영상 엔진·WPF 검증 호스트 | T09 완료 (잔여 수동 검증 사용자 승인 생략, PR #8 main 통합 완료) |
| 즐겨찾기·영구 제외·이번 제외·삭제 | T06 후보/이번 방문 억제·삭제 결과 전이 구현; 공통 UI는 T11 구현·자동 검증 통과, 실제 삭제는 T12 |
| 단축키·트레이·빠른 숨김/종료 | T15 PR #17 main 통합. T16 모두 숨김·앱 mute/복원·전역 키·빠른 정상 종료 구현 (PR #18 main 통합), Windows 자동 검증 완료·수동 미검증 |
| 일반 감상 단축키·전체화면 | T13 구현·Windows 자동 검증 완료 (PR #20 main 통합); 실제 사용자 키 입력/화면 확인은 미실시 |
| VSR | 후순위, 가능성 미검증 |

## T12 삭제·복구 구현

- 브랜치 `task/t12-file-deletion`, PR #16. 최신 main `a09009f`와 T11 PR #14 통합을 기준으로 T12만 구현했다.
- 확인 UI의 휴지통 기본/명시적 영구삭제/취소, PathKey 격리, durable 저널, 기존 ApplyDeletion/AppliedDeletion, 동일 방문 복귀 및 시작 복구를 연결했다. DB 스키마와 기존 기록 의미는 유지한다.
- 검증 코드 `a5ae8ff449dde5717843447b6d2f860cfa2a8055`: Windows x64/.NET 10 Release 빌드 경고 0·오류 0. T12 저장/복구 21개 시나리오 및 실제 휴지통·영구삭제·잠금/ACL·미디어 해제·취소 복귀·확인창 검사가 통과했다.
- Windows 실행 근거: [T11/T12 감상·삭제 및 만화/영상/자막 회귀 35549911943](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/35549911943), [T03 저장 35549911940](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/35549911940), [T05 스캔 35549911953](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/35549911953), [T06 세션 35549911980](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/35549911980), [T09 영상 35549911936](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/35549911936).
- 2026-09-21 사용자 지시에 따라 T12를 완료 처리한다. 사용자 Windows 삭제 UI 수동 검증은 미실시이며 기본 완성 후 실사용 검증으로 이관했다. 미검증을 통과로 표기하지 않으며 PR #16 main 통합을 완료했다.
- 상세 구현/검증 및 T14 후속 연결점은 docs/T12_DELETION_VALIDATION.md. 병렬 T14 문서와 상태 절은 수정하지 않았다.

## T13 일반 단축키와 전체화면 UX

- T17 PR #19의 main 통합 커밋 `1df5560ce356f1a01fe718cc92b8ef90c3512767`을 확인하고, main `db6ed3f93ff13ef67a780153c9c314ab9a06657c`에서 `task/t13-viewing-shortcuts`로 분기했다. PR #20은 `d7504b4`로 main 통합했다.
- docs/SHORTCUTS_AND_TRAY.md의 확정된 T13 로컬 키 배정으로 공통 감상 창에서 기존 버튼/명령 경로를 호출한다. 텍스트/숫자 입력, IME, 선택 상자, 목록, 슬라이더, 모달, Busy 및 비활성/격리 상태를 우회하지 않는다. Delete는 기존 확인창을 열며 실제 삭제 의미를 바꾸지 않는다.
- 전체화면은 borderless/maximized 표시와 3초 유휴 후 컨트롤 자동 숨김을 적용한다. 마우스/키 입력으로 컨트롤을 다시 표시하며, Esc 일반 상태는 창 닫기·종료·숨김으로 작동하지 않는다. 기존 창 상태와 T16 PrivacyWindows 숨김/복원을 보존한다.
- 검증 코드 `2e633372211839cc55abc7e60178ab3f443c1d97`의 Windows Server 2025 x64/.NET SDK 10.0.401 Release 빌드 및 T13 포함 회귀가 성공했다. T11/T12/T15/T16/T17·저장/스캔/만화/영상/자막 영향을 받은 자동 검사 결과는 docs/T13_VIEWING_SHORTCUTS_VALIDATION.md에 기록했다.
- 자동 검증은 synthetic WPF 키 입력과 테스트 복사본 삭제 확인/취소를 포함한다. 사용자의 실제 Windows 키보드/화면 검증, 물리 키 입력, 청취는 수행하지 않았으며 성공으로 표시하지 않는다. 당시 T18A 설계는 PR #21로 main에 통합됐으며 현재 구현 상태는 아래 T18A-1~2 절을 따른다.

## 검증

- 2026-09-07 사용자 Windows x64 PC에서 검증 완료 확인을 받았다. .NET 10 SDK 설치 및 최신 main 별도 clone 안내 후 restore·Release build·빈 창 실행·X 종료 결과 요청에 사용자가 “문제 없음”으로 확인했다.
- 검증 증거는 사용자 수동 확인이며 에이전트가 직접 실행한 결과가 아니다. 설치 후 SDK 패치 버전, 실제 로컬 HEAD 출력과 성공 로그는 별도로 제공되지 않았다. 안내 기준 main은 894517f이며 이를 실측 커밋으로 단정하지 않는다.
- 최초 실패 로그의 SDK 7.0.203 / NETSDK1045와 후속 exe 부재는 SDK 설치 전 환경 문제였다. 해당 실패를 최종 검증 상태로 유지하지 않는다.
- 솔루션/App 시작 구성의 기존 정적 검증 결과를 보존하며 T01은 사용자 확인을 근거로 완료 처리한다.

## T02 설계 상태

- T02 문서 계약 완료, 제품 코드/솔루션/패키지 변경 없음. 상세는 docs/DATA_AND_RANDOM_POLICY.md.
- 기준: PR #1/#2 미병합을 확인하고 docs/confirmed-policies(435170f)에서 docs/t02-data-session-contract로 분기.
- T01의 task/t01-windows-shell-validation(d11b6c0) 문서 결과를 확인해 위 검증 내용과 검증 대기 상태를 보존했다. T01 브랜치는 수정하지 않았다.
- 기존 문서의 T01 완료 전 T02 금지 및 D01/D02 사용자 재확인 문구는 최신 지시와 충돌하여 제거했다. T01에서 데이터 설계를 바꿀 기술 문제는 확인되지 않았으며 실행 가능성을 검증한 것은 아니다.
- T02 확인: 요구사항/상태표/Task 교차 검토, 문서 DDL의 임시 SQLite 제약 검사. 제품 구현 테스트나 Windows 검증을 뜻하지 않는다.

## 다음 작업과 차단

T18A-1 경로·저장 기반 구현과 Windows 자동 검증을 완료했다(PR #23, main 통합 완료). T18A-2 PR #24의 구현·보완·Windows 자동 재검증을 완료했으며 `d2848f5`로 main에 통합했다. T18A-3 PR #25는 구현·성능 보완·Windows 자동 검증 후 `7437e9d`로, T18A-4 PR #26은 보완 검증 후 `863c86d`로 main에 통합했다. T18A-5 PR #27은 Windows 영향 CI 통과 후 검토 대기이며 아직 병합하지 않았다. T18A-6·T18 배포는 미착수다. 설계 PR #22 main 통합 완료 (`2959391`). DECISIONS.md D10~D12/D14~D16은 2026-10-04 사용자 승인 완료다. D13 지원 목표는 확정이며 원격 호환성 실증은 아직 없다.

## T03 저장 구현

- 기준 main a2ae988. T01 사용자 Windows 확인과 T02 통합 결과를 보존했다.
- Core 공통 모델, App/Data SQLite v1 초기화·설정·방문/삭제 트랜잭션, 실행형 Core.Tests/Data.Tests를 추가했다.
- 동일 VisitId의 전체 payload 검증 불일치는 사용자 승인 후 VisitCommit 검증값 테이블로 보완했다. 상세 계약은 DATA_AND_RANDOM_POLICY의 T03 보완을 따른다.
- Windows Server 2025 x64 / .NET SDK 10.0.400 GitHub Actions에서 restore·Release build 성공(경고 0, 오류 0), Core 검사 20개 및 SQLite 통합 시나리오 13개 통과. 빈 WPF 창 생성·정상 닫기·재실행을 2회 확인하고 LocalAppData DB 생성을 확인했다. 사용자 데스크톱에서 직접 관찰한 결과와 구분한다.
- 검증 코드 커밋: 791529c. [성공 실행 34137980053](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/34137980053). 이후 완료 상태 갱신은 문서만 변경한다.
- 로컬 Linux에서는 SQL 실행/외래키, 문서와 SQL 일치, 프로젝트/XAML XML, diff 공백 검사를 수행했다. C# 실행 결과는 위 Windows 러너의 실제 로그를 근거로 한다.
- Microsoft.Data.Sqlite 10.0.8, SQLitePCLRaw.bundle_e_sqlite3 2.1.13을 고정했다. 최종 복원에 NU1903 경고 없음.
- T03 완료(PR #4 main 통합 완료). 다른 Task는 진행하지 않았다.

## T04 분류/소스 UI 구현

- 기준 main 5291e9d에서 task/t04-category-source-ui로 분기했다. T03 저장 API와 Core 모델을 그대로 사용하며 DB 스키마·공통 모델은 변경하지 않았다.
- 분류 생성/이름/활성/Comic·Video 타입 편집, 다중 소스 추가/편집/제거, 소스 활성/하위 폴더 포함 설정을 WPF View/ViewModel로 구현했다. MainWindow에는 해당 뷰 진입만 최소 연결했고 App 시작 코드는 변경하지 않았다.
- T02 경로 입력 계약을 UI 경계에서 적용하고 T03 AddSource의 중복 반환 의미를 유지했다. 중첩 소스는 허용하며, 소스 제거는 MediaItem/감상 기록을 변경하지 않는다. 실제 스캔·Missing 판단은 T05에 남겼다.
- PR #5 자동 검증 실행 34174712538: Windows Server 2025 x64 / .NET SDK 10.0.400에서 restore·Release build 경고 0/오류 0, Core 20개, 기존 SQLite 13개와 T04 3개 시나리오 통과. T04 시나리오는 재시작 영속성, 중복 옵션 비덮어쓰기/중첩 허용, 소스 제거 시 기록 보존, 빈/항목 존재 분류 타입 변경, 저장 실패 rollback을 검증한다. 앱 메인 창 실행·정상 종료 2회도 통과했다.
- 2026-09-09 잔여 검증 재개 시 최신 main `f17037b`와 T04 잔여 검증 지시, 현재 CategoryEditorView/ViewModel을 다시 확인했다. 작업 환경에는 Windows 데스크톱 직접 조작 수단이 없어 사용자에게 실제 UI 검증 절차를 제공했고 T09 검증 생략 승인은 적용하지 않았다.
- 사용자가 해당 Windows 절차를 직접 수행하고 “모두 정상”으로 확인했다. 분류 생성·이름/활성/Comic·Video 편집, 여러 소스 추가·수정·제거, 활성/하위 폴더 포함, 중복 소스 기존 옵션 보존, 중첩 소스 허용, 저장 후 앱 재시작 영속성을 실제 입력/클릭으로 확인한 결과다. 항목이 존재하는 분류의 타입 변경 제한은 기존 Windows 자동 T04 시나리오로 검증된 상태를 함께 사용하며, 이번 사용자 검증에서 별도 우회나 생략 승인을 적용하지 않았다.
- T04 범위의 새 문제는 보고되지 않아 제품 코드는 추가 수정하지 않았다. T04는 완료로 처리하며 PR #9에는 검증 결과와 상태 문서만 포함한다.

## T05 라이브러리 최초/수동 스캔 — 완료, PR #12 main 통합 완료

- 기준 main `d59648dc25e7eeb6935a3134d24296cbd11d0015`의 “T05 초기 인덱싱 확장자 결정”을 기존 `task/t05-library-scan` 브랜치에 반영해 재개했다. DB 스키마·Core 공통 모델·랜덤/기록 정책은 변경하지 않았다.
- 활성 분류 소스에서 수동 스캔을 실행한다. Comic `.zip/.cbz`, Video `.mp4/.mkv/.avi/.webm/.mov/.wmv/.m4v/.ts`만 마지막 확장자로 대소문자 무시 비교하며 대문자 확장자는 포함하고 `.mp4.tmp`, 자막, 비대상/확장자 없는 파일은 신규 미디어로 등록하지 않는다. 스캔 중 미디어 디코딩·재생·감상 기록 생성은 하지 않는다.
- 하위 폴더 포함 설정과 중첩 소스를 지원하면서 CategoryId/PathKey 기준 중복을 막고 분류별 ItemId/상태 분리를 유지한다. 기존 `ApplyObservedItems`로 성공 관찰 범위의 Present와 확정 Missing을 한 번에 반영한다.
- 접근 거부·오프라인/준비되지 않은 드라이브·취소·부분 실패, 소스 비활성/제거를 부재로 취급하지 않는다. reparse point 파일/폴더/상위 경로를 따라가지 않고 Windows 대소문자 구분 디렉터리는 미지원 안내 후 해당 범위 상태를 보존한다. 이동/이름 변경은 상태를 승계하지 않고, 같은 경로 재등장과 내용 교체는 T02/T03 계약을 따른다.
- 기존 T04 분류 화면에 `수동 스캔`/`취소`와 진행·완료/경고 문구만 최소 연결했다. 파일 목록 UI는 T17에 남겼으며 T08/T09 창 진입·종료, T10 자막 수명은 변경하지 않았다.
- PR #12 헤드 `2b84ece3a57971167735dddcbc4ebbb27a92ceda`에서 [T05 Windows 스캔 검증 34351854763](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/34351854763)이 성공했다. 테스트용 Windows 파일/SQLite DB로 최초/재스캔 중복, 중첩 소스, 하위 포함, 확장자 대소문자/비대상·자막·이중 확장자, 다중 분류, Missing, 소스 비활성/제거, 이동/재등장/내용 교체, 취소, ACL 접근 거부, 부분 성공, reparse, case-sensitive directory를 검증했다.
- 같은 헤드의 [T03 저장 회귀 34351854659](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/34351854659)와 [T09 영상 회귀 34351854664](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/34351854664)도 성공했다.
- 사용자 Windows 수동 검증에서 분류/테스트 소스 구성과 수동 스캔 실행·완료, 소스 활성 OFF 시 상태 유지 등 UI에서 확인 가능한 항목이 통과했다. 현재 UI에는 파일 목록과 Missing 표시가 없어 재시작 후 개별 인덱스와 파일 삭제 후 Missing은 직접 화면 확인할 수 없었으며 위 자동 DB 검증으로 확인했다. 테스트 파일 규모가 작아 취소 버튼은 스캔 완료 전에 누르기 어려워 수동 재현하지 못했고 취소 DB 미반영 자동 검증을 근거로 한다.
- 병합 검토 보완: 없는 소스 루트를 Missing으로 확정하기 전에 드라이브부터 기존 상위 경로의 reparse/대소문자 구분 여부를 검사한다. 미지원 상위 경로 아래 없는 소스는 상태를 보존하며 정상 경로의 실제 부재는 Missing으로 반영한다. 코드 `50e68dc9f2fe2de6697e7abf91c625976807befc`의 회귀 사례를 포함한 [T05 Windows 검증 34354089140](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/34354089140), [T03 저장 회귀 34354089195](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/34354089195), [T09 영상 회귀 34354089151](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/34354089151)가 성공했다. 기존 사용자 수동 검증과 미재현 범위는 유지한다.
- 사용자가 위 결과를 바탕으로 T05 완료 처리를 지시했다. PR #12 main 통합 완료. T06 구현은 다음 Astra Work 범위다.

## T07 만화 압축 읽기

- 기준 main 5291e9d에서 task/t07-zip-cbz-page-reader를 분기했다. 작업 중 PR #5/T04가 main에 통합되어 최종 브랜치 동기화 시 그 상태와 코드를 보존했다. Core 공통 모델·T02 계약·DB 의미는 변경하지 않았다.
- App/Media/Comic의 `ComicArchive`가 .NET `ZipArchive`로 ZIP/CBZ를 열고 JPG/JPEG/PNG/WEBP/BMP/GIF 엔트리만 내부 폴더 경로까지 포함해 자연 정렬한다. 압축 전체 추출이나 전체 페이지 메모리 적재는 하지 않는다.
- 압축 결과를 Opened/EmptyArchive/NoImageEntries/UnsupportedEncryption/CorruptArchive/Cancelled/Failed로 구분한다. 페이지 요청은 0-based 인덱스로 필요한 엔트리 스트림만 열고 Opened/InvalidPage/Cancelled/Failed를 반환한다.
- 압축 객체가 원본 파일·ZipArchive와 열려 있는 페이지 스트림을 소유한다. 페이지 스트림을 먼저 Dispose하면 해당 스트림만 해제되고, 압축 Dispose는 남아 있는 페이지 스트림까지 닫은 뒤 원본 파일 잠금을 해제한다. 취소된 압축 열기도 파일 핸들을 남기지 않는다.
- 압축 Opened는 T02의 감상 Ready가 아니다. T08에서 시작 페이지를 실제 디코딩한 뒤에만 만화 Ready를 완성해야 하며, T07 자체는 Pending/감상 기록을 생성하지 않는다.
- Windows Server 2025 x64 / .NET SDK 10.0.400 GitHub Actions에서 restore·Release build 및 T07 실행형 검증을 통과했다. 자연 정렬(1/2/10, 내부 폴더, 숫자 자릿수·선행 0·대소문자), 빈/이미지 없음/손상/암호화, 잘못된 페이지, 취소, 페이지·압축 소유권과 파일 잠금 해제를 확인했다. [T07 성공 실행 34174996255](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/34174996255).
- 같은 코드 헤드에서 기존 T03 회귀 workflow도 restore·Release build, Core/Data 검사, WPF 빈 창 2회 실행·닫기까지 성공했다. [회귀 성공 실행 34174996314](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/34174996314).
- PR #6 main 통합 완료. T08이나 다른 Task는 진행하지 않았다.

## T08 만화 표시·조작 — 완료, PR #10 main 통합 완료

- 기준 main `f17037b77a64c5cf7e42beb295f0246836675c72`에서 `task/t08-comic-viewer`를 분기했다. T07/PR #6의 main 통합을 확인한 뒤 작업했으며 DB·랜덤·기록·영상 구현은 변경하지 않았다.
- `PreparedComic`은 T07 `ComicArchive.Opened` 뒤 복원 대상 시작 페이지를 SkiaSharp로 실제 디코딩해야 `Ready`를 반환한다. 준비와 활성화를 분리하고 준비 generation/취소 토큰으로 늦은 결과를 폐기한다. 새 준비 실패·취소는 기존 활성 만화를 교체하지 않는다.
- 표시 모드는 한 페이지/두 페이지/세로 연속 스크롤, 읽기 방향은 좌→우/우→좌다. 원본/창/폭/높이 맞춤과 Custom 확대(10~800%), Ctrl+휠 확대, 일반 휠 이동/스크롤, 왼쪽 드래그 팬을 구현했다. Core `PlaybackProgress.Comic(page, offset)`을 입력/반환하지만 DB 저장과 공통 감상 세션 연결은 T11에 남겼다.
- SkiaSharp 4.151.2 코어 패키지를 고정했다. `SkiaSharp.Views.WPF`는 .NET 10 복원에서 legacy OpenTK/NU1701 경고가 확인되어 사용하지 않는다. SkiaSharp 오프스크린 BGRA 프레임을 Mitchell cubic sampling으로 렌더링한 뒤 WPF `WriteableBitmap`에 복사하며, 같은 프레임 크기에서는 출력 버퍼를 재사용한다. 창 `SizeChanged`는 40ms 디바운스로 과도한 연속 고품질 재렌더를 줄였다. 기존 SQLite·LibVLC 패키지 버전은 유지했다.
- 현재 기준 앞 1/뒤 2 페이지를 비동기 프리로드하고 디코딩 이미지 LRU를 256 MiB로 제한한다. ZIP 엔트리의 비 seek 스트림은 한 페이지 단위로 `MemoryStream`에 완전히 복사한 뒤 SkiaSharp로 디코딩하며, 동일 `ZipArchive`의 엔트리 바이트 읽기는 직렬화하여 프리로드 간 동시 읽기로 인한 부분 디코딩을 막는다. 전환/닫기에서 캐시 `SKBitmap`, 페이지 스트림, `ComicArchive`를 해제한다. 캐시 설정 UI, AI 확대, 추가 압축 형식은 추가하지 않았다.
- MainWindow에는 `만화 뷰어` 진입만 추가했다. 기존 T04 `CategoryEditorView`와 T09 `VideoValidationWindow` 소유/`ShutdownAsync()` 대기 종료 경로를 보존하며, 메인 종료 시 만화 뷰어만 먼저 정상 닫아 리소스를 해제한다.
- Windows Server 2025 x64 / .NET SDK 10.0.401 GitHub Actions 실행 `34320005257`: restore 성공, Release build 경고 0/오류 0. T08 자동 검사는 시작 페이지 디코딩 후 Ready, 손상 시작 이미지 DecodeFailed와 파일 잠금 해제, 페이지/세로 offset 복원, LTR/RTL spread, 페이지 경계 통과, 이미 취소된 준비의 Cancelled 반환과 기존 활성 보존, 종료 파일 잠금 해제, 17×2048² 이미지 압축에서 256 MiB LRU 오래된 페이지 퇴출을 통과했다. 기존 T07 검사 24개도 같은 실행에서 통과했다.
- 자동 검사 도중 이미 취소된 준비에서 `TaskCanceledException`이 노출되는 결함을 발견해 `Cancelled` 결과로 정규화했고 재검증에서 통과했다.
- 사용자 Windows 수동 검증에서 다음 결함을 발견·수정했다. (1) XAML 초기 선택 이벤트가 아직 생성되지 않은 `ZoomText`를 참조해 창이 종료되는 NRE, (2) 비 seek ZIP 엔트리 스트림 직접 Skia 디코딩으로 이미지 상단 일부만 표시되는 문제, (3) 진단 I/O와 연속 전체 프레임 재생성으로 맞춤/리사이즈가 느린 문제, (4) 비동기 프리로드가 같은 ZIP을 병렬 읽어 11페이지 이후 일부 페이지가 상단만 디코딩되는 문제. 모두 T08 내부에서 수정했다.
- 최종 수동 확인에서 만화 뷰어 정상 진입, 실제 ZIP/CBZ 전체 이미지 표시, 한/두/세로 표시 모드, 맞춤 변경, 창 리사이즈 체감 성능, 후반 페이지 진행을 재확인했고 사용자가 추가 문제 없음으로 확인했다. 별도 DPI 배율의 최종 재확인은 받지 않았으나 초기 사용자 환경 로그는 DPI 1.5 배율에서 렌더 크기 계산과 실제 표시 문제를 추적한 근거를 포함한다. 파일 잠금 해제는 자동 검사 근거를 유지한다.
- 최신 헤드 `694aaa85d4f1a9503a7982f452c5174504036136`에서 Actions T07 comic archive verification `34346329699`, T09 video native verification `34346329821`, T03 storage verification `34346329702`가 모두 성공했다. DB/영상 코드 자체는 수정하지 않았다.
- T08 구현·자동·사용자 Windows 검증 완료, PR #10 main 통합 완료.

## T09 영상 기반 — 완료

- 사용자가 “나머지 부분은 패스하고 T09 마무리 해”로 잔여 수동 검증 생략과 완료 처리를 승인했다. 생략은 검증 통과가 아니다. PR #8 main 통합 완료.
- LibVLCSharp/WPF 3.10.1, VideoLAN.LibVLC.Windows 3.0.23.1 고정, 실제 엔진 libVLC 3.0.23 Vetinari. 독립 엔진·고정 HWND·숨김/음소거 Prepare→Ready→Activate, 작업/방문 토큰, 최소 WPF 호스트와 비동기 해제를 구현했다.
- 사용자 승인 두 예외를 T02에 반영했다. 장치 부재는 영상 전용이며 연결 후 다시 열기로 소리를 복구한다. 끝 위치는 완료 상태를 유지하고 명시적 재생에서 같은 방문의 처음부터 시작한다.
- 코드 844d8a0의 Windows 영상 검사 34298998375와 저장/셸 회귀 34298998351 성공. 빌드 경고/오류 0. 실제 장치 없는 러너에서 무음 MP4/MKV·음성 MP4의 준비/토큰/취소/해제 및 끝 복원 검사가 통과했다.
- 최신 사용자 로그: AudioUnavailable=False, 음성 포함 MP4/MKV 각 3회 준비·취소·실패·끝 복원·정지 후 재생·복사본 잠금/이동/삭제 통과, Native probe complete 및 정상 종료 확인. 로컬 HEAD/SDK/GPU 세부값은 미제공이다.
- 사용자 수동 통과: 장치 없음/재연결, 끝 위치 복원/재생, 전체화면/중첩, 일반·소프트웨어 재생/실제 HW 확인. 자동 테스트는 시간이 걸리지만 응답 없음이 발생하지 않음을 확인했다.
- 사용자 승인 생략: 준비 중 화면·음성 누출/재생 중 현재 보존의 수동 관찰, 실패 시 UI 보존, 전체 기본 조작/내장 트랙, UI 반복·준비 중 종료, 별도 DPI 및 HW 실패/미지원 fallback. 준비 실패·취소·잠금 해제의 자동 검사 통과와 구분한다.
- 승인된 예외를 포함한 계약으로 T09 작업을 수락·완료한다. 모든 환경에서 전체 계약을 실측으로 입증한 것은 아니다. PR #8 main 통합으로 T09 자체의 T10/T11 차단을 해제하고 남은 실측 범위를 인계한다. 다른 Task는 진행하지 않았다.
- 상세 결과·소유권·T10/T11 인계는 [영상 검증 문서](docs/VIDEO_ENGINE_VALIDATION.md)를 따른다. DB·랜덤·기록·공통 감상 조정자·외부 자막·삭제는 구현하지 않았다.

## T07/T09 통합 검증

- T07 PR #6을 main에 병합한 뒤 T09 브랜치에 통합했다. T07의 App.Media 네임스페이스와 LibVLC Media 타입 충돌은 T09 PreparedVideo의 명시적 VlcMedia 별칭으로 해소했다. 기능·데이터 계약은 변경하지 않았다.
- 통합 코드 `b18a2830411e919c76baf93692428035461e0523`에서 [Windows 솔루션 빌드·Core/Data/T04·셸 회귀](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/34214680381)와 [무음 MP4/MKV 네이티브 검증](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/34214680409)이 성공했다. T07 테스트 프로젝트는 솔루션 빌드에 포함되며 T07 실행형 테스트의 최종 별도 성공은 `cfd28df`의 [34175399461](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/34175399461)이다.
- T04 UI 수동 조작은 사용자 확인으로 완료했다. T09의 최종 상태는 위 완료 절을 따른다. 병합 자체를 검증 성공으로 해석하지 않는다.
## T10 외부 SRT/SMI — 완료

- 기준 main `f17037b77a64c5cf7e42beb295f0246836675c72`에서 `task/t10-external-subtitles`를 분기했고 PR #11을 준비했다. T09 PR #8 main 통합을 시작 전에 확인했다.
- D07 위임 기본값은 docs/DECISIONS.md에 확정했다. 같은 폴더의 정확한 기본 이름 또는 점 접미사 SRT/SMI만 자동 후보로 보고, 정확 SRT → 정확 SMI → 점 접미사 SRT → 점 접미사 SMI 순으로 첫 후보 하나를 자동 선택한다. 언어/로캘 추측은 하지 않는다.
- UTF-8 SRT는 원본을 직접 사용한다. SMI는 엄격한 UTF-8 후 CP949 fallback을 사용하며 EUC-KR 호환 바이트도 CP949 범위로 처리한다. CP949/EUC-KR SMI는 UTF-8 임시 SMI로 변환해 현재 `PreparedVideo`가 소유하고 해제 후 삭제한다. 비 UTF-8 SRT는 임의 CP949 추측 없이 자막 실패로 반환한다.
- 기존 활성 MediaPlayer에 외부 subtitle slave를 추가하며 Media/MediaPlayer/디코더를 재생성하지 않는다. 자동/수동 자막 실패는 별도 결과와 안내로 처리하고 영상 Ready, 현재 VideoVisit, 재생/일시정지 상태를 변경하지 않는다. 자막 끄기와 후보/수동 선택을 검증 창에 연결했다.
- [T10 Windows 자동 검증 34319687933](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/34319687933): Windows Server 2025 10.0.26100, .NET SDK 10.0.401에서 솔루션 Release 빌드 경고 0/오류 0. 후보 검색/우선순위/자막 없음, UTF-8 SRT, CP949/EUC-KR SMI UTF-8 변환과 한글·SYNC 마크업 보존, 잘못된 SRT 인코딩 실패 분리, LibVLC 트랙 추가/선택/끄기, pause·Visit 보존, 임시파일/원본 핸들 해제, 다음 영상에 이전 외부 자막 미잔류가 통과했다. 실제 엔진은 libVLC 3.0.23 Vetinari였다.
- 같은 코드의 [T03 저장/셸 회귀 34319687867](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/34319687867)와 [T09 영상 네이티브 회귀 34319687871](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/34319687871)도 성공했다. DB/Core/만화/공통 세션/엔진 패키지는 변경하지 않았다.
- 사용자 Windows 수동 확인에서 한글 자막 재생, 자막 변경/선택, 자막 끄기 등 확인 가능한 실제 UI 동작이 정상임을 확인했다.
- 사용자는 수동 검증 2(UTF-8 SRT 한글 표시와 타임코드), 3(CP949/EUC-KR SMI 한글 표시와 `<SYNC>` 시점), 5(손상/잘못된 인코딩 자막 실패 후 현재 영상 유지)는 테스트 불가로 생략하고 T10 완료 처리를 승인했다. 이 세 항목은 수동 통과가 아니라 미검증으로 남긴다. 자동 검증 근거는 위 결과를 유지한다.
 

## T04/T08/T10 통합 검증

통합 코드 `1082342efbf0d0386a1e33a974b519262fbb47b8`에서 Windows 자동 검증이 성공했다: [저장·Core/Data/T04·메인 창 회귀](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/34348333860), [영상 네이티브 검사](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/34348333817), [외부 자막 검사](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/34348333890). T08 코드는 개별 검증된 코드와 동일하며 솔루션 통합 빌드에 포함됐다. 사용자 수동 검증과 승인 생략 범위는 각 Task 절을 그대로 따른다.

## T06 랜덤/Pending 핵심

- 기준 main `dfa41d4`, T02/T03/T05 통합과 PR #12 병합을 확인하고 `task/t06-random-pending`에서 구현했다. PR #13.
- Core 후보/방문 정책, App 조정자, 기존 SQLite snapshot/VisitCommit 확인 API를 추가했다. DB 스키마·미디어 엔진/자막/뷰어·공통 화면·실제 삭제·생명주기는 변경하지 않았다.
- 고정 선택 분류, 균등 후보, 7일/0일/미래 UTC 경계, Back/Forward·수동 삽입·방문별 억제, 준비→고정 payload 저장→활성, Busy/중복 명령/토큰/취소/두 미디어 소유권, 저장 실패·성공 불명 확인·재시도 및 삭제 결과 전이를 검증한다. API/소유권과 실패 처리의 단일 인계는 DATA_AND_RANDOM_POLICY.md의 T06 구현 절이다.
- Windows x64 (10.0.26100) / .NET SDK 10.0.401에서 코드 `381b9c4d97eff8764897cc98be7d820444b914fd`의 [T06 검증 34423475169](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/34423475169)이 성공했다. 솔루션 restore·Release build 경고 0/오류 0, Core 기존 20개 + T06 후보/방문/10000 한도 검사, SQLite 기존 13개·T04 3개 + T06 통합 10개 시나리오, T05 스캔 6개 그룹이 통과했다. 미디어 실패/지연은 테스트 대역이며 실제 SQLite 저장을 사용한다. 사용자 수동 검증을 받았다고 표기하지 않는다. T11 화면/실제 엔진 어댑터 통합은 아직 없으며 T09/T10 승인 예외·미검증 범위는 그대로 유지한다.
- 같은 코드의 [T03 저장/셸 회귀 34423475145](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/34423475145), [T05 스캔 회귀 34423475155](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/34423475155), [T09 영상 회귀 34423475200](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/34423475200)도 성공했다. 이후 완료 반영 커밋은 문서만 변경한다.
- T06 구현·지정 자동 검증 완료, PR #13 main 통합 완료. T11 착수 조건은 충족되었으며 실제 공통 화면 연결은 다음 Work 범위다.

## T11 공통 감상 연결 — 완료, PR #14 main 통합 완료 (수동 생략 범위는 최종 검증 정리 참조)

- 기준 main `d30ca0c`, T06 #13/T08 #10/T09 #8 통합 확인. `task/t11-common-viewing-ui`에서만 작업한다.
- 공통 WPF 감상 창, T06 실제 엔진 어댑터, 설정/공통 조작/진행 checkpoint/정상 닫기를 연결했다. DB 스키마·랜덤/기록 정책은 유지한다.
- 코드 `99e7763862a87294c7bf4b45ed102bb3447a4ae8`에서 Windows x64 (10.0.26100) / .NET SDK 10.0.401의 [T11 검증 34538104897](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/34538104897)이 성공했다. 솔루션 Release 빌드, Core/Data/T06, 실제 엔진 어댑터/SQLite 및 공통 WPF 창 자동 조작, 만화·영상·자막 회귀가 통과했다. 같은 코드의 T03 저장/셸 `34538104907`, T07 만화 `34538104929`, T09 영상 `34538105041`도 성공했다. 실제 사용자 Windows UI 확인은 대기한다. 구현 경계·실행 방법·수동 절차는 [T11 검증](docs/T11_VIEWING_VALIDATION.md)에 있다.
- PR #14 main 통합 완료. 아래 중간 검증 기록의 대기 표기는 당시 상태이며 최종 검증 정리가 현재 기준이다.

### T11 사용자 검증 후 보완

- 사용자 보고: 1·2 정상, 3 만화만 있는 분류의 랜덤 후보 없음, 4 일부 영상 무음, 5는 3으로 확인 불가, 6 미실행. 랜덤 감상 창 X를 두 번 눌러야 닫힘. 번호는 사용자 보고 그대로이며 미확인 항목을 통과로 처리하지 않는다.
- `67c66ba`: WPF Activated 이름 충돌 CS0108 수정.
- `3c237a1`: 활성 방문 없는 동기 LeaveAsync 완료가 Closing 이벤트 안에서 Close를 재호출하는 경로를 Dispatcher yield로 분리. `9c1c596`: 감상 전/후보 없음 이후 한 번 닫기 자동 회귀 추가. 해당 수정의 [Windows T11 검사](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/35094695450)는 통과했다.
- 만화 후보 없음은 사용자가 미스캔 원인으로 확인했다. 일부 AVI 무음은 기존 영상 검증 창에서도 재현되며 트랙 새로고침 후 동일하다. 같은 파일은 PotPlayer에서 정상이고 H.264/AC3 48kHz·2채널·448kbps이다. 활성 오디오 상태/보존 로그 진단과 합성 AVI 회귀를 추가했으며 원인 및 실제 소리 해결은 미확인이다. 정책/엔진을 추정 변경하지 않았다. T11과 PR #14는 완료/병합 대기다.

- 오디오 진단/합성 AVI 코드 `df17c831`의 [T11 Windows 검사](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/35097804090), [T09 영상](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/35097804123), T03 `35097804355`, T07 `35097804544`, T10 `35097804168` 모두 성공. AVI 3회 반복 포함. 실행 로그는 `AudioUnavailable=True`로 실제 AC3 소리 출력은 미검증이며 사용자 문제 파일의 활성 진단 대기.

- AVI/AC3 후속 확인: 사용자 PC에서 정상 MP4와 합성 AC3(bsid 8/6)는 유음, 원본 AVI와 스트림 복사 MKV는 무음이며 PotPlayer에서는 정상이다. bsid 6 또는 첫 출력 생성 실패만으로 무음 원인을 확정할 수 없다. 진단은 현재 방문의 동일 MediaPlayer를 읽지만 텍스트 포커스 동안 갱신이 멈춘다. 수집 UTC·파일명·VisitId, 타임스탬프가 있는 최근 전체 로그를 추가했다. 재생 경로는 변경하지 않았고 해결은 미확인이다. 코드 `59cee61`의 Windows 자동 검사 T11 `35209494309`, T09 `35209494292`, T03 `35209494337`, T07 `35209494356`, T10 `35209494392` 모두 성공했다. 실제 Windows 소리 출력과는 구분한다.

- AVI/AC3 Windows 진단 후속: 원본 AVI의 기본 demux에서 불완전 AC3 프레임과 반복 채널 변경이 발생했으며, 같은 파일을 AVFormat으로 입력하면 직접 재생/준비 후 활성 모두 해당 오류가 사라졌다. 사용자 청취 비교에서 AVFormat 원본 AVI와 기본 경로 추출 MKV 모두 유음을 확인했다. 사용자 승인에 따라 `.avi` 확장자(대소문자 무관)에만 `--demux=avformat`을 적용했다. 다른 확장자와 엔진/출력/저장 정책은 유지한다.
- 수정 코드 `5dc6cab8c84399ddfb2371afd10ae2de9e6e7e31`의 Windows 자동 검사 [T11](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/35336697791), T09 `35336697804`, T03 `35336697806`, T07 `35336697915`, T10 `35336697931` 모두 성공했다. Release 빌드·공통 감상·AVI 포함 영상 회귀를 통과했다. 제품 WPF에서 원본 소리·탐색·이어보기는 사용자 확인 대기이며 T11 완료/PR #14 병합은 보류한다.

### T11 재개 상태 (2026-09-19)

- 실제 제품 PID의 Core Audio 세션을 수집한 최종 60개 표본에서 Active/volume 1/mute true를 확인했다. 사용자는 비활성 창 자동 음소거 프로그램 사용을 알렸으며 무음 조사를 보류하도록 지시했다. 외부 프로그램의 개입 가능성은 있으나 영상별 차이와 음소거 설정 주체·시점은 확정하지 않았다. AVI AVFormat 수정은 유지하며 무음 해결/수동 통과로 처리하지 않는다.
- T11 나머지 진행을 재개한다. 기존 구현과 `5dc6cab`의 Windows 자동 검사 결과를 유지한다. 남은 작업은 공통 UI 수동 검증이며 우선 이어보기/처음부터, 이번 제외, 즐겨찾기/영구 제외, 정상 닫기를 확인한다. 이미 보고된 정상 항목과 스캔으로 해결된 만화 후보 문제를 반복 결함으로 취급하지 않는다. 나머지 미확인 수동 항목은 docs/T11_VIEWING_VALIDATION.md에서 유지한다. T11 완료나 PR 병합, T12 착수 승인은 아직 없다.

### T11 다음 전환 지연 보완 (2026-09-19)

- 사용자 보고: 다음 버튼 후 긴 대기 뒤 전환되며 멈춘 것처럼 보임. 별도 Windows 진단에서 시작 위치 복원 15초 timeout/restart가 관측됐으나 이번 사용자 전환과 동일 원인인지는 미확정이다.
- `5519fb6`: 전환 시작 시 준비 중/취소 안내를 먼저 표시하고 Dispatcher에 렌더링 기회를 준다. 첫 디코딩 이후 목표가 0이고 현재 시간이 기존 허용 범위인 0~1000ms라면 pause 확인 후 위치를 재확인해 중복 seek를 생략한다. 범위를 벗어나면 기존 복원 경로로 진행한다. 중간 이어보기·완료 위치·취소·소유권 계약은 유지한다.
- Windows 코드 `5519fb6225da8e4193f2f358894aa4ade8a7fe55`의 [T11 검사](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/35417802799), T09 `35417802748`, T03 `35417802743`, T07 `35417802827`, T10 `35417802745` 모두 성공했다. 사용자는 업데이트 후 다음 전환의 대기 시간 및 준비 안내 확인 요청에 ‘문제 없음’으로 응답했다. 해당 전환 지연 보완은 사용자 수동 확인 정상으로 기록한다. 다른 T11 수동 항목의 통과로 확대하지 않는다. 외부 자동 음소거 관련 조사는 계속 보류한다.

## T11 최종 검증 정리 (2026-09-19)

사용자는 ‘테스트 하기 힘들거나 불가한 부분 제외하고 기본적인 작동은 확인했어’라고 보고했다. 기본 동작은 사용자 확인 완료로, 어렵거나 불가능한 추가 수동 검사는 사용자 보고에 따라 생략·미검증으로 남기고 T11 구현/검증을 마무리한다. 항목별 결과가 별도로 제공되지 않았으므로 이전 수동 표 전체를 통과로 바꾸지 않는다. 준비 실패/취소·누출 등 개별 미확인 항목에는 기존 자동 검사 근거만 적용한다. AVI 무음 조사는 보류이며 해결로 표기하지 않는다. 다음 전환 지연 보완은 별도 사용자 정상 확인을 유지한다.

Windows 자동 검증 근거는 코드 `5519fb6225da8e4193f2f358894aa4ade8a7fe55`의 T11 `35417802799`, T09 `35417802748`, T03 `35417802743`, T07 `35417802827`, T10 `35417802745` 성공이다. 이번 변경은 검증 상태 문서만 갱신한다. PR #14 main 통합 완료. 후속 작업은 최신 main을 기준으로 한다.

## T14 프로그램 생명주기 정책 설계 — 완료, PR #15 main 통합

- 기준 main a09009f1c097c1071da077ff0654f675424a12da에서 task/t14-lifecycle-design 브랜치를 만들었다. T11 PR #14 통합을 확인했으며 T12와 병렬인 문서 설계만 수행했다.
- 실제 MainWindow/ViewingWindow/SessionCoordinator를 대조했다. 현재 ViewingWindow의 정상 닫기 LeaveAsync·SaveFailed 보존, 준비 취소, DB checkpoint 직렬화와 MainWindow의 비동기 영상 해제 대기 동작을 기존 계약으로 보존한다.
- docs/SHORTCUTS_AND_TRAY.md를 생명주기 단일 상세 기준으로 확정했다. 시작 표시/일반 최소화/메인 X→트레이 숨김, 최소 트레이 메뉴, Ctrl+Shift+H Quick Hide, 모든 앱 창 숨김+영상 앱 mute, pause 없음, 원래 mute/창 상태 복원, 외부 음소거 비개입을 결정했다.
- 빠른 정상 종료는 먼저 숨김+앱 mute, 신규 명령 차단, Opening 취소, 진행 중 직렬 작업/T12 경계 대기, Active Pending 정상 Leave 저장, 리소스 해제, DB·전역 키·트레이 정리 후 종료 순서다. 강제 Kill/timeout/Pending 자동 폐기를 추가하지 않았다.
- SaveFailed/CommitUnknown/미디어 해제 실패는 종료를 차단하고 숨김 상태에서 데이터를 보존한다. 트레이 복원으로 기존 재시도/감상 복귀 경로에 접근한다.
- T12의 삭제 저널·AppliedDeletion·격리 계약은 변경하지 않았다. T12 통합 후 Deleting/Unknown/Succeeded+DB 정리 관찰 경계와 미디어 해제 소유권을 대조했다. 구체적인 종료 규칙은 docs/SHORTCUTS_AND_TRAY.md §6에 기록했다.
- 제품 코드, 패키지, DB 스키마, AVI 무음 보류 조사, T15/T16 구현은 변경하지 않았다. 문서 정합성 검토만 수행했다.

## T15 트레이·일반 정상 종료 — PR #17 main 통합, 사용자 확인 대기

- 기준 main `b850b324ddc5d5395e481a142a10d4f52d6cb00d`에 T12 #16/T14 #15가 통합됨을 확인하고 `task/t15-tray-lifecycle`에서 작업했다.
- 트레이 열기/복원·더블클릭·종료, 시작 표시/일반 최소화/메인 X 숨김, 감상 X 정상 Leave, 스캔·감상·삭제 명령 대기 후 DB/트레이/WPF 종료를 연결했다. 메인 종료/삭제 복구 버튼은 트레이 실패·종료 차단 시 접근 경로다.
- SaveFailed/CommitUnknown·현재 경로 격리·Succeeded 정리 실패·미디어 해제 실패는 종료를 차단한다. 기존 Pending/FrozenCommit/저널과 복구 UI를 보존한다. 모든 창 즉시 숨김·음소거/전역 키는 T16에 남기고 해당 트레이 메뉴는 비활성이다.
- 검증 코드 `c00b68e3259e110f0af9d0a587299e7f15b0511c`: Windows x64 10.0.26100 / .NET SDK 10.0.401. Release 빌드 경고 0·오류 0. T15 데이터 경계와 실제 WPF 시나리오, Shell_NotifyIcon 등록/갱신·메뉴/더블클릭 callback·해제, 저장/삭제/미디어 회귀가 통과했다.
- 성공 실행: [T11/T12/T15 감상·생명주기 및 만화/영상/자막 회귀 36231753776](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/36231753776), [T03 저장·앱 시작/정상 종료 36231753784](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/36231753784), [T05 스캔 36231753788](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/36231753788), [T06 세션 36231753787](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/36231753787), [T07 만화 36231753769](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/36231753769), [T09 영상 36231753785](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/36231753785), [T10 자막 36231753774](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/36231753774).
- 이후 변경은 기준/검증 문서뿐이다. 실제 사용자 Windows 수동 확인(트레이 클릭·전체화면/보조 창·Explorer 재시작)은 미실시이며 기존 생략 승인을 적용하지 않는다.
- 상세 검사/구현 제약: docs/T15_LIFECYCLE_VALIDATION.md. PR #17은 main에 통합됐다. 다음 구현은 T16이며 사용자 수동 확인은 미검증으로 유지한다.

## T16 모두 숨김·음소거·빠른 정상 종료 — PR #18

- 기준 main `5b611c19773e3a735566a9aef64f54fe4498f464`, T15 PR #17 통합 확인. 브랜치 `task/t16-quick-hide-exit`.
- Ctrl+Shift+H 모두 숨김/복원, Ctrl+Shift+Q 빠른 정상 종료를 전역 등록한다. 충돌 시 해당 키를 비활성화하고, 복원 키·트레이 중 하나라도 없으면 Quick Hide도 비활성/이유 안내한다. 모달 명령 Task와 원래 창 상태·앱 mute 의도를 보존하고 늦은 창/Ready/삭제 실패 재열기에 비공개 상태를 적용한다.
- T15 단일 종료 Task와 WhenIdleAsync/ReleaseError, RequestCloseAsync, Pending/HasIncompleteSuccess 경계를 보존한다. 실패 시 자동 창 복원은 제거했다. DB 스키마·삭제 저널·기록 정책은 변경하지 않았다.
- 검증 코드 `b74babc43fbdca5736b3df6ff24914db1f5655ff`: Windows x64 10.0.26100 / .NET SDK 10.0.401. Release 빌드 경고 0·오류 0. 실제 WPF/네이티브 모달·트레이 팝업·전역 키 등록/충돌/해제, 최소화/전체화면 복원, 늦은 창·Ready·삭제 실패 재열기, 기존 앱 mute/pause, DB writer 대기와 T15 종료 경계가 통과했다.
- 성공 실행: [T11/T12/T15/T16 및 Core/Data·스캔·만화·영상·자막 회귀 36301497300](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/36301497300), [T03 저장·앱 시작/정상 종료 36301497269](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/36301497269), [T09 영상 36301497322](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/36301497322), [T10 자막 36301497276](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/36301497276). 최초 구현 검증이며 병합 전 보완 검증은 아래와 구분한다.
- 실제 사용자 키보드/트레이 클릭, 순간 화면 노출과 실제 스피커·외부 음소거 조합, Explorer 재시작은 미검증이다. T15 미검증과 T12 실사용 이관·T09/T10/T11 기존 생략 상태는 보존한다. T16 네이티브 영상 검사의 오디오 장치 부재 경로와 실제 청취 검증을 구분한다.
- 2026-09-28 병합 전 보완: 실제 복원 키+트레이 동시 가용 조건, 숨김 불가 시 화면 유지 일반 정상 종료, 독립 초기화, Hidden/Closing/ExitBlocked 키 유지 및 최종 해제 순서를 보완했다. 코드 `bea0b04066d9e6fa8a0a867e7064ae9f2d475a97`에서 Windows x64/.NET SDK 10.0.401 Release 경고 0·오류 0, 지정 복합 실패 및 기존 회귀를 통과했다. [통합 회귀 36362827110](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/36362827110), [저장·앱 시작/종료 36362827122](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/36362827122), [영상 36362827097](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/36362827097), [자막 36362827106](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/36362827106). 이후 변경은 지정 문서뿐이다. 트레이 실패는 오류 주입이며 실제 Explorer 재시작·사용자 키 입력·청취를 통과로 바꾸지 않는다.
- 상세 구현·자동/수동 검사: docs/T16_QUICK_ACTIONS_VALIDATION.md. PR #18 main 통합 완료. T17 PR #19는 main에 통합됐다.

## T17 분류별 라이브러리 브라우저 — 구현·Windows 자동 검증 완료, PR #19 main 통합 완료

- T16 PR #18 main 통합 후 최신 main `478f2f75ee7a78b2cc1c00028d7f08545f7f967e`에서 `task/t17-library-browser`로 진행했다. PR #19: https://github.com/danhk0612/Random_Multimedia_Manager/pull/19. PR #19 main 통합 완료.
- MainWindow의 분류 탭에서 목록을 열고, 기존 ViewingWindow에도 라이브러리 열기 진입점을 추가했다. 분류별 목록·대소문자 구분 없는 파일명 검색·즐겨찾기/영구 랜덤 제외/감상/미감상 필터, 이름·경로·미디어 종류·등록/누락·저장 크기·최근 감상·저장 진행 위치를 표시한다. 저장 플래그는 조회만 하며 스캔/디코딩/스키마 변경은 없다.
- 수동 열기는 두 진입점 모두 ViewingWindow `Run`/`Navigate`/SessionCoordinator의 기존 manual-open 흐름을 따른다. 최근 감상·랜덤 제외·비활성 분류여도 직접 열 수 있다. same-ItemId no-op, cursor 뒤 삽입 시 Forward 유지, 기존 Resume와 정상 Leave/기록 저장을 보존한다. 읽기 Task는 창 닫기/빠른 종료에서 drain하여 DB 수명 이후까지 남지 않으며, 닫힌 화면으로 늦은 결과를 반영하지 않는다.
- 검증 커밋 `5d4a33d84ae44fa5a134fc886097ed5b631e1295`, Windows Server 2025 x64 (10.0.26100), .NET SDK 10.0.401. Release 빌드 경고 0·오류 0. [T11 통합 WPF + T17 UI/세션 + 만화/영상/자막 회귀 36397417863](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/36397417863), [T03 저장/시작/종료 36397417840](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/36397417840), [T06 세션 36397417902](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/36397417902), [T09 영상 36397417777](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/36397417777) 성공.
- 자동 WPF 검증에서 파일 분류/필터/정보, 한글·공백 경로의 Explorer 단일 인수와 최근 삭제 경로 거부, 최근 감상·제외 항목 수동 열기, 세션 저장/Forward/no-op, 열린 브라우저 모달 중 종료를 검사했다. 테스트는 실제 Explorer 프로세스를 띄우지 않으므로 Windows Explorer 창의 실제 표시와 사용자 클릭은 미검증이다. 실제 사용자 Windows 수동 확인을 완료했다고 표기하지 않는다. 다른 Task의 수동 미검증·승인 생략은 변경하지 않았다.
- T17은 구현·자동 검증 및 main 통합 완료 상태다. 상세 기준과 잔여 확인은 docs/T17_LIBRARY_BROWSER_VALIDATION.md를 따르며, 다음 Task 구현은 시작하지 않았다.

T17 병합 검토: PR 최종 코드 `01b7bbd2b48b5decd94f0e3a6ffeb2da93e1317c`의 Windows Actions 36398161996/36398161950/36398161956/36398161953 모두 성공을 확인했다. 사용자 수동 검증을 뜻하지 않는다.

## T18A 네트워크 설계 보완 — PR #22 main 통합, 정책 승인 완료

- 최신 main `c8252a0`에서 `task/t18a-network-design` 분기. T18A 기존 설계 PR #21의 merge `7fb2ad4`를 확인했다.
- DATA_AND_RANDOM_POLICY T18A 절을 소스별 정책·UNC/매핑 바인딩·재매핑 차단·오프라인/캐시 부재·별칭 삭제/휴지통·지연 IO/정상 종료·v2 migration/저널 제안으로 보완했다. 전역 두 bool 및 10분 전체 검사안은 대체했다.
- 실제 경로 등록/스캐너/후보/ZIP/영상/자막/Explorer/삭제/종료 코드와 대조했다. UNC 명시 거부, 문자 기반 경로 검사, UI의 동기 File.Exists, 삭제 NotFound 및 단일 키 격리의 원격 보완 지점을 문서에 명시했다. 네트워크 결함을 실물 재현했다는 뜻은 아니다.
- 구현 인계는 T18A-1~6으로 나눴고 선행·담당·공유 파일 충돌·병렬 가능 검증 준비를 명시했다. D10~D12/D14~D16은 2026-10-04 사용자 승인 완료다. D13과 함께 구현 기준으로 사용한다.
- 검증: 문서/코드 대조, 문서 링크·범위·공백 검사. 제품 코드·SQL/DB·패키지·workflow 변경 없음. Windows 빌드/오류 주입/NAS/RaiDrive 실행은 하지 않았다. N01~N12는 후속 검증 조건이다.
- 이전 Task의 수동 미검증/승인 생략 및 AVI 무음 조사 보류를 유지한다. 설계 PR #22를 `2959391`로 main에 통합했다. T18A-1 저장 기반은 아래 PR #23에 구현하며 후속 런타임 연결·T18 배포는 미착수다.

## T18A-2 소스별 스캔·생명주기 연결 (PR #24)

- 기준 main `7b90722`, 브랜치 `task/t18a-2-scan-lifecycle`. `MainWindow.Scans`가 실제 Windows 연결 근거와 바인딩 revision/generation을 검증하고 SourceId별 관찰·watcher·시작/예약을 조정한다. 원격 Manual 자동 열거는 하지 않으며 원격/불확실한 부재로 기존 항목을 Missing 처리하지 않는다.
- 감상 창 생성 전부터 ShowDialog 반환까지 스캔 배타 lease를 유지한다. 소스 편집/삭제 복구와 반영을 직렬화한다. Closing은 신규 스캔/감상/편집/삭제를 차단하며 취소를 무시하는 실제 worker도 완료한 다음 기존 종료 drain과 DB 해제를 진행한다. ExitBlocked의 기존 저장 재시도·복구·복원은 유지하고 스캔을 자동 재개하지 않는다.
- 검증 코드 `093a3fc`의 Windows x64/.NET 10 Release 빌드(경고 0·오류 0) 및 T03/T05/T06/T09/T11 CI 5개가 통과했다. N04~N05/N09~N11 조정자 오류 주입과 실제 로컬 watcher/WPF 검증, 실제 NAS·RaiDrive 미검증을 [검증 기록](docs/T18A_2_SCAN_LIFECYCLE_VALIDATION.md)에 구분한다. 이후 완료 반영은 문서만 변경한다.
- PR #24 main 통합 완료 (`d2848f5`). T18A-2 당시 원격 삭제/v2 복구, 후보 정책, 감상 엔진 지연 IO, UNC·설정 UI는 후속 T18A-3~6에 남겼으며 최신 삭제 연결은 아래 PR #25 절을 따른다. T18 배포와 직접 병합은 수행하지 않았다.


## T18A-1 경로·바인딩·저장 기반

- 기준 main `ae77b8b`, PR #22 통합과 D10~D12/D14~D16 승인을 확인했다. 브랜치 `task/t18a-1-path-storage`, [PR #23](https://github.com/danhk0612/Random_Multimedia_Manager/pull/23), 구현·Windows 자동 검증 완료/main 통합 완료.
- IO 없는 로컬/매핑·UNC 정규화, 연결 근거 분류·revision/generation 확인 계약, 소스별 정책 API, v1→v2 migration, v1/v2 저널 읽기·검증을 구현했다. 기존 ID/키/기록/진행/저널 보존을 검사한다.
- 자동 갱신/원격 삭제 활성화, UNC UI, 스캔·감상 IO admission 연결은 하지 않았다. T18A-1 당시 v2 저널 복구는 T18A-3 연결 전 전역 차단했고 최신 연결은 아래 PR #25 절을 따른다. 실제 API/필드/후속 연결 경계는 DATA_AND_RANDOM_POLICY T18A §7.
- 검증 코드 `af4bd6e`: Windows Server 2025 x64/.NET SDK 10.0.401 Release 빌드 경고 0·오류 0. N01~N03 6개와 T03/T05/T06/T09/T11(만화·자막 및 T12~T17 영향 회귀 포함) CI 5개 모두 성공. 실제 NAS/RaiDrive·사용자 DB·Windows 수동 UI는 미검증. Linux 부분 실행 및 최초 Windows 검사 기대값 보완은 검증 문서에 구분했다.
- PR #23은 `08adddc`로 main 통합 완료했다. 검증 이후 최종 헤드 `06a89f4`까지의 차이는 문서 3개뿐임을 확인했다. 상세 검증은 docs/T18A_1_PATH_STORAGE_VALIDATION.md. T18A-2/3도 PR #24/#25로 통합됐으며 다음 작업은 T18A-4다. 인계는 TASKS.md의 새 작업 지시문을 따른다. T18A 전체 완료나 실제 네트워크 호환성 통과를 뜻하지 않는다.


## T18A-2 통합 결과와 다음 작업

PR #24를 `d2848f5`로 main에 통합했다. 종료 drain·probe 합류·진행 콜백·요청별 취소·watcher backoff의 다섯 보완을 검토했다. 검증 코드 `ac2a4d6`의 Windows CI 5개 성공, 이후 `d3c11bc`까지 문서 4개만 변경됨을 확인했다. T11 최초 파일 선택창 복원 실패와 코드 변경 없는 1회 재실행 통과는 docs/T18A_2_SCAN_LIFECYCLE_VALIDATION.md에 보존한다. 실제 NAS/RaiDrive·사용자 로그오프는 미검증이다.

T18A-2 통합 당시 다음은 고성능 Work의 T18A-3(원격 삭제·별칭/광역 격리·v2 복구)이며 TASKS.md의 새 작업 시작 지시문을 따른다. T18A 전체 완료나 배포 완료가 아니다. 제품 구현은 기존 선행 순서대로 진행하며 테스트 환경/복사본 준비만 병렬 가능하다.

## T18A-3 원격 삭제·별칭 격리·v2 복구 (PR #25, main 통합 7437e9d)

- 최신 main `b5debb3`(T18A-2 merge `d2848f5`)에서 `task/t18a-3-network-deletion`을 시작했다. 검증된 Windows 매핑 대상+상대 경로의 정확한 UNC 별칭만 사전 캡처한다. 원격/불명 광역 격리에는 소스 밖 과거 항목도 포함되며 추정 별칭 기록은 정리하지 않는다.
- 새 실행은 v2 Prepared durable 저널 이후 한 번만 OS 호출한다. 원격 부재/응답 유실은 Unknown으로 보존하고, 원격 휴지통은 실제 backend 검증 근거가 없어 기본 Unknown/차단이다. 기존 영구삭제는 사용자의 별도 선택에서만 실행한다.
- v1/v2 복구는 저장된 ItemId/PathKey 집합의 DB 정리만 수행한다. 대상별 키 검증·기록/진행/VisitCommit 제거·AppliedDeletion은 같은 transaction이며 저널 제거 실패/재등장도 멱등 처리한다. 손상 v2는 전역 차단, v1 원격 가능성은 보수 광역 격리다.
- 실제 삭제 IO가 살아 있으면 성공/실패 확인으로 격리를 먼저 풀 수 없다. Closing은 새 삭제 조회/OS IO를 막고 실제 작업 완료 및 기존 세션 명령/복구 drain 뒤 DB를 해제한다. 실패 복귀도 원래 실행 바인딩을 확인하며 재매핑 시 재열기 없이 Pending·마지막 진행을 보존한다.
- 검증 코드 `7ab2448`의 Windows Server 2025 x64/.NET SDK 10.0.401 Release 경고 0·오류 0, N07~N09 새 시나리오 10개 및 T03/T05/T06/T09/T11 CI 5개가 모두 통과했다. 검증 결과는 [T18A_3_NETWORK_DELETION_VALIDATION.md](docs/T18A_3_NETWORK_DELETION_VALIDATION.md)를 따른다. 실제 NAS/RaiDrive backend·실제 사용자 로그오프는 미검증이다. 기존 T11 파일 선택창 복원 재실행 이력·수동 미검증·승인 생략·AVI 조사 보류는 유지한다. PR #25 병합 전에는 후속 T18A-4를 시작하지 않는다. UNC/설정 UI·감상 후보/엔진 IO·배포는 추가하지 않았다.

## T18A-3 PR #25 병합 검토 보완

최신 main `047031a`의 병합 보류 사유와 TASKS.md 보완 지시문을 반영했다. 기존 PR의 구현·검증 기록은 보존한다. 격리 판정의 항목별/소스별 SQL을 DB gate/read transaction의 판정 단위 스냅샷으로 개선했다. 장기 캐시 없이 후보/소스 배치가 재사용하고 실제 열기/반영의 live 게이트를 유지한다.

최종 코드·테스트 `f4f3b29dc86313fb3612d54b4acb0e8910be1569`(제품 코드 `6836bc4`)의 Windows Release 경고 0·오류 0, N07~N09 12개 및 T03/T05/T06/T09/T11 영향 CI 5개가 모두 통과했다. 100/10,000항목·48소스에서 격리 스냅샷 3회·DeletionPaths 2회·Pump 3회·분류 수동 요청 59회·랜덤 명령 18회로 SQL 수가 동일함을 확인했다. 이력/진행 없는 격리 조회와 Local/매핑/UNC/Unknown·과거 항목·겹친 pending·해제/경계/변경의 의미를 검증했다. 새 fixture 소스 범위 오류로 인한 최초 실패와 수정은 [T18A-3 검증 기록](docs/T18A_3_NETWORK_DELETION_VALIDATION.md)에 남겼다.

PR #25를 `7437e9d`로 main에 통합했다. 검증 코드 이후 `04e6408`까지 문서 4개만 변경됐음을 확인했다. 다음은 TASKS.md 지시문의 T18A-4다. 실제 NAS/RaiDrive·로그오프 미검증, T11 파일 선택창 복원 1회 재실행 이력과 기존 수동 미검증/승인 생략은 유지한다. T18A-4·UNC UI·배포는 시작하지 않았다.

## T18A-4 감상 연결 최초 제출 (보완 결과는 아래)

`task/t18a-4-network-viewing`에서 연결/바인딩 일시 후보 제외, 세션 실패 집합, 열기 전후 바인딩 확인과 live 삭제 격리, ZIP/영상/자막 실제 IO drain을 구현했다. 기존 Pending/기록/삭제/숨김·복원 의미를 보존한다. 검증 코드 `9862d87`은 Windows x64 Release 경고·오류 0개 및 영향 CI 7개가 모두 통과했다. 실제 NAS/RaiDrive는 미검증이다. 상세 검사와 제한은 docs/T18A_4_NETWORK_VIEWING_VALIDATION.md를 따른다. PR #26 main 통합 완료이며 T18A-5/6·배포는 미착수다.

## T18A-4 PR #26 보완 결과

최신 main `f44ed67`의 검토 지시 전체를 기존 `task/t18a-4-network-viewing`에 반영했다. 수정 전 회귀 검사 `ff61649`에서 준비 후 파일 재확인 실패의 다음 랜덤 재시도와 완료 위치 SeekAsync의 native seek 발행을 Windows에서 각각 재현했다. `ccb982a`는 재확인 false/예외의 세션 실패 억제를 추가하고 취소/Closing을 제외하며, 동일 완료 위치 비동기 seek의 native 발행을 생략한다. 늦은 Ready 실제 해제·Pending/cursor/Forward/Seen/기록, 완료 진행·처음부터 PlayAsync·다른 위치 seek·privacy mute·IO/drain을 검증한다.

최종 코드·테스트 `ccb982a497328ca61a4e30f0f981a2ed32506295`의 Windows x64 / OS 10.0.26100 / .NET SDK 10.0.401 Release 빌드 경고 0·오류 0 및 T03/T05/T06/T07/T09/T10/T11 영향 CI 7개가 모두 통과했다(해당 SHA 재실행 없음). 재현/수정 전후 근거는 docs/T18A_4_NETWORK_VIEWING_VALIDATION.md를 따른다. 실제 NAS/RaiDrive·기존 수동 미검증·T11 재실행 이력·AVI 조사 보류를 유지한다. PR #26을 `863c86d`로 main에 통합했다. 검증 코드 이후 `1813d67`까지 문서 3개만 변경됐다. 다음은 TASKS.md의 T18A-5 지시문이다. T18A-5/6·배포는 시작하지 않았다.

## T18A-5 소스 설정 UI와 Explorer 연결 (PR 제출·Windows 자동 검증 완료)

- 최신 기준 main `91778a4`와 T18A-1~4 통합을 확인한 뒤 `task/t18a-5-source-ui`에서 진행한다.
- UNC/로컬 입력 정규화, source별 정책 및 연결 상태/명시 확인 UI, DB 목록 기반 비동기 Explorer 연결을 기존 API에 연결했다. 저장 실패 복원, 선택 변경/닫힘 뒤 지연 결과 억제, Explorer 접근 확인의 pending read drain 검증을 추가했다.
- 코드 검증 커밋 `3a21a121e21eb92540fea2d9c081de72319d5ba8`은 Windows x64 / OS 10.0.26100 / .NET SDK 10.0.401 Release 경고 0·오류 0, T03/T05/T09/T11 영향 CI 4개가 통과했다. 자동/실물 검증 구분은 [T18A-5 검증 문서](docs/T18A_5_SOURCE_UI_VALIDATION.md)를 따른다.
- PR #27은 `task/t18a-5-source-ui`에서 열려 있으며 병합하지 않았다. DB/감상/삭제/숨김·종료 계약과 항목 식별자는 유지했다. 실 NAS/RaiDrive 및 실제 Explorer 창 표시는 미검증이며 T18A-6·배포는 시작하지 않았다.
