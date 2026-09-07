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
| WPF 시작/빈 메인창 코드 | 작성됨, Windows 실행 미검증 |
| 분류·스캔·SQLite·랜덤·감상 기록 | 미구현 |
| 만화·영상·SRT/SMI·이어보기 | 미구현 |
| 즐겨찾기·영구 제외·이번 제외·삭제 | 미구현 |
| 단축키·트레이·빠른 숨김/종료 | 미구현 |
| VSR | 후순위, 가능성 미검증 |

## 검증

- T01에서 PR #1이 open/미병합임을 확인하고 setup/project-foundation의 최신 커밋 fb3bad20198d0bb6545fde06216c89e7b8f0b4b0을 기준으로 관련 문서·솔루션·App 시작 코드를 재확인했다.
- README의 Windows x64/.NET 10용 명령은 `dotnet restore RandomMultimediaManager.sln`, `dotnet build RandomMultimediaManager.sln -c Release --no-restore`, `dotnet run --project src/RandomMultimediaManager.App/RandomMultimediaManager.App.csproj`이다.
- 현재 작업 실행 환경은 Windows가 아니며 외부 Git clone도 실행 환경의 네트워크 제한으로 수행할 수 없었다. 따라서 Windows x64/.NET 10에서 restore/build 및 빈 창 실행·닫기는 여전히 미검증이다.
- 프로젝트는 net10.0-windows, WPF, x64이고 App.xaml은 MainWindow.xaml을 시작하며 MainWindow는 빈 Grid인 최소 셸임을 정적 확인했다. 솔루션 프로젝트 경로도 App 프로젝트와 일치한다.
- 정적 확인은 컴파일/실행 성공을 의미하지 않는다. 실제 Windows 빌드/빈 창 실행 확인 전에는 T01을 완료 처리하지 않는다.

## T02 설계 상태

- T02 문서 계약 완료, 제품 코드/솔루션/패키지 변경 없음. 상세는 docs/DATA_AND_RANDOM_POLICY.md.
- 기준: PR #1/#2 미병합을 확인하고 docs/confirmed-policies(435170f)에서 docs/t02-data-session-contract로 분기.
- T01의 task/t01-windows-shell-validation(d11b6c0) 문서 결과를 확인해 위 검증 내용과 검증 대기 상태를 보존했다. T01 브랜치는 수정하지 않았다.
- 기존 문서의 T01 완료 전 T02 금지 및 D01/D02 사용자 재확인 문구는 최신 지시와 충돌하여 제거했다. T01에서 데이터 설계를 바꿀 기술 문제는 확인되지 않았으며 실행 가능성을 검증한 것은 아니다.
- T02 확인: 요구사항/상태표/Task 교차 검토, 문서 DDL의 임시 SQLite 제약 검사. 제품 구현 테스트나 Windows 검증을 뜻하지 않는다.

## 다음 작업과 차단

T00 완료, T01 Windows 검증 대기, T02 계약 완료 및 main 통합. 후속 코드 구현은 T01 완료 및 T02 계약 통합 전 시작하지 않는다.
다음 구현은 Astra T03. 이후 T07(Sol), T09(Astra), T04(Sol)는 별도 브랜치에서 병렬 가능하다.
T14 생명주기 상세(특히 숨김 상태 종료 저장 실패), T08 표시 기본값, T10 자막, T18 배포 검증은 해당 Task에 남긴다. T02 데이터 정책 자체의 추가 사용자 결정은 없다.
T01의 정적 검증 결과와 검증 대기 상태는 T02 문서를 통해 main에 보존했다. 남은 T03 차단 조건은 Windows x64/.NET 10에서 실제 restore·Release build·빈 창 실행·닫기 검증이다. 아래 검증 결과가 확인되기 전에는 T03을 시작하지 않는다.

## T01 재개 절차

Windows x64 PC에서 최신 main을 새로 받아 README의 명령으로 검증한다. `dotnet --info`와 `git rev-parse HEAD`로 SDK/OS 및 대상 커밋을 함께 남긴다.

```powershell
dotnet restore RandomMultimediaManager.sln
dotnet build RandomMultimediaManager.sln -c Release --no-restore
dotnet run --project src/RandomMultimediaManager.App/RandomMultimediaManager.App.csproj -c Release --no-build
```

앞 명령이 실패하면 다음 단계로 진행하지 않는다. 제목이 Random Multimedia Manager인 빈 창이 나타나고, X로 닫으면 오류 없이 프로세스가 종료되어 터미널로 돌아오는지 확인한다.
사용자가 제공한 실제 로그/창 확인 결과 또는 Windows 실행 환경의 검증 증거를 확인한 뒤 T01을 완료 처리하고 이 문서와 TASKS.md에 대상 커밋·검증 환경·결과를 반영한다. 이후 최신 main에서 T03을 재개한다.
