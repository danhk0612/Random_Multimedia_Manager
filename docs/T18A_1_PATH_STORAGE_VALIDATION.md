# T18A-1 경로·저장 검증

기준 main `ae77b8b`, 작업 브랜치 `task/t18a-1-path-storage`. T18A-1만 수행한다. 코드 계약의 단일 기준은 DATA_AND_RANDOM_POLICY T18A §7이다.

## 자동 검증

`Data.Tests/T18A1Verification.cs`를 기존 Data.Tests 진입점에 연결했다. `--t18a1`로 이 범위만 실행할 수 있다.

| 범위 | 검사 |
|---|---|
| N01 | 드라이브/UNC 공유 루트, 한글·공백·슬래시·점 해소, 대소문자 키·Unicode 비병합, share/share2·A/AB 경계, 장치/ADS/상대·루트 탈출·끝 공백/마침표 거부 |
| N02 migration | 실제 SchemaV1 fixture → v2, Category/Source/Item/History/Progress/VisitCommit/AppliedDeletion/Settings 전체 행 대조, 소스 밖 루트 추출, 재시작, DDL 실패 rollback/재실행·상위 버전 무변경 거부 |
| N02 정책 | 미선택 수동, 확인된 Local 기본값·사용자 수동 선택 비덮어쓰기, 원격 Events/범위 오류 거부, 저장 실패 rollback, 기간/Resume 독립, source 삭제 시 정책 cascade·바인딩 보존 |
| N03 | WNet 실패·Fixed 단독 근거로 Local 판정 금지, 초기 원격 사용자 확인, 다른 공유 재매핑 거부, stale revision/generation 폐기, 소스 밖 과거 경로·재시작·Unknown 재연결 확인 계약 |
| 저널 | v1/v2 혼재 읽기, v1 원본 bytes 보존·OS 재실행 0, target별 키/바인딩 필수 검증, 손상 v2 전역 차단, 기존 단일 키 경로의 v2 부분 정리 차단 |

Linux x64/.NET SDK 10.0.401: N01~N03 6개 시나리오 및 T04 3개 통과. 최초 전체 Data.Tests 실행은 기존 저장 13개·T06 10개까지 통과했으나 T12의 실제 임시 파일 fixture가 POSIX 경로를 생성하여 Windows 경로 계약에서 중단됐다. 이를 제품 Windows 회귀 성공으로 표기하지 않는다.

Windows Release 및 영향을 받은 기존 자동 회귀: PR CI에서 실행하여 최종 결과를 기록한다.

## 미검증 및 후속 책임

- 실제 NAS/SMB 공유·매핑 해제/재매핑·다른 Windows 로그온 세션·RaiDrive backend/version·provider case/cache/recycle 동작: 미검증.
- 사용자 DB 복사본·실제 미디어는 사용하지 않았다. v1 fixture는 합성 데이터다.
- N03는 수집된 근거의 분류/저장/확인 계약을 오류 주입한 검사다. 실제 WNet 호출/IO generation admission·감상 Ready 폐기는 T18A-2/4의 연결과 T18A-6 실물 검증이 필요하다.
- UNC UI·원격 자동 검사·원격 삭제를 활성화하지 않는다. v2 저널은 구조를 읽되 T18A-3 원자 복구 연결 전 전역 차단 상태로 보존한다. v1 원격 광역 격리도 T18A-3 범위다.
- 기존 T09~T17 수동 미검증/승인 생략·AVI 조사 보류 상태를 바꾸지 않는다. 직접 병합/T18 배포는 하지 않는다.
