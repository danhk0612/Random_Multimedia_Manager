# T18A-4 네트워크 감상 검증

기준 main `9f85fc7`, T18A-3 PR #25 merge `7437e9d`. 이 작업은 감상 후보·바인딩·지연 IO만 연결하며 T18A-5 UI/T18A-6 실물 통합·배포는 시작하지 않는다.

## 구현과 검증 경계

- `ViewingAccess`는 감상 창의 scan exclusive lease 안에서 루트 근거와 소스 접근 메타데이터만 확인한다. 후보 결정마다 바인딩 집합을 읽고 같은 루트의 접근 가능한 활성 소스가 하나 이상인 항목만 허용한다. 미확인 원격 최초 채택은 하지 않는다. Unknown은 이번 프로세스의 명시 확인을 요구하고 관찰된 전체 접근 실패 이후 같은 확인을 재사용하지 않는다.
- 실제 열기 전후 binding revision/generation과 live 삭제 격리를 검사한다. 늦은 Ready는 실제 Dispose 완료까지 소유하며 기존 cursor/Forward/Pending을 유지한다. 실패 집합은 세션 메모리이며 Seen/영구 제외/Missing/기록을 바꾸지 않는다. 명시적 열기 성공과 새 세션으로 해제한다. 한 명령 한 후보, 자동 연쇄 추첨·재연결 자동 재생 없음.
- ZIP 페이지 작업은 페이지별 중복 소유를 합치고 프리로드 수를 제한한다. 비동기 해제는 신규 읽기를 차단·취소 요청한 뒤 실제 읽기와 압축 해제를 기다린다. 현재 페이지 실패는 기존 페이지/방문을 유지한다.
- 영상 경로 메타데이터·준비 Play/복원 seek·감상 seek·자막 AddSlave·Stop/Dispose는 worker에서 실행한다. HWND 생성/연결/제거와 UI/방문 상태는 Dispatcher에서 유지한다. 영상 IO의 소유 Task가 끝나기 전에 native/임시 자막 파일을 해제하지 않는다. native timeout은 해제 성공이 아니다. 오류 후 진행은 마지막 유효 위치를 유지한다.
- 자막 검색 접근 실패를 빈 목록으로 처리하지 않으며 읽기/인코딩/트랙 적용 실패는 영상 Pending과 분리해 안내한다. Closing 이후 검색 결과의 UI 적용·후속 자막 로드를 차단한다.

## 자동 검증

Windows x64 Release 및 영향 CI 결과는 제출 후 갱신한다. 이 문서 작성 시점에는 실행 대기이며 성공으로 간주하지 않는다.

| 코드/조건 | 검사 |
|---|---|
| Data.Tests T18A4Verification | N06 소스 중첩·연결 제외·실패 집합/재확인·Back/Forward·Seen/DB 불변·새 세션/기간 차이 |
| Data.Tests T18A4Verification | N03/N09 늦은 Ready와 revision 변경 폐기, 실제 해제 전 Busy·기존 Pending/진행/기록 유지 |
| Data.Tests T18A4Verification | N11 취소 무시 probe 완료까지 명령/DB 소유 유지, Closing 후 미디어 open 0 |
| Comic.Tests T08Checks | 실제 ZIP + 결정적 페이지 지연, 중복 읽기 합침·Closing 읽기 0·실제 drain 후 파일 잠금 해제 |
| Viewing.Tests T18A4NativeVerification | 실제 libVLC/WPF + seek/자막/Stop 발행 경계 지연, UI 응답·신규 IO 거부·drain 후 HWND 제거 |
| 기존 영향 회귀 | T03/T05/T06/T07/T09/T10/T11, 삭제 격리 bulk SQL·Pending·기록·만화·영상·자막·숨김/복원·종료 |

연결 대상·가용성·준비 실패·지연은 결정적 오류 주입이다. SQLite/저널과 Windows 로컬 ZIP/영상/자막/native/WPF는 실파일/실제 API를 사용한다. 오류 주입 결과는 원격 장비 지원 증거가 아니다.

## 실물 미검증 및 보존 사항

실제 NAS SMB 매핑/UNC, RaiDrive backend/version·캐시/전송량·읽기/seek/Stop/Dispose·재매핑·권한·단절/재연결·숨김 복원 응답은 장비 접근이 없어 미검증이다. 원격 휴지통 Unknown/차단은 T18A-3 그대로다. OS가 숨기는 대상 교체와 최종 검사/실제 IO 사이 경합을 원자적으로 탐지한다는 보장은 없다. 사용자 확인/UNC/소스 정책 UI는 T18A-5에 남는다.

기존 수동 미검증·사용자 승인 생략·T11 파일 선택창 복원 최초 실패와 1회 재실행 이력·AVI 음성 조사 보류는 기존 검증 문서 그대로 유지한다. 신규 테스트 복사본만 사용하며 사용자 운영 파일을 수정하지 않는다.
