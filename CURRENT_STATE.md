# 현재 구현 상태

## 기반

- 최초 조사 기준: main의 9ebda2c. 원격 브랜치는 main 하나였고 README와 docs 아래 기획 8개, 총 Markdown 9개만 있었다. 소스·솔루션·AGENTS.md는 없었다.
- 준비 작업 브랜치: setup/project-foundation. 다음 작업은 병합 전 이 브랜치, 병합 후 최신 main을 기준으로 한다. 정확한 커밋과 병합 여부는 Git에서 확인한다.
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

## 다음 작업과 차단

T00 문서/최소 구조 준비 완료. T01은 Windows x64/.NET 10 실행 환경에서 실제 restore·Release build·빈 창 실행/닫기 확인이 필요해 검증 대기 상태다. T01 완료 전 T02를 진행하지 않는다.
D01/D02의 데이터/랜덤 의미는 T02 전 사용자 확인이 필요하다. 빠른 종료·숨김·트레이·삭제 기본값은 아직 미확정이며 구현하지 않았다.
상태 변경 시 TASKS와 이 문서의 현재 사실만 갱신한다.
