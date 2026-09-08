# T09 영상 엔진 계약 및 Windows 검증

## 상태와 적용 범위

T09 구현·Windows 자동 검증 완료, 데스크톱 실측 대기로 Task 완료 아님. 기준 main 5291e9d에서 T01 사용자 Windows 검증,
T02 계약 통합, T03 PR #4 병합을 확인했다. DB·랜덤·Pending·감상 기록·이어보기 DB,
외부 자막 로드/검색·삭제·트레이·전역 키는 구현하지 않는다.
T02 계약의 원문은 DATA_AND_RANDOM_POLICY.md §3/4/7을 그대로 따른다.

## 패키지와 공식 근거

2026-09-08 공식 패키지 페이지와 API 문서에 접근하여 확인했다.

| 계층 | 고정 버전 | 확인 범위 |
|---|---|---|
| 관리 API | LibVLCSharp 3.10.1 | net6.0/.NET Standard 자산, net10.0 호환으로 계산 |
| WPF | LibVLCSharp.WPF 3.10.1 | net6.0-windows7.0 자산, net10.0-windows 호환으로 계산 |
| Windows 네이티브 배포 | VideoLAN.LibVLC.Windows 3.0.23.1 | Windows 네이티브 번들; 로드된 엔진 버전은 검증 창/프로브 출력으로 별도 확인 |

NuGet 호환성 표는 실제 WPF/드라이버 실행 성공 보장이 아니다. App은 기존
net10.0-windows/PlatformTarget=x64를 유지한다. 4.x preview는 사용하지 않는다.
배포 OS 범위·네이티브 플러그인/라이선스 동봉 최종 확인은 T18이다.

- [관리 API 패키지](https://www.nuget.org/packages/LibVLCSharp/3.10.1)
- [WPF 패키지](https://www.nuget.org/packages/LibVLCSharp.WPF/3.10.1)
- [VideoLAN Windows 번들](https://www.nuget.org/packages/VideoLAN.LibVLC.Windows)
- [Microsoft .NET Windows 지원표](https://learn.microsoft.com/en-us/dotnet/core/install/windows)
- [VideoLAN MediaPlayer API](https://docs.videolan.me/libvlcsharp/api/LibVLCSharp.Shared.MediaPlayer.html)
- [VideoLAN 콜백/해제 주의](https://docs.videolan.me/libvlcsharp/docs/best_practices.html)
- [WPF 시작 안내와 airspace](https://github.com/videolan/libvlcsharp/blob/3.10.1/docs/getting_started.md)
- [고정 버전 WPF VideoView 소스](https://github.com/videolan/libvlcsharp/blob/3.10.1/src/LibVLCSharp.WPF/VideoView.cs)
- [3.0.23 DirectSound 출력 소스](https://github.com/videolan/vlc/blob/3.0.23/modules/audio_output/directsound.c)

마지막 소스는 GitHub 연결의 raw 읽기로 접근 확인했다. API 문서의 Mute는 활성 오디오
스트림 부재/출력 플러그인/패스스루에서 보장되지 않는다. 따라서 Play 전에 Mute만
설정하고 무음 성공으로 간주하지 않는다. DirectSound 소스의 초기 volume 상속과
버퍼 시작 전 적용을 근거로 아래 방식을 선택했으며 실측 확인은 별개다. `--mute`는 이 번들에서 알 수 없는 옵션으로 엔진 생성을 실패시켜 제거했다.
초기 볼륨 0으로 준비하고 디코딩 후 Mute를 재적용한다.

## 영상 측 구현

`App/Video/PreparedVideo` 하나가 LibVLC, Media, MediaPlayer, VideoView/HWND를 소유한다.
현재 미디어와 다음 준비 대상은 각각 독립 LibVLC 인스턴스를 갖는다. 준비 중인 엔진의
음량 설정이 현재 엔진으로 전파되지 않게 한다. 검증 창은 현재 + 후보 최대 두 개만 보유한다.
T11도 이 상한과 직렬 호출을 유지해야 하며 별도 공통 인터페이스/세션 조정자는 만들지 않았다.

- PrepareAsync 입력: VideoOperation(SessionId, OperationId, ItemId), 로컬 절대 경로,
  Core PlaybackProgress 또는 null, 하드웨어 요청 여부, 취소 토큰.
- 준비: Hidden VideoView의 HWND를 먼저 확보하고 같은 MediaPlayer에 한 번 연결한다.
  실제 Play → 유효 video output/영상 디코딩 통계 증가 확인, 오디오가 있으면 오디오
  디코딩/출력 버퍼 통계도 확인 → 숨김 상태 위치 적용 → Paused 확인을 거친다.
  파일 열림/메타데이터/Playing 이벤트 하나만으로 Ready를 반환하지 않는다.
- 출력: `--aout=directsound,none --directx-volume=0 --no-spdif --no-volume-save`.
  출력 플러그인을 임의 fallback하여 무음 조건을 바꾸지 않는다. 오디오가 있으면
  Ready 전에 Mute와 Volume=0도 확인한다. 무음 파일은 오디오 장치 존재를 요구하지 않는다.
- Prepare의 실패/취소는 자신의 객체만 해제한다. 취소 후 Ready가 도착해도 검증 창의
  operation/세션 검사에서 버리고 해제한다. Ready 핸들도 취소된 토큰으로 활성화할 수 없다.
- Activate는 정확한 작업 토큰과 새 VisitId를 받고 같은 HWND/디코더를 보이게 하며
  음량·음소거를 적용하고 pause를 해제한다. Media 재설정·파일 재열기·HWND 이전은 없다.
- 준비 후 엔진 오류는 CanActivate에서 거부한다. 활성화 이후 오류는 현재 방문 오류다.
  영상 엔진이 DB 저장 결과나 Pending을 결정하지 않는다.
- 조작/위치 API에는 VideoVisit(작업 토큰 + VisitId)을 전달한다. 다른 방문 명령은 거부한다.
  네이티브 콜백은 해당 객체의 오류 flag/최대 160개 진단 큐만 갱신하며 엔진 재진입,
  동기 Dispatcher 호출, 전역 현재 객체 변경을 하지 않는다. 위치는 250ms UI 조회이며
  DB checkpoint가 아니다.
- 하드웨어 요청은 3.10.1의 EnableHardwareDecoding(true), Windows에서는 d3d11va 옵션이다.
  성공 여부를 이 boolean으로 단정하지 않는다. 검증 창의 소프트웨어 체크는 다음 준비에서
  avcodec-hw=none을 적용하는 T09 진단용이며 제품 설정/DB 옵션을 추가하지 않는다.
  자동 HW 실패 시 일반 재생 여부와 명시적 SW 준비 결과를 각각 기록한다.
- 외부 자막 자동 감지는 엔진 옵션으로 끈다. 현재 트랙 선택만 있고 T10 검색/인코딩/
  외부 로드 및 자동 선택 정책은 없다.

## 소유권과 해제

공개 API는 소유 WPF Dispatcher에서 직렬 호출한다. StopAsync/DisposeAsync가 진행 중일 때
호출자는 해당 객체의 조작·snapshot을 호출하지 않는다. 검증 창은 busy로 차단한다.

1. 작업/방문 무효화, 후보 취소, 표시 숨김 및 음소거/볼륨 0.
2. EncounteredError 구독 해제. 이미 실행 중인 콜백은 자기 객체 flag만 건드린다.
3. 바인딩된 HWND를 유지한 채 백그라운드 스레드에서 동기 Stop 완료를 기다린다.
4. UI 스레드에서 VideoView.MediaPlayer=null, 시각 트리 제거, VideoView.Dispose로 HWND와 overlay 창 해제.
5. 로그 구독 해제 후 MediaPlayer → 소유 Media → LibVLC 순서로 백그라운드 해제.
6. 동일 DisposeAsync 재호출은 같은 완료 Task를 기다린다. 반환 뒤에만 파일 이동/삭제를 검사한다.

MediaPlayer.Media getter가 새 네이티브 참조를 만든다는 공식 주의를 따라 진행 조회에는
해당 getter를 사용하지 않는다. 앱이 별도로 소유한 Media 참조만 해제한다.
Stop은 같은 방문을 유지한다. 검증 창 닫기와 메인 창 닫기는 작업/해제를 기다리고,
강제 Kill이나 시간 초과 후 강제 네이티브 파괴를 추가하지 않는다.
이 대기는 검증 창의 리소스 수명이며 T14 제품 종료 정책을 구현한 것이 아니다.

## Windows 실행 방법

```powershell
git switch task/t09-libvlc-integration
dotnet restore RandomMultimediaManager.sln
dotnet build RandomMultimediaManager.sln -c Release
dotnet run --project tests/RandomMultimediaManager.Video.Tests -c Release
dotnet run --project src/RandomMultimediaManager.App -c Release
```

메인 창의 `영상 기반 검증`을 연다. `파일 준비` 후 Ready에서 화면/소리가 없는 상태를
관찰하고 `준비 대상 활성화`로 재생한다. A가 활성인 동안 B 준비/취소/손상 파일 준비를
시도한다. 두 파일에는 쉽게 구분되는 화면과 음성을 사용한다. 검증은 별도 복사본으로 한다.

오디오 포함 실파일에 자동 프로브를 추가로 실행하려면:

```powershell
dotnet run --project tests/RandomMultimediaManager.Video.Tests -c Release -- 'D:\Test\sample.mp4' 'D:\Test\sample.mkv'
```

프로브는 원본을 새 고유 임시 디렉터리에 복사한다. exclusive open, 이동, 삭제는 이
복사본에만 수행한다. 원본에 File.Delete/Move를 호출하지 않는다. 기본 CI fixture는
직접 생성한 6초 H.264 무음 MP4/MKV이며 오디오/자막/GPU 검증을 대신하지 않는다.

## 완료 게이트와 실제 결과

검증 코드 커밋: `98d8c28ff149323d18471bb8ea8fa21471047f89`.
Windows Server 2025 x64 (10.0.26100), .NET SDK 10.0.400에서 실행했다.
로드된 런타임은 **libVLC 3.0.23 Vetinari**다.
[영상 프로브 성공](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/34211175806),
[빌드·저장·셸 회귀 성공](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/34211175847).
이후 상태 문서 갱신은 코드 검증 결과를 바꾸지 않는다.

| 항목 | 실제 상태 |
|---|---|
| 로컬 XML/XAML·csproj 파싱 및 diff 공백 | 통과 |
| 로컬 C# 빌드/실행 | Linux, dotnet 미설치로 미수행 |
| Windows x64 빌드·기존 저장/셸 회귀 | 통과: SDK 10.0.400, 경고/오류 0, Core 20개·SQLite 13개·T04 3개·창 실행/종료 2회 |
| 합성 무음 MP4/MKV 디코딩·수명 프로브 | 통과: 각각 3회, Ready·동시 보유·실패/취소·토큰 거부·정지 후 재생·반복 해제·복사본 exclusive open/이동/삭제 |
| 음성 포함 MP4/MKV 실파일 재생·조작·트랙 선택 | 미검증 |
| 후보 화면/음성 누출 없음, 기존 영상 음성/화면 보존 | 데스크톱 미검증 |
| 실제 재생 위치/배속, 정지 후 재생 | 데스크톱 미검증 |
| WPF VideoView 내부 컨트롤 중첩·전체화면/Esc·모니터 DPI | 데스크톱 미검증 |
| HW 사용 실측·HW 실패/미지원 시 SW 일반 재생 | 미검증 |
| 음성 파일 반복 열기/닫기·늦은 콜백·파일 잠금 해제 | 데스크톱 미검증 |
| T02 계약 성립 | 미확정; T09 완료 및 T11 통합 진행의 게이트 유지 |

준비 타임아웃/Hidden HWND 실패/음성 누출이 발생하면 파일 코덱·길이, OS/SDK,
GPU/드라이버, libVLC 런타임 버전, 후보/현재 토큰, 엔진 로그, 재현 순서를 기록한다.
실측 실패를 Ready 정의 완화로 해결하지 않는다. 특히 아래 경우 T11 전에 구조 판단이 필요하다.

- Hidden HWND에서 decode 진행 불가: 별도 비표시 네이티브 호스트의 수명/활성 이동 검증,
  또는 libVLC 버전/출력 경로 재검증이 대안이다. 적용에는 추가 구조 검토가 필요하다.
- 음성 초기화가 현재 영상에 영향을 주거나 음성 누출: 출력 모듈/장치별 초기 무음 격리를
  검증한다. 음성을 끄고 Ready 뒤 처음 디코딩하는 것으로 몰래 바꾸지 않는다.
- Ready 유지 중/활성 직전 오류: 저장과 활성 경계의 재검증/복구를 T11에서 설계한다.
  기존 미디어를 먼저 파괴하고 새 파일을 여는 대안은 허용하지 않는다.

## T10/T11 및 병합 인계

T10: 외부 SRT/SMI 검색/인코딩/로드 API 및 선택 정책을 추가해야 한다. 내장 트랙 선택과
별개이며 자막 실패는 영상 Ready 실패로 합치지 않는다.

T11: 기존 저장 성공 이후에만 Activate 호출, 현재 + 후보 상한, 세션/명령 Busy,
VisitId 생성·Pending·기록 정책, 최종 진행 저장, 취소 세대 무효화를 연결한다.
검증 창의 임시 GUID와 바로 활성화 순서를 제품 감상 흐름으로 사용하지 않는다.
RestorePosition의 0/중간/길이 경계/길이 미확정·탐색 불가/실패 fallback 실측도 필요하다.
현재는 준비 도중 Ended/탐색 후 디코딩 대기 실패를 Failed로 반환한다. 길이 경계에서
Ready가 성립하는지와 탐색 실패 시 처음부터 복귀는 아직 보장하지 않으므로
DB 이어보기에 연결하지 않는다. 이 경계도 T09의 미완료 항목이며 T11에서 묵인하지 않는다.
공통 만화/영상 인터페이스와 DB 이어보기 연결은 아직 없다.

공유 파일은 MainWindow.xaml의 진입 버튼, MainWindow.xaml.cs의 검증 창 소유/해제 대기,
App.csproj의 패키지 3개다. 작업 중 병합된 T04 main a678c0c를 T09 브랜치에 통합했다.
분류 뷰와 상태/검증 결과를 보존하고 버튼만 상단에 배치했다. 후속 OnClosing 변경과의 통합에는 주의한다.
App.xaml/App.xaml.cs, Core, DB, T03 workflow는 변경하지 않는다. TASKS/CURRENT_STATE는
T09 부분만 반영하고 다른 Task의 최신 상태를 복원/덮어쓰기하지 않는다. PR은 Draft이며 병합하지 않는다.
