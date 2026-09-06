# 아키텍처

## 1. 목표

이 문서는 구현 방향을 고정하기 위한 초기 아키텍처다. 세부 클래스/프로젝트 구조는 실제 개발 중 조정할 수 있지만, 책임 분리는 유지한다.

## 2. 초기 기술 스택

- Language: C#
- Runtime: .NET
- UI: WPF
- Database: SQLite
- ORM: EF Core 또는 경량 직접 접근 중 구현 시 선택
- Comic rendering: SkiaSharp 계열 우선 검토
- Archive: .NET ZIP + 필요 시 SharpCompress
- Video: LibVLCSharp/libVLC 우선 검토
- Logging: Microsoft.Extensions.Logging 계열 또는 동등 구조
- Dependency Injection: 과도하지 않은 범위에서 .NET 기본 DI 사용 가능

구체적인 패키지 버전은 구현 시작 시 최신 안정성과 호환성을 확인해 확정한다.

## 3. 상위 모듈

```text
Random Multimedia Manager
│
├─ UI
│  ├─ Main Window
│  ├─ Comic Viewer
│  ├─ Video Player
│  ├─ Settings
│  ├─ Library Browser
│  └─ Dialogs / Overlays
│
├─ Application Services
│  ├─ RandomSelectionService
│  ├─ PlaybackSessionService
│  ├─ HistoryService
│  ├─ LibraryScanService
│  ├─ FileOperationService
│  ├─ ShortcutService
│  └─ TrayService
│
├─ Media
│  ├─ ComicArchiveReader
│  ├─ ImageDecode/Cache
│  ├─ VideoEngineAdapter
│  └─ SubtitleHandling
│
├─ Data
│  ├─ SQLite
│  ├─ Repositories
│  └─ Migrations
│
└─ Platform
   ├─ Windows Global Hotkeys
   ├─ Recycle Bin
   ├─ FileSystemWatcher
   ├─ Startup Registration
   └─ Window/Tray Integration
```

## 4. UI 책임

UI는 직접 랜덤 SQL이나 파일 삭제 로직을 수행하지 않는다.

예:

```text
Next Random button
→ PlaybackSessionService.MoveNextAsync()
→ RandomSelectionService (필요 시)
→ HistoryService commit current pending
→ Viewer host switches media
```

이렇게 해야 만화/영상 UI가 달라도 동일 정책을 공유할 수 있다.

## 5. RandomSelectionService

책임:

- 선택 Category 해석
- 최근 감상 제외 기간 계산
- 즐겨찾기 필터
- 랜덤 제외 필터
- Missing 제외
- 현재 Session 중복 제외
- 후보 0개 상태 반환
- 최종 항목 랜덤 선택

UI에 SQLite 쿼리 세부사항을 노출하지 않는다.

## 6. PlaybackSessionService

프로그램의 핵심 오케스트레이터.

책임:

- 현재 RandomSession
- 현재 MediaItem
- Back/Forward 인덱스
- 현재 Pending View 상태
- 다음/이전 이동 전 기록 확정
- Comic/Video 타입에 맞는 View 전환
- 삭제 성공 후 현재 항목 상태 처리
- Quick Hide/Fast Exit와 현재 감상 상태 연결

## 7. HistoryService

책임:

- ViewHistory 저장
- 마지막 감상일 조회
- 감상 횟수 조회
- PlaybackProgress 저장/조회
- 현재 1회 기록 억제 처리
- 기록 초기화

`ViewHistory`와 `PlaybackProgress`를 혼동하지 않는다.

## 8. LibraryScanService

책임:

- CategorySource 스캔
- 확장자 필터
- 신규 파일 등록
- Missing 판정
- 이동/이름 변경 후보 재식별
- 수동 재스캔
- 시작 시 빠른 검증
- FileSystemWatcher 이벤트를 직접 DB에 난사하지 않고 debounce/coalesce 후 반영

스캔은 UI 스레드를 막지 않는다.

## 9. FileOperationService

책임:

- 탐색기에서 선택
- 휴지통 이동
- 영구 삭제
- 삭제 확인 정책에 필요한 결과 모델 제공
- 파일 존재/권한 확인

중요:

```text
실제 파일 작업 성공
→ 그 다음 DB 상태 반영
```

반대 순서로 하지 않는다.

## 10. ComicArchiveReader

인터페이스 개념:

```text
Open(path)
GetPageCount()
GetPageDescriptor(index)
DecodePageAsync(index)
Close()
```

압축 엔트리 목록과 이미지 디코딩을 분리한다.

- Archive 전체 extract 금지
- Natural Sort
- 인접 페이지 비동기 prefetch
- 취소 토큰 지원

## 11. Comic Image Cache

메모리 제한이 있는 캐시.

권장:

- 현재 페이지 우선
- 인접 페이지 높은 우선순위
- LRU 또는 유사 정책
- 페이지 이동이 멀어지면 오래된 디코딩 결과 제거
- 이미지 원본 바이트와 렌더링 Bitmap을 불필요하게 중복 보관하지 않도록 검토

## 12. VideoEngineAdapter

LibVLCSharp 같은 구체 엔진을 UI 전체에 직접 노출하지 않는다.

개념 인터페이스:

```text
Open(path)
Play()
Pause()
Seek(position)
SetVolume()
SetMute()
SetRate()
GetTracks()
SelectAudioTrack()
SelectSubtitleTrack()
LoadExternalSubtitle()
Close()
```

이유:

- 영상 엔진 교체 가능성
- Quick Hide에서 공통 Mute/Pause 호출
- RTX VSR 통합 시 렌더링 경로 분리
- 테스트 가능성

## 13. RTX VSR 확장 지점

초기 버전에서 실제 SDK 연동을 완료하지 않더라도 아키텍처가 VSR 추가를 막지 않아야 한다.

권장 개념:

```text
Video Decode
→ Video Processing Pipeline
   ├─ Normal pass-through
   └─ RTX VSR processor (optional)
→ Renderer
```

실제 LibVLC 출력 방식과 NVIDIA SDK 요구사항이 맞지 않을 수 있으므로, 구현 단계에서 다음을 검증한 뒤 확정한다.

- libVLC 기본 출력에 시스템 VSR을 맡길 수 있는지
- custom video output/callback이 필요한지
- DX11/DX12/Vulkan/CUDA 중 어떤 경로가 현실적인지
- 복사 비용 및 HDR/색공간 문제

VSR 때문에 1차 플레이어 출시를 지연시키지 않는다.

## 14. ShortcutService

두 레벨을 관리한다.

- Local shortcut
- Global hotkey

책임:

- 현재 설정 읽기
- 중복 검증
- Windows 전역 HotKey 등록/해제
- 등록 실패 상태 UI 제공
- Fast Exit 이벤트
- Quick Hide/Restore 이벤트

전역 단축키 콜백에서 무거운 작업을 직접 하지 않고 앱 서비스에 명령을 전달한다.

## 15. QuickHideService 또는 WindowVisibilityCoordinator

Quick Hide는 단일 Window.Hide()로 끝내지 않는다.

책임:

- 현재 보이는 모든 앱 창 상태 캡처
- 영상 재생 여부 캡처
- 음소거 상태/볼륨 캡처
- 모든 앱 창 숨김
- Taskbar 표시 제거
- 옵션에 따른 Tray 숨김
- 복원 시 이전 상태 재적용

Quick Hide 중 새로운 설정창/대화상자가 튀어나오지 않게 상태를 중앙에서 관리한다.

## 16. TrayService

책임:

- 트레이 아이콘 생성/삭제
- 클릭/더블클릭 정책
- 메뉴 활성/비활성 상태
- Show/Hide
- Play/Pause
- Mute
- Previous/Next Random
- Favorite toggle
- Quick Hide
- Settings
- Exit

UI 상태를 자체적으로 소유하지 않고 PlaybackSessionService 상태를 구독한다.

## 17. 설정 저장

두 종류로 나눈다.

### 사용자 설정

예:

- window size
- hotkeys
- tray behavior
- delete policy
- viewer preferences
- VSR setting

JSON 파일 또는 SQLite 설정 테이블 중 하나를 사용한다.

### 라이브러리/상태 데이터

SQLite에 저장.

- categories
- sources
- media items
- history
- progress
- sessions

사용자 설정과 미디어 이력의 백업/복구 단위를 분리할 수 있게 한다.

## 18. 스레딩/비동기 원칙

UI thread에서 하면 안 되는 작업:

- 전체 라이브러리 스캔
- 큰 ZIP entry decode
- fingerprint 계산
- 장시간 파일 삭제/휴지통 작업
- 영상 메타데이터 분석

취소 가능한 작업:

- 만화 prefetch
- 라이브러리 scan
- metadata extraction

현재 미디어 변경 시 이전 미디어 관련 비동기 작업을 가능한 한 취소한다.

## 19. 로깅

최소 로그 영역:

- App startup/shutdown
- Database migration/error
- Library scan summary
- File delete/missing error
- Archive open/decode error
- Video engine error
- Subtitle error
- Global hotkey registration failure
- Quick Hide/Fast Exit state transition
- RTX VSR initialization/fallback

개인 파일 전체 경로가 로그에 과도하게 남는 것이 싫을 수 있으므로 향후 `상세 경로 로깅` 옵션을 고려한다.

## 20. 테스트 우선 영역

단위 테스트 가치가 높은 영역:

- 랜덤 후보 필터
- 기간 경계 계산
- Session Back/Forward
- Pending 기록 확정/억제
- 삭제 성공/실패 상태 변화
- Natural Sort
- Category override
- 후보 0개 fallback
- Quick Hide 상태 전이

UI 픽셀 수준 테스트보다 정책 로직 테스트를 우선한다.

## 21. 권장 Solution 구조 초안

실제 구현 시 과도한 프로젝트 분할은 피한다.

초안:

```text
src/
  RandomMultimediaManager.App/
  RandomMultimediaManager.Core/
  RandomMultimediaManager.Infrastructure/

tests/
  RandomMultimediaManager.Core.Tests/
```

작은 규모에서 시작한다면 App/Core/Infrastructure 3개 정도면 충분하다.

## 22. 배포

초기 목표:

- Windows x64
- 설치형 또는 portable 중 구현 단계에서 결정

영상 엔진/libVLC 네이티브 파일 배포 방식과 NVIDIA SDK 재배포 조건은 실제 패키징 전에 반드시 검토한다.
