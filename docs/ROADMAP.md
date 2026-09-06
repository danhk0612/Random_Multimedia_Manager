# 개발 로드맵

## 원칙

- 한 단계에서 실제 실행 가능한 결과를 만든 뒤 다음 단계로 넘어간다.
- RTX VSR처럼 불확실성이 큰 기능 때문에 기본 뷰어/플레이어 개발을 지연시키지 않는다.
- 랜덤/기록 정책은 UI보다 먼저 테스트 가능한 Core 로직으로 만든다.
- 각 단계 완료 시 관련 문서와 실제 구현이 어긋나면 문서를 함께 갱신한다.

---

## Phase 0 - 저장소/프로젝트 기반

목표:

- .NET/WPF Solution 생성
- 기본 프로젝트 구조 생성
- Git ignore
- 기본 로깅/설정 로딩
- SQLite 연결 및 migration 기반
- 앱 실행/종료 가능

권장 구조:

```text
src/
  RandomMultimediaManager.App/
  RandomMultimediaManager.Core/
  RandomMultimediaManager.Infrastructure/

tests/
  RandomMultimediaManager.Core.Tests/
```

완료 조건:

- Clean clone 후 빌드 가능
- 빈 메인창 실행 가능
- SQLite DB 초기화 가능
- 기본 테스트 실행 가능

---

## Phase 1 - 라이브러리 / 분류 / 랜덤 Core

목표:

- Category CRUD
- CategorySource 다중 폴더
- Comic/Video 타입
- 파일 스캔
- MediaItem 인덱스
- Missing 처리
- ViewHistory
- PlaybackProgress
- RandomSession
- 랜덤 후보 계산

필수 테스트:

- 최근 N일 제외
- 기간 경계값
- 즐겨찾기 필터
- 랜덤 제외
- 여러 분류 합치기
- 현재 Session 중복 제외
- 후보 0개
- Back/Forward

완료 조건:

- UI에서 분류 생성/폴더 지정 가능
- 스캔 후 파일 수 확인 가능
- 랜덤 선택 결과를 파일명 수준에서 확인 가능
- 기록 후 같은 파일이 제외 기간 동안 후보에서 빠짐

---

## Phase 2 - 만화 뷰어

목표:

- ZIP/CBZ 열기
- 이미지 Entry 추출
- Natural Sort
- 단일 페이지
- 두 페이지
- 세로 연속 스크롤
- 읽기 방향
- 확대/축소/맞춤
- 고품질 렌더링
- 인접 페이지 prefetch/cache
- 마지막 페이지/스크롤 위치 저장

완료 조건:

- 큰 ZIP을 전체 임시 압축 해제하지 않고 감상 가능
- 페이지 이동이 자연스러움
- 확대 시 기본 WPF 단순 확대보다 품질이 나은 렌더링 경로 적용
- 마지막 위치 이어보기 가능

---

## Phase 3 - 영상 플레이어

목표:

- LibVLCSharp/libVLC 또는 검증 결과에 따른 영상 엔진 통합
- 재생/일시정지/Seek
- 볼륨/음소거
- 배속
- 전체화면
- 하드웨어 디코딩
- 오디오 트랙 선택
- 내장 자막 선택
- 외부 SRT/SMI
- 자막 자동 검색
- 마지막 재생 위치 저장

추가 권장:

- ASS/SSA/VTT
- 자막 싱크 조절

완료 조건:

- 일반 MP4/MKV 재생 가능
- SRT/SMI 한글 자막 확인
- 이어보기 가능
- 만화↔영상 타입 전환 시 동일 RandomSession 유지

---

## Phase 4 - 감상/정리 공통 UX

목표:

- 즐겨찾기
- 랜덤 제외
- 이번 감상 기록 제외
- 이전/다음 랜덤
- 파일 정보
- 탐색기에서 보기
- 삭제
- 휴지통/영구삭제/매번 선택
- Pending ViewHistory 정책

완료 조건:

다음 시나리오가 정확히 동작해야 한다.

```text
랜덤 A 열기
→ 기록 제외 ON
→ 다음 B
→ A ViewHistory 없음

B 삭제
→ 파일 실제 삭제 성공
→ B ViewHistory 새로 생성되지 않음
→ 다음 파일로 진행 가능
```

---

## Phase 5 - 단축키 / Quick Hide / 트레이

목표:

- 앱 내부 단축키
- 전역 단축키 등록
- Fast Exit
- Quick Hide/Restore
- 모든 앱 창 숨김
- 재생 pause + mute
- 숨기기 전 상태 저장/복원
- 작업표시줄 숨김
- 트레이 아이콘 유지/숨김 옵션
- 최소화/닫기 버튼 동작 옵션
- 트레이 메뉴
- Windows 시작 시 실행

필수 안전 조건:

- 복원 전역키가 정상 등록되지 않으면 Quick Hide 시 트레이까지 숨기는 옵션 금지
- Fast Exit에서 현재 Pending ViewHistory 기본 미확정
- Quick Hide 자체는 ViewHistory를 확정하지 않음

완료 조건:

- 전체화면 영상 중 전역 Quick Hide 즉시 작동
- 음성이 즉시 중단됨
- 같은 전역키 또는 트레이로 복원됨
- 복원 시 설정한 정책대로 이전 볼륨/재생 상태 회복
- Fast Exit은 확인창 없이 종료됨

---

## Phase 6 - 안정성 / 라이브러리 관리

목표:

- FileSystemWatcher + debounce
- 파일 이동/이름 변경 재식별 개선
- 깨진 ZIP 처리
- 재생 실패 처리
- 파일 삭제 실패 처리
- DB 정합성 점검
- 스캔 진행 표시/취소
- 라이브러리 검색
- 즐겨찾기/최근/미감상 필터
- 기록 초기화 관리

완료 조건:

- 외부 탐색기에서 파일 추가/삭제 후 앱 상태가 안정적으로 갱신됨
- 오류 파일 하나 때문에 랜덤 세션이 끊기지 않음

---

## Phase 7 - RTX Video Super Resolution

이 단계 시작 전 NVIDIA 공식 SDK/드라이버/지원 GPU/API 요구사항을 최신 상태로 다시 조사한다.

Spike 먼저 진행:

1. 현재 영상 출력 경로 파악
2. RTX Video SDK 최소 샘플 동작
3. 현재 VideoEngineAdapter와 결합 가능성 검증
4. 프레임 복사 비용/렌더링 성능 측정
5. 지원되지 않는 환경 fallback 검증

목표 UI:

```text
RTX Video Super Resolution
- 사용 안 함
- 자동
- 사용
```

완료 조건:

- 지원 GPU에서 실제 VSR 적용 확인
- 미지원 GPU/드라이버에서 일반 재생 정상
- VSR 초기화 실패 시 일반 영상 재생으로 fallback

---

## Phase 8 - 배포 / 사용성 마무리

목표:

- Windows x64 배포
- 설치형/Portable 결정
- 네이티브 영상 라이브러리 포함 방식 확정
- 설정/DB 백업·복구 검토
- 업데이트 방식 검토
- 라이선스/재배포 고지 정리
- 기본 단축키 최종 확정
- UI 마무리

완료 조건:

- 새 Windows 환경에서 설치/실행 가능
- 주요 네이티브 의존성 누락 없음
- 라이브러리 등록부터 랜덤 감상/삭제까지 처음 사용자 흐름 완성

---

# 후속 기능 후보

핵심 기능 안정화 후 검토:

- 만화 AI 업스케일
- 분류별 랜덤 가중치
- 분류 균등 랜덤
- 고급 통계
- 중복 파일 탐지
- 태그 시스템
- 사용자 정의 자막 프리셋
- 출력 오디오 장치 선택
- 마지막 RandomSession 자동 복원
- 이미지 폴더를 만화로 직접 등록
- RAR/CBR/7z 확대 지원

# 구현 우선순위 요약

```text
DB/랜덤 정책
→ 만화 뷰어
→ 영상 플레이어
→ 공통 감상/삭제 UX
→ 단축키/Quick Hide/트레이
→ 안정성
→ RTX VSR
→ 배포
```

RTX VSR은 중요한 목표지만 기본 프로그램 완성 이후 별도 기술 단계로 진행한다.
