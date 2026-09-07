# 아키텍처

## 현재 구현

- C# / .NET 10 / WPF, Windows x64용 앱 프로젝트 하나.
- src/RandomMultimediaManager.App: App.xaml로 시작하여 MainWindow를 여는 최소 셸.
- RandomMultimediaManager.sln: 솔루션 진입점.
- 외부 패키지, DB, 뷰어, 플레이어, 정책 서비스는 아직 없다.
- 셸의 일반 창 닫기는 WPF 기본 동작이다. 트레이/빠른 종료 제품 정책의 확정이 아니다.

T02는 문서 계약만 확정했다. T03에서 순수 모델/정책 테스트를 위해 net10.0 Core와 Core.Tests를 실제 내용과 함께 추가한다. App→Core 단방향이며 SQLite/Windows/엔진 의존성은 App 내부에 둔다. 빈 Infrastructure나 역할별 인터페이스는 만들지 않는다.

## 유지할 기술 방향

| 영역 | 방향 | 도입 시점 |
|---|---|---|
| UI | WPF; 필요 화면부터 View/ViewModel 분리 | 각 UI Task |
| 데이터 | SQLite + Microsoft.Data.Sqlite 직접 SQL; user_version 순차 migration | T03 |
| 만화 | .NET ZIP, SkiaSharp 계열 | T07/T08 |
| 영상 | LibVLCSharp/libVLC | T09 |
| VSR | NVIDIA RTX Video SDK 가능성 검증 | T19 |

.NET 세대는 새 기반에 .NET 10을 선택했다. 패치 버전과 외부 패키지 버전은 도입 시 공식 지원/호환성을 확인하고 고정한다. 처음부터 DI, ORM, 별도 로깅 프레임워크, 범용 저장소 인터페이스, VSR 프레임 파이프라인을 만들지 않는다.

## 구현 시 책임 경계

아래는 역할 설계이며 지금 모두 클래스로 만들라는 지시가 아니다.

| 역할 | 소유하는 책임 | 직접 수행하지 않는 일 |
|---|---|---|
| UI/ViewModel | 분류 선택, 사용자 명령, 표시 | SQL·실제 파일 삭제·기록 확정 정책 |
| 감상 세션 조정 | 현재 항목, Pending, 이전/다음, 열기 성공/실패와 전환 | 영상 디코딩 |
| 랜덤/기록 정책 | 후보 필터, 기간 판정, 1회 기록 억제 | 창 표시·엔진 API |
| 데이터 접근 | SQLite 저장과 트랜잭션 | 삭제 성공 추정 |
| 파일/스캔 | 실제 존재 확인·스캔·삭제 결과 | 임의 감상 기록 생성 |
| 만화/영상 구현 | 리소스 열기·조작·해제, 결과 전달 | 각각 별도 랜덤/기록 정책 구현 |
| Windows 생명주기 | 트레이·키 등록·창/음소거 상태 조정 | 미확정 종료/복원 정책 선택 |

공통 명령 → 대상 숨김·음소거 준비 → 기존 방문의 진행/기록 저장 → 대상 활성화와 새 Pending 순서다. 준비/저장 실패 시 현재 방문과 cursor를 유지한다. 직렬 명령 처리와 세션/작업/방문 토큰으로 중복과 늦은 완료를 차단한다.
실제 파일 삭제와 SQLite는 하나의 원자적 작업이 아니다. T12는 durable 삭제 저널과 DB의 AppliedDeletion 멱등 표식으로 복구한다. 성공 불명은 격리하고 기록을 보존한다. 상세 상태표·스키마·메서드 계약은 docs/DATA_AND_RANDOM_POLICY.md가 단일 기준이다.

## 데이터 설계 경계

Category/Source, MediaItem의 즐겨찾기·제외·존재 상태, ViewHistory, PlaybackProgress, 현재 세션 순서를 분리한다.
T02 확정 계약은 docs/DATA_AND_RANDOM_POLICY.md에 있다. (CategoryId, PathKey) 유일성, 기본 7×24시간과 정확한 경계 허용, 방문별 Pending, 동일 경로 삭제의 모든 분류 정리가 기준이다. 기록/진행/설정은 %LOCALAPPDATA%/RandomMultimediaManager/library.db에 저장한다. 세션/Seen/Pending은 메모리만이며 복원하지 않는다. 후순위 필드는 만들지 않는다.

## 미디어와 비동기

- 압축 엔트리와 이미지 디코딩을 분리하고 현재/인접 페이지에 한정한 메모리 사용을 검증한다.
- 파일 전환 시 이전 비동기 결과가 새 화면에 반영되지 않도록 취소/소유권을 정한다.
- LibVLC 리소스 수명은 영상 구현 내부에 둔다. 공통 상태에는 필요한 명령과 결과만 노출한다.
- WPF 영상 출력·전체화면·컨트롤 중첩·음소거·해제 후 파일 삭제 가능성을 T09에서 검증한다.
- VSR 미지원/실패 시 일반 재생이 가능해야 한다. VSR용 렌더러 분리는 검증 결과 없이 선행 구현하지 않는다.

## 개발·검증 환경

Windows x64와 .NET 10 SDK에서 솔루션 빌드 및 앱 실행을 검증한다. 배포는 exe 또는 압축, 의존성 포함 우선이며 불가능하면 추가 설치/연결을 허용한다. 실제 지원할 Windows 버전은 T18에서 확정하며, 기존 Windows 10/11 가정만으로 모든 에디션 지원을 보장하지 않는다.

- [Microsoft .NET Windows 설치 및 지원표](https://learn.microsoft.com/en-us/dotnet/core/install/windows)
- [Microsoft WPF 개요](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview/)

위 공식 문서는 2026-09-07에 접근 확인했다. SDK/Windows 지원은 배포 시 재확인한다.
