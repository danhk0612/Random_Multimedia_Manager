# 새 Work용 인수인계

## 1. 이 문서의 목적

새 ChatGPT Work/Codex 작업은 이전 대화 내용을 전제로 하지 말고 이 저장소의 문서를 기준으로 개발한다.

저장소:

`danhk0612/Random_Multimedia_Manager`

## 2. 새 Work가 가장 먼저 읽을 문서

다음 순서로 읽는다.

1. `README.md`
2. `docs/PRODUCT_SPEC.md`
3. `docs/DATA_AND_RANDOM_POLICY.md`
4. `docs/VIEWER_PLAYER_SPEC.md`
5. `docs/SHORTCUTS_AND_TRAY.md`
6. `docs/ARCHITECTURE.md`
7. `docs/ROADMAP.md`
8. `docs/DECISIONS.md`

문서 간 충돌이 있으면 사용자에게 임의로 숨기지 말고 충돌 지점을 명확히 보고한다.

## 3. 제품 핵심 의도

이 앱은 단순 랜덤 파일 런처가 아니다.

핵심은 다음 반복 흐름이다.

```text
분류된 라이브러리
→ 최근 본 항목을 피한 랜덤 선택
→ 자체 만화/영상 뷰어에서 감상
→ 즐겨찾기 / 랜덤 제외 / 기록 제외 / 삭제
→ 다음 랜덤
```

즉 `감상`과 `파일 정리`를 한 화면과 한 세션에서 처리하는 것이 제품 정체성이다.

## 4. 반드시 보존해야 할 정책

### 기록

- 파일을 여는 순간 ViewHistory를 바로 확정하지 않는다.
- 현재 감상은 Pending으로 관리한다.
- 항목을 벗어날 때 감상 기록을 확정한다.
- `이번 감상 기록 제외`는 이번 1회만 기록하지 않는다.
- 과거 ViewHistory는 지우지 않는다.
- PlaybackProgress는 ViewHistory와 별개다.

### 삭제

- 삭제 방식은 옵션화한다.
- `매번 확인 / 휴지통 / 영구 삭제`를 지원한다.
- 실제 파일 삭제 성공 전 DB에서 먼저 삭제 완료 처리하지 않는다.
- 삭제된 현재 파일에는 새 ViewHistory를 남기지 않는다.

### 랜덤

- 최근 N일 감상 항목 제외
- 현재 RandomSession에서 신규 선택 중복 방지
- 여러 Category 동시 선택 가능
- Comic + Video 혼합 선택 가능
- 이전/다음은 현재 RandomSession의 Back/Forward 순서를 유지

### Quick Hide

- 모든 앱 창 숨김
- 영상 일시정지
- 앱 오디오 즉시 음소거
- 작업표시줄에서 제거
- 설정에 따라 트레이 아이콘도 숨길 수 있음
- 트레이까지 숨길 때는 정상 동작하는 전역 복원 단축키가 반드시 있어야 함
- Quick Hide 자체로 ViewHistory를 확정하지 않음

### Fast Exit

- 확인창 없이 매우 빠르게 종료
- 현재 영상/오디오 중단
- 현재 Pending ViewHistory는 기본적으로 확정하지 않음
- 이미 진행 중인 짧은 DB transaction은 가능한 한 안전하게 완료
- 무조건적인 Process.Kill을 정상 구현으로 간주하지 않음

## 5. 초기 기술 방향

현재 기준:

- C# / .NET
- WPF
- SQLite
- SkiaSharp 계열 이미지 렌더링 검토
- .NET ZIP + 필요 시 SharpCompress
- LibVLCSharp/libVLC 영상 엔진 우선 검토
- NVIDIA RTX Video SDK는 별도 후속 단계

패키지 버전이나 SDK 최신 지원 조건은 개발 시점에 공식 자료로 다시 검증한다.

## 6. 개발 진행 방식

`docs/ROADMAP.md`의 Phase 순서를 기본으로 한다.

한 번에 모든 기능을 만들지 않는다.

권장:

```text
Phase 0
→ 빌드 가능한 기반

Phase 1
→ DB/라이브러리/랜덤 정책 완성

Phase 2
→ 만화 뷰어

Phase 3
→ 영상 플레이어
...
```

각 Phase는 실행 가능한 상태로 마무리한다.

## 7. Git 운영 권장

기본 브랜치: `main`

개발은 기능/단계 브랜치를 권장한다.

예:

```text
phase-0-bootstrap
phase-1-library-random
phase-2-comic-viewer
phase-3-video-player
phase-5-shortcuts-tray
```

작업 단위마다 의미 있는 커밋을 남긴다.

큰 변경은 PR을 통해 변경 사항과 테스트 결과를 남기는 것을 권장한다.

문서에서 정해진 제품 정책을 변경하는 구현은 코드만 바꾸지 말고 해당 문서도 같이 수정한다.

## 8. 구현 시 피할 것

- 랜덤 버튼을 누를 때마다 모든 폴더 전체 재검색
- ZIP 전체를 항상 임시 폴더로 압축 해제
- 파일 경로만을 영구 식별자로 가정
- 파일 열자마자 ViewHistory 확정
- ViewHistory와 PlaybackProgress를 하나의 개념으로 합침
- 삭제 전에 DB 항목부터 제거
- Video engine API를 모든 ViewModel/UI에 직접 퍼뜨림
- Quick Hide를 MainWindow.Hide() 한 줄로 처리
- RTX VSR을 위해 기본 플레이어 개발 전체를 지연
- 필요 이상으로 초기에 마이크로서비스/플러그인 아키텍처 도입

## 9. 테스트에서 특히 확인할 것

- 최근 감상 제외 날짜 경계
- `이번 감상 기록 제외` 후 다음 이동
- 삭제 성공/삭제 실패 차이
- Fast Exit 시 Pending 기록
- Quick Hide 후 복원 상태
- Quick Hide 중 ViewHistory 미확정
- RandomSession Back/Forward
- 여러 Category 혼합 랜덤
- 후보 0개 처리
- ZIP Natural Sort
- 깨진 파일이 다음 랜덤 흐름을 막지 않는지

## 10. 새 Work에 전달할 시작 메시지 예시

아래 정도로 새 Work를 시작하면 된다.

```text
GitHub 저장소 danhk0612/Random_Multimedia_Manager를 기준으로 개발해.
먼저 README.md와 docs 아래의 문서를 모두 읽고 현재 사양과 로드맵을 파악해.
특히 docs/WORK_HANDOFF.md와 docs/DECISIONS.md를 반드시 확인해.
그 다음 docs/ROADMAP.md의 Phase 0부터 진행해.
기존 문서에 없는 제품 정책을 임의로 바꾸지 말고, 구현상 변경이 필요하면 이유와 영향을 먼저 설명해.
각 단계는 빌드/테스트 가능한 상태로 저장소에 반영하고 작업 결과를 정리해.
```

## 11. 현재 저장소 상태

현재는 제품/기술 설계 문서를 먼저 정리한 초기 단계다.

애플리케이션 소스 코드는 아직 작성하지 않았으며, 다음 Work는 `Phase 0 - 저장소/프로젝트 기반`부터 시작하면 된다.
