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
| 분류 UI·스캔·랜덤 | 미구현 |
| 만화·SRT/SMI·이어보기 DB 연결 | 미구현 |
| 영상 엔진·WPF 검증 호스트 | T09 구현 검증 중, Windows 실행 게이트 미완료 |
| 즐겨찾기·영구 제외·이번 제외·삭제 | 미구현 |
| 단축키·트레이·빠른 숨김/종료 | 미구현 |
| VSR | 후순위, 가능성 미검증 |

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

T00/T01 완료, T02 계약 완료 및 main 통합. T03은 task/t03-sqlite-foundation에서 구현·검증 완료했으며 PR #4 main 통합 완료다.
T03이 main에 통합되어 T07(Sol), T09(Astra), T04(Sol)는 별도 브랜치에서 병렬 가능하다. 이번 작업에서 후속 Task는 시작하지 않았다.
T14 생명주기 상세(특히 숨김 상태 종료 저장 실패), T08 표시 기본값, T10 자막, T18 배포 검증은 해당 Task에 남긴다. T02 데이터 정책 자체의 추가 사용자 결정은 없다.
T01 완료 근거는 위 사용자 수동 검증 확인이다. 후속 작업은 PR #4 통합 여부와 최신 기준 문서를 확인한다.

## T03 저장 구현

- 기준 main a2ae988. T01 사용자 Windows 확인과 T02 통합 결과를 보존했다.
- Core 공통 모델, App/Data SQLite v1 초기화·설정·방문/삭제 트랜잭션, 실행형 Core.Tests/Data.Tests를 추가했다.
- 동일 VisitId의 전체 payload 검증 불일치는 사용자 승인 후 VisitCommit 검증값 테이블로 보완했다. 상세 계약은 DATA_AND_RANDOM_POLICY의 T03 보완을 따른다.
- Windows Server 2025 x64 / .NET SDK 10.0.400 GitHub Actions에서 restore·Release build 성공(경고 0, 오류 0), Core 검사 20개 및 SQLite 통합 시나리오 13개 통과. 빈 WPF 창 생성·정상 닫기·재실행을 2회 확인하고 LocalAppData DB 생성을 확인했다. 사용자 데스크톱에서 직접 관찰한 결과와 구분한다.
- 검증 코드 커밋: 791529c. [성공 실행 34137980053](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/34137980053). 이후 완료 상태 갱신은 문서만 변경한다.
- 로컬 Linux에서는 SQL 실행/외래키, 문서와 SQL 일치, 프로젝트/XAML XML, diff 공백 검사를 수행했다. C# 실행 결과는 위 Windows 러너의 실제 로그를 근거로 한다.
- Microsoft.Data.Sqlite 10.0.8, SQLitePCLRaw.bundle_e_sqlite3 2.1.13을 고정했다. 최종 복원에 NU1903 경고 없음.
- T03 완료(PR #4 main 통합 완료). 다른 Task는 진행하지 않았다.

## T09 영상 기반

- main 5291e9d에서 별도 task/t09-libvlc-integration으로 착수. T01/T02/T03 결과와 다른 Task 상태를 보존한다.
- LibVLCSharp/WPF 3.10.1, VideoLAN.LibVLC.Windows 3.0.23.1 고정. 독립 엔진·고정 HWND의 숨김/음소거 준비와 토큰 검사, 별도 WPF 검증 창을 추가했다.
- T09 미완료: 정적 검증 통과, Windows CI 확인 중. 음성·HW·중첩 UI·전체화면 실측은 미검증이며 T02 계약 성립을 확정하지 않는다.
- 실제 결과, 소유권, 검증 명령과 T10/T11 게이트는 [영상 검증 문서](docs/VIDEO_ENGINE_VALIDATION.md)에 기록한다. DB·랜덤·기록·공통 감상 조정자·외부 자막·삭제는 구현하지 않았다.
- PR #7 Draft로 준비하며 직접 병합하지 않는다.
