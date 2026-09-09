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
| 분류/소스 폴더 UI | T04 구현·자동 검증·사용자 Windows UI 수동 검증 완료 (PR #9 main 통합 완료) |
| 스캔·랜덤 | 미구현 |
| 만화 ZIP/CBZ 페이지 읽기 기반 | T07 구현·Windows 자동 검증 완료 (PR #6 main 통합 완료) |
| 만화 표시·조작 | T08 구현·Windows 자동 검증 및 사용자 Windows 수동 검증 완료 (PR #10 main 통합 완료) |
| 만화/영상 이어보기 DB 연결 | 미구현, T11 범위 |
| 외부 SRT/SMI 자막 | T10 완료 (자동 검증 + 사용자 UI 검증, 일부 수동 항목 승인 생략, PR #11) |
| 영상 엔진·WPF 검증 호스트 | T09 완료 (잔여 수동 검증 사용자 승인 생략, PR #8 main 통합 완료) |
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

T04(PR #9), T08(PR #10), T10(PR #11)의 완료 결과를 통합한다. T09 완료·승인 생략 범위도 유지한다.

- 다음 착수: T05 라이브러리 최초/수동 스캔 — Sol. T03/T04 선행 충족.
- 이후: T05 완료·main 통합 → T06 랜덤/Pending 핵심(Astra) → T11 공통 감상 UI/진행 저장(Astra). T08/T09는 이미 완료되어 T11은 T06을 기다린다.
- 현재 남은 Task의 기존 선행 관계를 지키면 T05와 동시에 바로 시작할 독립 구현 Task는 없다. 병렬화를 위해 T06/T11 또는 T14를 앞당기거나 기능을 새로 쪼개지 않는다.
- T05 중 공통 데이터 API/스키마/정책 변경이 필요하면 Astra 구조 판단 대상으로 보고한다. 자동 감지는 T18A, 실제 삭제는 T12, 트레이/빠른 숨김·종료는 T14~T16에 남긴다.
- T09/T10의 사용자 승인 생략은 해당 기록 범위에만 적용하며 이후 검증을 생략하는 일반 승인이 아니다.

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
- PR #10은 열려 있고 mergeable이며 직접 병합하지 않았다. T08 구현과 요구된 자동·수동 검증은 완료했고 PR main 통합만 남아 있다.

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
 
