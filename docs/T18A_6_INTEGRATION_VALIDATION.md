# T18A-6 통합 검증 및 실사용 점검표

## 기준과 결론

- 시작 main: `70661377f9460df0984d22dd2729e1b3612da21e`. PR #23~27의 merge `08adddc` / `d2848f5` / `7437e9d` / `863c86d` / `0629106`이 모두 조상임을 Git으로 확인했다.
- 작업: `task/t18a-6-integration`, PR #28. D10~D16과 DATA_AND_RANDOM_POLICY T18A §1~8을 유지한다. 제품 코드·DB 스키마·패키지·공통 API·삭제/기록 정책 변경은 없다.
- 최초 Windows 통합 검증 코드/테스트/CI SHA: `4bd5f57f983118a10607894d8a9e04841fd60a3c`. 이 커밋의 `src` tree는 시작 main과 동일한 `ad7ffd93ae3e72c0f820e0ae8147ff9f2a88fb1f`다. T18A-5 검증 SHA `f509d2b`부터 시작 main까지 `src/`, `tests/` diff는 0이다. 반복 스캔 검사의 후속 보완 SHA와 Windows 결과는 아래 별도 절을 따른다.
- 현재 작업 환경은 Linux x64이며 .NET SDK/Windows GUI/사용자 장비 접근이 없다. Windows 검증은 아래 GitHub Actions에서 수행한다. NAS/SMB 서버·실제 매핑/RaiDrive 계정·backend/version은 **미제공/미검증**이다. Linux 실행을 Windows 성공으로 계산하지 않는다.
- 최종 자동 검증 결과: **Windows 통합·전체 영향 회귀 최초 실행 통과**. Release 빌드 경고 0·오류 0, 기본 T03/T05도 성공했다. 자동 검증과 실물 미검증 인계 및 보완 검토를 완료하고 PR #28을 e850a64로 main에 통합했다. T18 착수는 아래 후속 판정을 따르며 이번 작업에서 배포/VSR을 시작하지 않는다.

## 기존 증거 대조

2026-10-11 GitHub Actions 조회로 아래 코드 SHA별 workflow의 최종 success를 다시 확인했다. 기존 결과는 해당 단계의 증거이며 최신 통합 재검증과 구분한다. E2의 T11 첫 실패와 1회 재실행을 숨기지 않는다. 개별 개발 실패/재현 이력과 기존 수동 승인 생략은 원문 검증 문서에 보존한다.

| 근거 | 정확한 검증 코드 SHA | 환경 / 최종 자동 결과 | 원문 및 대표 run |
|---|---|---|---|
| E1 경로·저장 | `af4bd6e26b5aa91bcc5f5acbbe90ebc4c8079063` | Windows Server 2025 x64, OS 10.0.26100, SDK 10.0.401; T03/05/06/09/11 성공 | [T18A-1](T18A_1_PATH_STORAGE_VALIDATION.md), [T03 37184381582](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/37184381582) |
| E2 스캔·수명주기 최종 보완 | `ac2a4d6e8a2c0ead0fa98d4a56e98440540a51ed` | Windows Server 2025 x64, OS 10.0.26100, SDK 10.0.401; T03/05/06/09/11 성공, T11 native dialog 최초 실패→1회 재실행 | [T18A-2](T18A_2_SCAN_LIFECYCLE_VALIDATION.md), [T05 37710101585](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/37710101585), [T11 37710101599](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/37710101599) |
| E3 삭제·대량 격리 | `f4f3b29dc86313fb3612d54b4acb0e8910be1569` | Windows Server 2025 x64, OS 10.0.26100, SDK 10.0.401; T03/05/06/09/11 성공 | [T18A-3](T18A_3_NETWORK_DELETION_VALIDATION.md), [T03 37791114254](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/37791114254) |
| E4 감상·완료 위치 seek 보완 | `ccb982a497328ca61a4e30f0f981a2ed32506295` | Windows x64, OS 10.0.26100, SDK 10.0.401; T03/05/06/07/09/10/11 성공 | [T18A-4](T18A_4_NETWORK_VIEWING_VALIDATION.md), [T09 37897357023](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/37897357023), [T11 37897357075](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/37897357075) |
| E5 소스 UI·Explorer | `f509d2bc3325ab43df62c539662f74f2af647946` | Windows x64, OS 10.0.26100, SDK 10.0.401; T03/05/09/11 성공; 이후 문서 복원·merge의 코드 불변 | [T18A-5](T18A_5_SOURCE_UI_VALIDATION.md), [T05 37906240777](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/37906240777), [T11 37906240732](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/37906240732) |
| E6 최신 통합 | `4bd5f57f983118a10607894d8a9e04841fd60a3c` | 아래 Windows CI 결과 참조 | [통합 38119424164](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/38119424164) |

F=연결 근거·가용성·IO 지연/OS 결과 대역 및 오류 주입, W=Windows 로컬 실파일/SQLite/실제 WPF·libVLC·Shell API, R=실제 NAS/RaiDrive. W와 F를 함께 사용한 검사를 R로 승격하지 않는다.

| ID | 기존 증거·코드 연결 / 최신 재검사 | 구분 | 남은 실물 검증 |
|---|---|---|---|
| N01 | E1 WindowsPath/UNC 경계·키, E5 UI UNC 정규화/Explorer 단일 인수; E6 scanner unc/t18a5/t18a6 및 T17 browser | F+W | 공유 루트·한글/공백의 실제 Explorer, provider case 충돌 |
| N02 | E1 실제 v1 fixture→v2 전체 행·rollback/재시작/상위 버전 거부, 정책 독립·v1/v2 저널; E6 전체 Data와 반복 색인 ItemId | F+W | 사용자 DB **복사본**, 실사용 저널 혼재 |
| N03 | E1 binding/revision, E2 stale 관찰/Unknown 재확인, E4 late Ready, E5 명시 확인; E6 실제 Windows local proof/UI 확인 | F+W | 매핑 해제/다른 공유 재매핑, 로그온 세션/앱 재시작 |
| N04 | E2 source별 수동/예약·overflow/합침·취소 소유권·watcher backoff/재연결, E5 정책 실패 원복/완료 시각 경합; E6 lifecycle/t18a5/t18a6 | F+W | 원격 수동 소스의 자동 열거 없음 계측, 사용자 로컬 watcher 수렴 |
| N05 | E2 원격 빈/캐시·부분/권한·Missing 입력 무시, E6 lifecycle 및 scanner state/access | F+W | 실제 단절/재연결·권한/캐시가 빈 목록을 반환하는 backend |
| N06 | E4 post-Ready 파일 실패 억제·취소 제외·Forward/Seen/Pending/기록 불변, E6 Data/Video/Viewing | F+W | 원격 페이지/seek 단절, 자막 실패 분리, 수동 재확인 |
| N07 | E3 durable Prepared 전 OS 0, 검증된 alias 집합·target 원자 정리/rollback·광역/손상 격리·대량 SQL, E6 Data/T12 native | F+W | 실제 매핑+UNC 별칭 삭제와 미확인 alias 기록 보존 |
| N08 | E3 recycle capability/NotFound·응답 유실 Unknown·AppliedDeletion 멱등·재매핑 후 OS 재삭제 0, E6 Data/T12 native | F+W | 실제 backend 원격 오류/응답·명시 영구삭제; 원격 휴지통은 현재 Unknown 차단 유지 |
| N09 | E2 scan/Closing worker drain, E3 live delete drain, E4 ZIP/native/late Ready dispose, E6 Comic/Viewing | F+W | 느린 원격 open/read/seek/Stop/Dispose 중 UI/복원키 응답 |
| N10 | E2 전체 감상 lease(Empty/Active/checkpoint/Hidden/SaveFailed), E4 기존 방문 보존, E6 Viewing/Data/Scanner | F+W | 감상 중 외부 교체 후 새 스캔의 진행 정리·옛 진행 부활 없음 |
| N11 | E2 sticky Closing·모의 SessionEnding/DB drain, E3 삭제 완료 대기, E4 자막 후속 IO 차단, E6 T15/16/18A2/4 native | F+W | 실제 지연+트레이/복원키 장애+저장 실패, 실제 OS 로그오프/종료 |
| N12 | E2 메타데이터 scanner, E4 ZIP/영상/자막 소유 IO, E5 DB browser/선택/late read; E6 **본문 잠금+UI→실제 probe→정책→색인** 추가, T17 Explorer 대리자 | F+W | provider 전송량/본문 다운로드/seek/cache, 실제 Explorer·미디어/자막 |

N04의 원격 수동 소스 “자동 IO 0”은 기존 검사에서 collector/미디어 본문 호출 0을 확인한 표현이다. 실제 ScanCoordinator.Start는 Discovery를 등록하고 WindowsStorageProbe의 연결 근거 관찰을 수행할 수 있다. 이는 §2의 시작 바인딩 확인과 함께 대조했으며 모든 WNet/장치 조회까지 0이라고 주장하지 않는다. 실물 계측에서도 probe와 디렉터리 열거/본문 읽기를 구분한다.

R는 N01~N12 모두 미실시다. N02의 실제 사용자 DB, N11의 실제 사용자 로그오프 등 원격 이외 실물 잔여도 별도 미검증이다.

## 최신 통합 Windows 실행

`.github/workflows/t18a6-integration.yml`은 PR head SHA를 직접 checkout하고 HEAD 및 src/tests tree, Windows OS/architecture와 `dotnet --info`를 로그에 남긴다. 단일 job에서 아래 suite를 순차 실행하여 같은 코드의 통합 결과를 확인한다. T18A 변경이 경로/DB/스캔/감상/삭제/미디어/창 수명까지 연결되므로 T03/05/06/07/09/10/11 모두 선택했다. T06 Core/Data, T12~17은 기존 Data/Viewing 진입점에 포함된다.

| 단계 | 명령 / 범위 | 최종 결과 |
|---|---|---|
| Restore/Release | `dotnet restore RandomMultimediaManager.sln`; `dotnet build RandomMultimediaManager.sln -c Release --no-restore`; 솔루션 밖 Scanner/Video/Subtitle/Viewing도 각각 restore/Release build | 통과 |
| T03/T06 | `dotnet run --project tests/RandomMultimediaManager.Core.Tests -c Release --no-build`; Data.Tests도 같은 명령. T18A1/3/4, T12/15/17 전체 포함 | 통과 |
| T05/소스 UI | Scanner.Tests를 `-c Release --no-build -- <scenario>`로 basic/state/cancel/access/reparse/case/unc/t18a5/lifecycle/t18a6 순차 실행 | 통과 |
| T07 | Comic.Tests `-c Release --no-build` | 통과 |
| T09 | Video.Tests `-c Release --no-build` | 통과 |
| T10 | Subtitle.Tests `-c Release --no-build` | 통과 |
| T11/T12~17 | Viewing.Tests `-c Release --no-build`, 실제 WPF·로컬 Shell·키/트레이·native + 지연 오류 주입 | 통과 |
| 셸 | 기존 T03 UIAutomation 검사 그대로 빈 앱 시작→종료 버튼→정상 종료를 2회 | 통과 |

실행 로그: [run 38119424164](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/38119424164), job `114410889495`. 2026-10-11 Windows Server 2025 x64 / OS 10.0.26100 / .NET SDK 10.0.401, runtime 10.0.12. 정확한 head·src tree와 tests tree `ba2622766535cee1aee00eddea2a8e9ce48b0c72`를 로그에서 확인했다. 솔루션 및 추가 4개 프로젝트의 Release 빌드 각각 경고 0·오류 0이고 모든 단계가 최초 실행에 성공했다. 같은 head의 [T03 38119424064](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/38119424064) / [T05 38119424069](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/38119424069)도 성공했다. 이번 SHA에서는 실패/재실행 없이 완료했다.

로그에서 N12 잠금 파일 5종의 본문 read 거부와 Comic/Video 등록·확인·정책·색인 통과, 기존 N01~N11 Data/Scanner 및 native 회귀를 확인했다. 100/10,000항목·48소스의 격리 SQL은 snapshot=3, paths=2, Pump=3, manual=59, random=20으로 동일했다. 동일 완료 위치 native seek 0/다른 위치 1, ZIP 읽기 실제 drain, WPF seek/자막/Stop/Play 해제 전 IO 완료·Closing 후속 자막 차단, 셸 정상 종료 2회도 통과했다. libVLC의 CI 어댑터/thumbnail 진단과 GitHub runner의 기존 v4 action Node 20 deprecation 안내는 빌드 경고/오류와 구분한다. GPU·청취·원격 provider 통과로 해석하지 않는다.

새 `Scanner.Tests/T18A6IntegrationVerification.cs`는 Windows 로컬 임시 폴더의 ZIP/CBZ/MP4/SRT/SMI에 `FileShare.None`을 적용하고 `File.OpenRead` 실패로 fixture를 확인한다. 실제 CategoryEditorViewModel/ScanCoordinator/WindowsStorageProbe/LibraryScanner/SQLite를 연결하여 읽기 불가능한 본문과 무관하게 메타데이터 색인, 확장자 필터, ItemId 유지·완료 시각을 확인한다. 이 검사와 LibraryScanner의 FileInfo/속성 기반 코드 대조는 앱 색인 경계를 검사한다. provider가 메타데이터 조회만으로 다운로드하는 것을 막았다는 증거는 아니다. 영상/만화 재생은 이 무효 미디어 fixture로 검사하지 않고 기존 정상 native fixture suite를 재사용한다.

## 지원 한계

| 환경/기능 | 현재 확인한 범위 | 한계 |
|---|---|---|
| Windows 로컬 | CI 테스트 폴더의 경로·watcher·색인·ZIP/영상/자막·삭제·WPF 생명주기 | CI Server 환경은 모든 Windows 10/11 에디션·사용자 GPU/청취·실제 키/Explorer의 지원표가 아니다. OS 지원 확정은 T18 범위 |
| SMB UNC/매핑 | 문자열/API 계약, 연결/오류 대역·별칭 캡처/격리 | 실제 서버/인증/재매핑 미검증. Windows 연결 선행, 로그온 세션 차이·숨겨진 대상 교체·최종 검사와 IO 사이 경합 제한 |
| RaiDrive/가상 | Unknown 보수 정책·명시 확인·수동 기본·실패/캐시 보존 계약 | backend/version 미제공. 캐시·전송량·seek·placeholder/reparse/case/recycle 미검증 |
| 원격 Missing | 열거 성공/빈 캐시만으로 Missing 처리하지 않는 자동 검사 | authoritative 부재 API가 없으므로 오래된 Present가 남을 수 있음 |
| 원격 휴지통 | 모든 원격 기본값 Unknown, recycle-only 미입증 시 실행 차단 | NAS 자체 휴지통이 Windows recycle 지원을 뜻하지 않음. 실물 성공 후에도 자동으로 capability 승격하지 않음. 원격 휴지통 활성화는 별도 판단/작업 |
| 원격 영구삭제 | 사용자 명시 선택·binding/Prepared/결과 불명 격리 자동 검사 | 서버 스냅샷/휴지통까지 완전 제거 보장 없음. 실물은 복사본에서만 시험 |
| 정상 종료 | 취소 요청 뒤 실제 소유 IO/native 해제 완료를 기다림 | 종료 시간 상한 없음. 영원히 반환하지 않는 provider, 강제 Kill/전원 차단은 정상 저장 보장 밖. CI timeout은 제품의 drain 성공 기준이 아님 |

## 사용자 실사용 점검표

### 시험 준비와 결과 기록

별도 Windows 테스트 계정 또는 폐기 가능한 VM에서 검증 SHA를 checkout하고 위 restore/Release 명령으로 빌드한다. 새 배포를 만들지 않는다. 앱은 exe 폴더와 무관하게 `%LOCALAPPDATA%\RandomMultimediaManager\library.db` 및 `delete-journal`을 사용하므로 **exe만 복사하면 운영 DB가 격리되지 않는다**. 사용자 DB 시험은 앱을 정상 종료한 뒤 데이터 폴더 전체(DB/WAL/SHM/저널 포함)를 복사하여 테스트 계정에 두고 원본을 보존한다. 운영 연결이나 기존 드라이브를 재매핑하지 않는다.

테스트 폴더 예: `C:\RMM-Test\한글 공백`, `\\TEST-NAS\RMM-Test\한글 공백`, 그 공유만 연결한 미사용 드라이브 문자, RaiDrive의 별도 테스트 연결/폴더. 정상 ZIP/CBZ·작은 MP4/MKV/AVI와 SRT/SMI **새 복사본**을 준비한다. 삭제/교체/권한/재매핑은 이 복사본·테스트 공유/연결에서만 수행한다. 시스템 장애 주입 권한이 없으면 해당 행을 미실시로 기록한다.

| 기록 필드 | 현재 값 / 시험 시 채울 내용 |
|---|---|
| 앱 SHA, Windows 에디션/build/x64, SDK | 사용자 장비 미제공; 실제 실행값 기록 |
| NAS 제조사/모델/펌웨어, SMB 버전, 공유/계정/권한 | 미제공; 테스트 대상만 기록, 비밀번호/token은 기록하지 않음 |
| 매핑 드라이브 문자, 대상 UNC, 실행 계정/로그온 세션 | 미제공; 테스트 연결에서 기록 |
| RaiDrive 정확한 버전, backend 및 연결 종류, 캐시 설정/상태 | 미제공; backend마다 별도 결과 필요 |
| 미디어/자막 종류·크기, 시험 날짜 | 복사본 정보만 기록 |
| 결과 | 각 N번호·환경별 통과/실패/미실시, 실제/오류 주입 구분, 기대/실제 동작·오류·재연결/대기 시간·재현 순서 |

아래 모든 행의 사용자 실물 상태는 **미실시**다. 각 행을 로컬/SMB UNC/SMB 매핑/RaiDrive에 해당하는 범위로 나누어 기록한다.

| ID | 재현 절차 | 기대 결과 / 별도 확인 |
|---|---|---|
| N01 | 한글/공백 하위 폴더와 UNC 공유 루트 등록→수동 스캔→선택 파일/위치 열기. `..`로 공유 경계를 벗어나는 입력도 시도. 대소문자 구분 backend에서는 테스트 파일 두 이름을 구분해서 준비 | 정상 경로만 저장, 경계 탈출 거부, Explorer가 **실제 올바른 파일을 선택**. case 충돌은 조용히 병합하지 않고 반영 보류 |
| N02 | 테스트 계정의 빈 DB 시작, 가능하면 닫힌 기존 v1 DB/저널의 전체 복사본으로 시작→목록/ID/이력/진행/설정 대조→재시작. v1/v2 미해결 저널이 있는 실제 복사본이 없으면 혼재 실물은 미실시 | 기존 키/ID/기록 보존, migration/복구가 OS 삭제를 재실행하지 않음. 인위적인 운영 저널 수정 금지; 실패 rollback/상위 버전은 자동 fixture 결과 참조 |
| N03 | 테스트 공유 A를 미사용 문자로 연결→확인/색인→앱 정상 종료→A 연결 해제→같은 **테스트 문자**를 B에 연결→재시작→스캔/랜덤/수동/Back·Forward/Explorer/삭제 요청. 원래 A 복원 후 다시 확인. Unknown은 시작/관찰된 단절 뒤 확인 전후 비교 | B 차단·A의 기록 보존·새 대상 자동 수용 없음. 소스 제거 후 과거 항목도 차단. 다른 로그인 세션/관리자 실행 시 보이지 않는 매핑은 Local로 승격하지 않음 |
| N04 | 로컬 자동 감지에서 복사본 생성/이름 변경/제거→수렴 확인. 같은 분류의 원격 두 소스 중 하나는 시작 끔/Manual, 다른 하나만 명시 Scheduled→시작·유휴·숨김에서 관찰→수동 취소/설정 변경 | 로컬 rename은 새 ID, 기존 상태 승계 없음. 예약이 Manual 소스를 열거하지 않음. Process Monitor 또는 backend 관찰로 **디렉터리 열거/본문 읽기**와 binding probe를 구분; Manual은 자동 열거 0이며 필요 확인 probe까지 0이라는 뜻은 아님 |
| N05 | 기존 목록/진행/이력 있는 테스트 공유를 단절하거나 테스트 권한을 제거→수동/예약 스캔. 열거 중 단절, RaiDrive warm cache/빈 목록을 별도로 관찰→원래 연결 복원→재확인/수동 스캔 | 단절/권한/빈 캐시로 기존 Present/Missing/기록이 삭제되지 않음. 일부 성공 Present만 동일 binding에서 반영. 재연결만으로 삭제 성공/Missing 해제 추정 없음 |
| N06 | A 감상 중 B 준비를 실패시킴→다음 랜덤 1회→명시 B 재확인. 원격 ZIP 페이지 이동/영상 seek 중 테스트 연결 단절→복원. 자막만 접근 실패도 별도 시도 | 한 명령 한 후보, 자동 연쇄 추첨 없음. A Pending/cursor/진행 유지, B는 세션 일시 억제·명시 성공/새 세션 해제. Active 실패는 마지막 유효 진행 유지, 자동 새 방문/재생 없음. 자막 오류는 영상 실패와 분리 |
| N07 | 같은 테스트 복사본을 매핑과 정확한 UNC로 서로 다른 분류에 등록하고 각각 방문/진행 생성→대상 경로를 확인→명시 영구삭제. 미확인 IP/별명 별칭도 있으면 별도 상태 대조. 실패/응답 불명은 별도 복사본에서 N08 절차 | 검증된 alias 캡처 항목만 함께 Missing·기록/진행 정리, 상태는 평소 공유하지 않음. 미확인 alias 기록은 추정 삭제하지 않음. Unknown은 원격/Unknown 광역 격리 유지, 로컬 무관 범위는 보존 |
| N08 | 먼저 원격 복사본의 휴지통 선택→파일 보존 확인. 별도 복사본의 **명시 영구삭제**는 정상 성공/권한 실패/발행 중 테스트 단절로 나눠 관찰. 결과 불명 발생 시 실제 원본 테스트 경로/서버 결과를 확인하고 live IO 완료 뒤 복구 UI로 판단; 재시작 시험은 이미 안전하게 중단된 테스트 복사본/폐기 VM에서만 | 현재 원격 휴지통은 Unknown 차단, 자동 영구삭제 0. 발행 후 NotFound/응답 유실은 성공으로 추정하지 않음. live IO 동안 사용자 확인으로 격리 해제 금지. 재매핑/복구에서 새 대상 재삭제 0·반복 DB 정리는 멱등. 권한 실패가 발행 전/후인지 구분 |
| N09 | 느린 테스트 share/대용량 복사본에서 open/페이지/seek/자막 적용 중 숨김/복원→전환/닫기/종료. 응답 시간과 실제 worker/native 해제 시점을 기록 | UI/복원 경로 응답, 늦은 Ready는 새 방문이 되지 않음. 미완료 IO 중 중복 worker/재시도 없음, 실제 반환/해제 전 gate·DB 유지. provider가 끝내 반환하지 않으면 미완료로 기록 |
| N10 | Empty/Opening/Active/pause/Hidden 감상 중 테스트 파일을 외부 교체하고 갱신 신호 발생→checkpoint→감상 정상 닫기→허용된 스캔. 저장 실패는 격리된 테스트 환경에서만 재현 가능한 경우 수행 | 감상 창 전체 수명에 스캔 DB 반영 없음. 정상 Leave/해제 뒤 반영, size/time 변경에 진행 정리·옛 진행 부활 없음. SaveFailed도 예외 아님 |
| N11 | 준비/스캔/삭제 지연 중 일반 종료와 Ctrl+Shift+Q 중복 요청→Ctrl+Shift+H/트레이 복원. 별도 테스트 환경에서 트레이/키 등록 충돌+저장 실패가 재현되면 종료. 실제 Windows 로그오프도 선택 시험 | Closing 새 IO 0, 중복 종료는 동일 작업 대기, 실제 drain 뒤 DB 해제. 복원 수단 없으면 숨김 진입 거부. ExitBlocked 복원만으로 scan/open 재개 없음. 실제 OS 요청은 거부될 수 있어 앱 종료 뒤 다시 로그오프 요청; 모의 SessionEnding 성공과 구분 |
| N12 | cold/warm cache에서 **색인만** 먼저 실행하며 backend 전송량 관찰→DB 목록 검색/필터·선택→ZIP 페이지/영상 seek/자막→실제 Explorer. 느린 Explorer 확인 중 선택 변경/브라우저 닫기도 시도 | 색인은 앱의 미디어 본문 요청 없음, 메타데이터-trigger provider 다운로드는 별도 기록. 재생 시 provider가 큰 부분/전체 파일 캐시 가능. DB 필터/선택 유지, 닫기/선택 변경 뒤 늦은 실행·UI 갱신 없음 |

실제 파일이 없어진 것을 성공 응답/저널 대신 사용하는 복구 판단은 하지 않는다. 원격 휴지통의 서버 기능 조사/운영 파일 삭제·재매핑·강제 종료는 이 점검표의 통과 조건이 아니다. 환경/권한이 없거나 결과를 분간하지 못하면 해당 조건을 미실시/판정 보류로 남긴다.

## 완료·미검증·후속 판정

- 완료: 최신 통합 코드 Windows x64/.NET 10 restore·Release, T03/05/06/07/09/10/11 영향 회귀, N01~N12 증거 대조, 부족한 N12 직접 검사, 지원 한계·실사용 점검표 및 상태 문서 인계. 제품 통합 결함은 이번 검사에서 발견되지 않아 제품 수정을 하지 않았다.
- 미검증: 실제 NAS/SMB/RaiDrive의 backend/version별 연결·단절·재매핑·캐시·전송량·원격 삭제, 사용자 DB 복사본·실제 Explorer·로그오프 등 위 점검표 전부. 자동 통과로 대체하지 않는다.
- 차단: 자동 검증 측 차단은 없다. 실물 장비/계정/권한 미제공은 전체 원격 호환성 선언의 차단이다. 원격 휴지통 Unknown 차단과 정상 종료 시간 상한 없음은 현재 지원 한계다.
- T18 착수 여부: **PR #28 검토·병합을 완료했으며 T18 패키지 준비 착수가 가능하다.** 미검증의 실사용 이관 범위·지원 한계를 배포 안내에 명시한다. 실제 원격 호환성 완료를 전제로 배포 범위를 확정하면 실물 결과가 먼저 필요하다. 직접 병합·T18 배포·T19 VSR은 수행하지 않았다.

전체 NAS/RaiDrive 호환성 완료로 표시하지 않는다. T09~17의 기존 수동 미검증/승인 생략, T11 native dialog 최초 실패/재실행, AVI 음성 조사 보류를 모두 유지하며 이 작업에서 재판정하지 않는다.


## 문서 보존 검증

최신 main `7066137`의 전체 Git 원문에서 CURRENT_STATE/TASKS/DATA_AND_RANDOM_POLICY를 읽고 현재 상태 및 검증 링크만 갱신했다. 기존 T18A-1~5 검증 문서·DECISIONS·SchemaV1/V2·제품 src는 변경하지 않았다. 기존 제목/절·표·코드펜스 유지, N01~N12 증거와 점검표 각 12행, 로컬 Markdown 링크·UTF-8·EOF·diff --check를 확인했다. T12 API와 T18A §1~9가 유지되며 현재 상태와 충돌하던 §7의 PR #25/26 미통합 문구만 통합 상태로 정정했다. 출력 덤프/잘림을 파일 원문으로 사용하지 않았다. 최초 결과 확정 이후 검토 head `56eb75d`까지 원격 제출 변경은 문서 4개뿐이며 당시 검증된 src/tests/CI를 유지했다. 후속 반복 스캔 검사 보완은 아래 별도로 기록한다.


## PR #28 반복 스캔 검사 보완 — 병합 재검토

- 기준 main: `f33915d8c59e60afd78e01ea2931303c7ad049f2`. TASKS.md의 현재 우선 지시문을 기존 PR #28에 반영했다. 검토 head `56eb75dc9c7ed957316d1cb11e03529411ab0511`의 두 번째 `ScanSourceAsync` 반환값 누락과 빈 목록에 대한 `All` 참 문제를 실제 코드에서 확인했다. 최초 통합 CI `38119424164`의 성공은 보존하되 그때의 반복 스캔 검사를 성공·개수·ID 보존의 충분한 근거로 사용하지 않는다.
- 수정 범위: `T18A6IntegrationVerification.cs`의 반복 스캔 검사만 보완했다. 두 번째 결과의 `Completed`, `WarningCount == 0`, 기대 `PresentCount`(Comic 2 / Video 1), `MissingCount == 0`을 확인한다. 두 번째 목록은 한 번 읽어 같은 기대 개수와 최초 목록의 정확한 `(PathKey, ItemId)` 집합 동등성을 확인한다. 빈 목록·누락·추가·키/ID 교체는 통과하지 않는다.
- 완료 시각: 첫 성공 직후 `LastCompletedAtUtc` 존재를 확인하고, 반복 성공은 두 번째 결과로 독립 증명한 뒤 시각이 저장되어 있음을 확인한다. 시각의 엄격 증가/변경을 주장하지 않으며 해상도에 의존하는 sleep은 없다.
- 정확한 테스트 변경/Windows 검증 SHA: `387cc495f96ea28d0e1de585d55ca007b31418e9`. `src` tree는 기존과 같은 `ad7ffd93ae3e72c0f820e0ae8147ff9f2a88fb1f`, `tests` tree는 `96b80bb30c189730d29afe18b87358dacdb663be`다. 기존 workflow를 변경하지 않고 해당 PR head의 자동 실행 결과를 확인했다.
- Windows 실행 결과: **2026-10-11 최초 실행 success**. Windows Server 2025 x64 / OS 10.0.26100 / SDK 10.0.401 / runtime 10.0.12에서 정확한 head·src/tests tree를 로그로 확인했다. 솔루션 및 추가 4개 Release 빌드 각각 경고 0·오류 0, Scanner `t18a6`의 Comic/Video 반복 성공·정확한 개수/PathKey·ItemId 집합·첫 시각/확인된 반복 성공 후 시각 저장 모두 PASS. 기존 전체 영향 suite 및 셸 정상 시작/종료 2회도 성공했다. [통합 CI 38120631136](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/38120631136), job `114414440414`; Scanner 명령은 `dotnet run --project tests/RandomMultimediaManager.Scanner.Tests -c Release --no-build -- t18a6`다.
- 기존 N01~N12 증거표·최초 통합 실행·실물 미검증 점검표·지원 한계를 보존한다. 실제 NAS/RaiDrive, 기존 수동 미검증/승인 생략·T11 재실행 이력·AVI 조사 보류는 유지한다. 제품 src·DB/공통 API·정책·배포 변경, 직접 병합, 새 Task 및 T18/T19 착수는 없다.
- 같은 SHA의 [T03 38120631142](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/38120631142) / [T05 38120631131](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/38120631131)도 success. 같은 SHA 재실행이나 추가 수동 검증 요구는 없다. 이 결과 후 최종 제출은 상태/검증 문서 3개뿐이며 검증된 src/tests/CI를 유지한다. 자동 검증 보완 확인 후 e850a64로 main 통합했다. 최종 head 5dcfa6073d18ab5465843af8a8f65d3c166c4bdc를 검토했으며 제품 코드 변경은 없다.
