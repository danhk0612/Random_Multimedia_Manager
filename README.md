# Random Multimedia Manager

Windows용 로컬 만화·영상 랜덤 감상 및 정리 프로그램. 분류·스캔·자체 감상·기록·삭제·트레이가 구현되었으며 T18 검토용 Windows x64 exe/ZIP과 실행 안내를 준비했다. 공개 배포 전 라이선스·실제 PC 확인이 남아 있다.

## 시작 문서

- [프로젝트](PROJECT.md)
- [요구사항](REQUIREMENTS.md)
- [아키텍처](ARCHITECTURE.md)
- [현재 상태](CURRENT_STATE.md)
- [Task 및 다음 작업](TASKS.md)
- [AI 작업 운영](AI_WORKFLOW.md)
- [미확정 결정](docs/DECISIONS.md)

## 개발 환경과 실행

Windows x64, .NET 10 SDK. 저장소 루트에서 실행한다.

```powershell
dotnet restore RandomMultimediaManager.sln
dotnet build RandomMultimediaManager.sln -c Release --no-restore
dotnet run --project src/RandomMultimediaManager.App/RandomMultimediaManager.App.csproj
```

실행·데이터 위치·패키지 교체·지원 한계는 [사용자 안내](packaging/USER_GUIDE.md), 패키징/검증은 [T18 결과](docs/T18_DISTRIBUTION_VALIDATION.md)를 확인한다. 공개 Release는 아직 없다.
현재 환경에서의 검증 결과와 Windows 실행 확인 여부는 CURRENT_STATE.md를 확인한다.

## T03 저장 검증

```powershell
dotnet run --project tests/RandomMultimediaManager.Core.Tests -c Release
dotnet run --project tests/RandomMultimediaManager.Data.Tests -c Release
```

테스트는 별도 프레임워크 없는 실행형 검증 프로그램이며 실패하면 비정상 종료한다. `dotnet test` 대신 위 명령을 사용한다. Data.Tests는 임시 폴더의 새 DB만 사용하고 종료 시 정리한다.
앱은 시작 시 `%LOCALAPPDATA%/RandomMultimediaManager/library.db`를 초기화한다. 분류·감상 UI가 연결되어 있으며 삭제 저널은 같은 데이터 폴더의 `delete-journal`에 저장한다. PR의 T03 storage verification은 Windows에서 복원·Release 빌드·두 검증 프로그램·빈 창 실행/정상 종료/재실행을 확인한다.
