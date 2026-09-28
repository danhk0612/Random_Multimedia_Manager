# T17 분류별 라이브러리 브라우저

## 구현 경계

- T16 PR #18이 통합된 main `478f2f7`에서 `task/t17-library-browser` 브랜치로 진행했다. PR #19는 검토 대기이며 main 통합 전이다.
- MainWindow에서 분류별 브라우저를 열고, 기존 감상 중에도 같은 브라우저를 모달로 열 수 있다. 행은 가상화한다. 조회는 기존 파일 snapshot과 방문/진행 데이터를 이용하며 전체 디스크 재스캔이나 미디어 디코딩을 유발하지 않는다.
- 대소문자 무시 파일명 검색, 즐겨찾기/영구 랜덤 제외/감상/미감상 필터와 파일명·경로·미디어 타입·존재/누락 상태·저장 크기·최근 감상·저장 진행 위치를 제공한다. 검색·필터는 기존 상태를 변경하지 않는다. 새 메타데이터나 DB 컬럼은 추가하지 않았다.
- Explorer 동작은 기존 파일 위치의 `/select,<full-path>`를 `ArgumentList` 단일 인수로 전달해 한글과 공백을 보존한다. 라이브러리 행이 남아 있어도 현재 파일이 사라졌으면 실행하지 않는다.
- 수동 열기는 기존 ViewingWindow `Run`/`Navigate`를 거쳐 `SessionCoordinator.OpenManualAsync`에 도달한다. 비활성 분류·최근 감상·랜덤 제외는 수동 진입을 막지 않는다. 동일 ItemId no-op, cursor 뒤 삽입·Forward 유지, Resume, 정상 Leave 기록/진행 저장은 기존 T02 계약을 따른다.
- 브라우저 읽기 Task는 닫기/빠른 종료에서 기다리고 exit 요청 뒤 추가 조회를 막는다. 모달 close 이벤트는 pending read가 끝난 뒤 Dispatcher의 후속 턴에서 닫아 Closing reentrancy를 피한다. DB/공통 모델·세션/기록/삭제/숨김/종료 계약은 변경하지 않았다. Astra 검토가 필요한 공통 계약 변경은 없다.

## Windows 자동 검증

커밋 `5d4a33d84ae44fa5a134fc886097ed5b631e1295`에서 Windows Server 2025 x64 (10.0.26100), .NET SDK 10.0.401로 실행했다. 솔루션 Release 빌드 경고 0·오류 0이며 아래 네 검증 실행 모두 성공했다.

- [T11 공통 감상·T17 UI/세션·스캔/만화/영상/자막 통합 36397417863](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/36397417863)
- [T03 저장·시작/정상 종료 36397417840](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/36397417840)
- [T06 세션 36397417902](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/36397417902)
- [T09 영상 36397417777](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/36397417777)

T17 자동 검사는 분류 한정 목록과 검색/각 필터, 빈/누락 항목·정보·진행 표시, 필터 조회 뒤 저장 상태 불변, Unicode/공백 경로 인수, 인덱스 후 삭제된 경로 거부를 확인했다. WPF 자동 통합은 최근 감상·랜덤 제외 항목 수동 열기, inactive 분류, normal Leave/기록 저장, Forward 보존, 동일 ItemId no-op, browser modal 중 exit 차단/닫힘을 검사한다. 만화·영상 시나리오와 저장·삭제·생명주기 회귀는 해당 통합 테스트의 기존 fixture를 사용한다.

## 사용자 Windows 수동 확인 — 미검증

자동 테스트는 WPF 컨트롤과 Explorer 실행 인수를 확인하지만 사용자의 실제 Explorer UI를 띄우거나 제품을 직접 조작하지 않았다. 다음 항목을 확인하면 수동 완료 상태를 별도로 갱신한다.

1. MainWindow에서 분류별 목록, 파일명 검색, 즐겨찾기/제외/감상 필터와 파일 정보를 확인한다.
2. 한글/공백 파일을 Explorer에서 열어 선택되는지 확인하고, 인덱스 뒤 삭제한 파일에 대해 오류/실패 안내가 나오는지 확인한다.
3. 감상 창 안에서 최근 감상/제외 파일 및 다른 분류 파일을 열어 기존 Resume와 뒤로/앞으로 이력이 유지되는지 확인한다.
4. 목록을 읽는 중 라이브러리 대화상자를 닫거나 모두 숨김/빠른 종료를 요청할 때 멈춤·늦은 UI 갱신 없이 기존 종료/복원 동작이 유지되는지 확인한다.

T15/T16의 키보드·트레이·Explorer 재시작·순간 화면 노출/실제 청취 수동 미검증, T12의 실사용 이관, T09/T10/T11 기존 생략 범위는 별도 기준대로 유지한다.
