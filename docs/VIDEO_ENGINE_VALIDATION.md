# T09 영상 엔진 계약 및 Windows 검증

## 상태와 적용 범위

T09 완료: 자동·사용자 검증 결과를 확인했고 잔여 수동 검증은 사용자 승인으로 생략했다. PR #8의 main 통합은 별도다.
현재 동작은 사용자 승인 보완 절, 최종 완료/검증 범위는 문서 마지막 절을 따른다. 앞선 미완료·차단·미검증 표는 당시 진행 기록이며 최신 결과와 완료 승인으로 대체된다. 기준 main 5291e9d에서 T01 사용자 Windows 검증,
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
| 음성 포함 MP4/MKV 실파일 재생·조작·트랙 선택 | 2026-09-09 프로브 각 3회와 UI 영상/음성 사용자 확인; 전체 조작·트랙 선택은 미확인 |
| 후보 화면/음성 누출 없음, 기존 영상 음성/화면 보존 | 데스크톱 미검증 |
| 실제 재생 위치/배속, 정지 후 재생 | 데스크톱 미검증 |
| WPF VideoView 내부 컨트롤 중첩·전체화면/Esc·모니터 DPI | 데스크톱 미검증 |
| HW 사용 실측·HW 실패/미지원 시 SW 일반 재생 | 미검증 |
| 음성 파일 반복 열기/닫기·늦은 콜백·파일 잠금 해제 | 사용자 실파일 프로브 각 3회 통과; UI 응답 없음 원인 검증은 남음 |
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
T09 부분만 반영하고 다른 Task의 최신 상태를 복원/덮어쓰기하지 않는다. PR #7과 후속 PR #8은 main에 통합되었다. 최종 완료·검증 생략 범위는 이 문서의 완료 기록을 따른다.


## 2026-09-09 사용자 실파일 결과와 후속 보완

사용자 제공 콘솔 로그에서 음성 포함 H.264 MP4/MKV 각각 3회 Ready와 모든 프로브 완료,
오디오 디코딩, 취소/실패/토큰/동시 보유, 볼륨 roundtrip, 복사본 exclusive open/이동/삭제를 확인했다.
사용자는 UI 일반 및 명시적 소프트웨어 설정에서 영상과 소리 재생을 확인했다.
사용자 로컬 HEAD/SDK/GPU는 미제공이며 에이전트 직접 실측으로 기록하지 않는다.
프로브는 의도적으로 음소거 활성화하므로 자동 검사 중 소리가 없는 것은 정상이다.
사운드 장치 부재 시 오류와 프로브 창 응답 없음도 사용자 보고로 보존한다.

후속 task/t09-readiness-fixes / PR #8은 main e3b7e77에서 분기했다.
대용량 File.Copy와 임시 디렉터리 정리를 백그라운드로 옮기고 복사/반복 시작 시각을 출력한다.
이는 확인된 UI 스레드 I/O를 제거하며 모든 응답 없음 원인의 해결을 보장하지 않는다.
실패 시 Stop 이전의 준비 단계, 상태, 위치/길이, 탐색 가능 여부, 영상/음성 디코딩 및
오디오 출력 버퍼 통계와 마지막 로그를 보존한다. 오디오 버퍼가 없는 경우 장치 확인 안내를 붙인다.
장치 목록이 비었다는 이유로 무음 파일처럼 취급하거나 출력 준비 검사를 생략하지 않는다.
[공식 AudioOutputDeviceEnum 문서](https://docs.videolan.me/libvlcsharp/api/LibVLCSharp.Shared.MediaPlayer.html#LibVLCSharp_Shared_MediaPlayer_AudioOutputDeviceEnum)는
목록이 비어 있어도 출력 가능할 수 있다고 명시한다(2026-09-09 재확인).

복원은 탐색 가능 상태를 기다린 후 clamp하고 새 디코딩과 목표 시간의 1초 이내 도달을 확인한다.
비경계 탐색 대기 실패에는 아직 숨김/음소거인 후보만 Stop→Play하여 처음부터 준비를 다시 확인한다.
취소/엔진 오류에는 이 fallback을 수행하지 않으며 현재 미디어는 건드리지 않는다.
정확한 길이 끝에서 실패하면 명시적 계약 차단 오류를 반환한다. 끝부분 자동 초기화는 추가하지 않는다.
0/1/2초 위치 검사는 프로브에 포함한다. 비탐색 입력과 탐색 실패 fallback의 실측은 별도로 남아 있다.

남은 완료 조건:
- 장치 없는 음성 파일의 진단 재수집. 영상만 재생하는 모드로 성공 처리하려면 준비된 오디오의 의미와
  장치 재연결 시 활성 방문 처리까지 계약을 명시해야 한다. 임의 오디오 비활성화로 Ready를 반환하지 않는다.
- 길이 끝 복원의 실제 재현과 처리 확정: 종료 상태를 준비 결과로 허용하는 계약 보완 또는 같은 디코더에서
  끝 프레임을 유지하고 재열기 없이 활성화하는 경로 검증이 필요하다. 실패를 처음부터 성공으로 숨기지 않는다.
- 실제 화면/음성 누출, 현재가 재생 중인 상태에서 후보 실패/취소 보존, 내장 트랙 선택,
  WPF 중첩/전체화면/Esc/DPI, 실제 GPU 사용과 HW 실패 fallback, 수정 후 UI 응답성 재검증.

T09 미완료와 T10/T11 선행 게이트를 유지한다. 패키지 버전, MainWindow/App 시작/프로젝트,
DB 및 다른 Task는 변경하지 않는다.

### 후속 Windows 자동 검증 결과

코드 fe9cdf32482161e99ab171332427614e20c9f91a에서
[영상 검증 34298340730](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/34298340730) 및
[기존 저장/셸 회귀 34298340739](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/34298340739)가 성공했다.
0/1/2초 복원 확인은 MP4/MKV에서 6회 모두 통과했다. 첫 수정 코드 cb88fdd에서도
빌드 경고/오류 0과 같은 위치/수명 검사가 통과했다(34298155566).

끝 위치는 PASS assertion과 별도의 END-BOUNDARY 진단이며 workflow success를 끝 위치 통과로 해석하지 않는다.
합성 6초 MP4: duration=6000, Failed, stage=pause confirmation, state=Ended, time=6000,
length=6000, seekable=True, canPause=True, vout=0, decodedVideo=72.
즉 파일 핸들/디코딩 성공 후에도 Paused 준비 핸들을 유지하지 못했다.
합성 6초 MKV는 같은 실행에서 Ready였으나 끝 지점 활성화·표시까지 검증한 결과는 아니다.
포맷/타이밍 차이가 있으므로 정확한 끝 복원 계약 전체 성립을 보장하지 않는다.
T11에는 이 차단 결과를 인계한다. 끝을 임의로 앞당기거나 처음으로 되돌리는 방식은 적용하지 않았다.

검토할 명시적 계약 보완안은 장치가 없는 경우의 영상 전용 준비 결과와 영상 끝 위치의 종료 상태 복원이다.
두 안 모두 현재 Ready 성공 의미를 바꾸므로 자동 적용하지 않았다. 장치가 다시 생길 때의 자동 소리 재개도
이번 수정에 포함하지 않는다. 사용자 확인 또는 동등 계약을 유지하는 추가 엔진 검증 전에는 T09 완료가 아니다.


## 사용자 승인 후 현재 계약과 구현 (2026-09-09)

두 예외를 사용자가 승인해 DATA_AND_RANDOM_POLICY §3/4와 DECISIONS에 명시했다.
WindowsAudioEndpoint는 기본 render/multimedia 장치를 COM GetDefaultAudioEndpoint로 조회한다.
E_NOTFOUND만 장치 부재로 판정하며 다른 HRESULT는 실패로 전달한다. endpoint 포인터와
COM enumerator는 생성한 작업 스레드에서 해제한다.
[Microsoft 공식 반환값/소유권](https://learn.microsoft.com/en-us/windows/win32/api/mmdeviceapi/nf-mmdeviceapi-immdeviceenumerator-getdefaultaudioendpoint)을 2026-09-09 확인했다.

장치가 없으면 해당 LibVLC 인스턴스는 --no-audio로 생성되어 영상 전용 Ready가 된다.
영상 디코딩과 Paused 확인은 그대로 필요하다. mute/volume/트랙 명령으로 오디오를 다시 켤 수 없고,
장치 연결 후 다시 열어야 한다. UI는 이 안내를 활성 미디어 수명 동안 표시한다.
장치가 있는 경우 기존 오디오 디코딩/출력 버퍼와 무음 준비 확인을 생략하지 않는다.
장치가 준비 도중 사라지는 race나 일반 출력 실패를 영상 전용 성공으로 바꾸지 않는다.

끝 위치는 시작 위치를 실제 디코딩·일시정지한 네이티브 객체와 별도의 완료 위치를 함께 보유한다.
Activate는 HWND를 숨긴 채 완료 상태와 duration을 노출하고 자동 재생하지 않는다.
표시는 검은 영상 영역이며 마지막 프레임 캡처/보장은 추가하지 않았다.
명시적 재생은 같은 객체/방문에서 처음부터 재생한다. 완료 상태의 pause/stop은 완료 위치를 보존한다.
다른 위치 탐색은 일시정지 상태로 이동한다. 취소/만료 작업 및 방문 토큰 검사는 그대로 적용한다.
검증 UI의 `다음 준비: 끝 위치 복원 검증`으로 duration 초과 저장 위치 clamp도 확인할 수 있다.

프로브의 END-BOUNDARY 관찰은 이제 Ready/끝 유지/자동 재생 없음/명시적 재생/만료 토큰 거부의
PASS assertion으로 대체했다. 자체 생성한 6초 파란 영상+440Hz AAC audio.mp4를 추가했다.
기본 프로브는 무음 MP4/MKV와 음성 MP4 각각 3회 실행하며 모든 활성은 계속 음소거다.
장치 부재 분기에서는 볼륨/음소거 해제/트랙 선택 후에도 영상 전용 상태와 안내가 유지되는지 확인한다.
테스트 복사본만 이동/삭제하며 사용자 원본은 변경하지 않는다.

아직 남은 사용자 Windows 확인: 수정 후 실제 장치 없음→연결→다시 열기와 소리 복구,
UI 응답성, 준비 중 화면/음성 누출과 재생 중 현재 보존, 전체 기본 조작/내장 트랙,
전체화면/Esc/중첩/DPI, 실제 GPU 사용 및 HW 실패 fallback. 이 확인 전 T09 전체 완료는 아니다.
T10 외부 자막·T11 세션/DB 인계 범위와 패키지 버전은 유지한다.


### 승인 예외의 실제 Windows 검증

코드 844d8a0d611f0789cbf007c2b5daa79f23413d98의
[Windows 영상 검사 34298998375](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/34298998375)가 통과했다.
빌드 경고/오류 0. 러너에서 실제 `AudioUnavailable=True`가 반환되었으며 무음 MP4/MKV 및
음성 AAC MP4 각각 3회 영상 전용 Ready, 토큰·취소·동시 보유·정지 후 재생·해제·복사본 잠금/이동/삭제가 통과했다.
세 파일 모두 끝 위치 Ready, 자동 재생 없이 duration/Ended 유지, pause/stop의 끝 유지,
만료 방문의 재생 거부, 명시적 재생의 처음부터 시작을 통과했다.
오디오 없는 러너의 결과는 장치 연결 후 실제 청취나 하드웨어 가속 성공 증거가 아니다.
종전 6초 MP4 끝 위치 실패는 사용자 승인 완료 상태 복원 구현으로 이 테스트에서 해소되었다.

동일 코드의 [기존 저장/셸 회귀 34298998351](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/34298998351)도 성공했다. 이후 커밋은 승인 계약과 결과 문서만 반영한다.


## 최종 완료와 검증 생략 승인

사용자는 “나머지 부분은 패스하고 T09 마무리 해”로 잔여 수동 검증 생략 및 완료 처리를 승인했다.
T09는 완료이며 PR #8 main 통합으로 T09 자체의 T10/T11 선행 차단을 해제했다.
이는 미검증을 통과로 변경하거나 모든 환경의 계약 성립을 보장하는 선언이 아니다.

| 구분 | 최종 근거/범위 |
|---|---|
| 사용자 자동 로그 | AudioUnavailable=False, 음성 MP4/MKV 각 3회, Ready·위치·토큰·취소·실패·동시 보유·끝 복원·정지 후 재생·복사본 잠금/이동/삭제, Native probe complete와 정상 종료 |
| 사용자 수동 통과 | 장치 없음/재연결, 끝 위치 복원/재생, 전체화면/중첩, 일반·소프트웨어 재생/실제 HW 확인 |
| UI 응답성 | 자동 테스트에 시간이 걸리지만 응답 없음은 발생하지 않음, 실패 없음으로 보고 |
| 승인 생략/미검증 | 준비 중 화면·음성 누출과 재생 중 현재 보존의 수동 관찰, 실패 시 UI 보존, 전체 기본 조작/내장 트랙, UI 반복·준비 중 종료, 별도 DPI, HW 실패/미지원 fallback |

사용자 로컬 HEAD/SDK/GPU 세부값은 제공되지 않았다. 첨부 로그의 자동 상태/파일 수명 검사는
실제 화면/음성 관찰을 대신하지 않는다. 위 미검증 범위는 후속 통합 시 참고할 잔여 위험이며 T09 재개를 강제하는 게이트가 아니다.
T10은 외부 SRT/SMI, T11은 저장 후 활성화와 공통 세션/DB 진행 연결을 담당한다.
승인된 영상 전용 및 완료 상태 복원, 현재+후보 상한, 토큰 무효화, HWND 유지 후 Stop/해제 순서를 보존한다.
MainWindow/App 시작/프로젝트 파일의 기존 통합 주의와 다른 Task 상태를 유지한다. 직접 병합하거나 다른 Task를 구현하지 않는다.
