# Random Multimedia Manager

Windows용 로컬 만화·영상 랜덤 감상 및 정리 프로그램. 현재는 최소 WPF 셸과 개발 기준 문서를 준비한 단계다.

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

현재 예상 동작은 제목이 있는 빈 창 실행/닫기뿐이다. 미디어 기능은 아직 없다.
현재 환경에서의 검증 결과와 Windows 실행 확인 여부는 CURRENT_STATE.md를 확인한다.
