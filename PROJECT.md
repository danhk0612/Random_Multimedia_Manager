# Random Multimedia Manager

Windows용 로컬 만화·영상 랜덤 감상 및 정리 프로그램이다.
분류된 라이브러리 → 랜덤 선택 → 자체 뷰어/플레이어 → 즐겨찾기·삭제·이번 기록 제외 → 기록 반영 → 다음 항목의 흐름을 제공한다.
메인 화면은 분류 선택과 랜덤 보기를 중심으로 단순하게 유지한다.

## 기준과 범위

- 저장소: https://github.com/danhk0612/Random_Multimedia_Manager
- 이전 대화를 전제로 하지 않는다. 현재 사용자 지시 → 실제 코드/Git 상태 → 최신 기준 문서 → Task → 과거 기억 순으로 판단한다.
- 확정 요구사항은 REQUIREMENTS.md, 설계는 ARCHITECTURE.md, 구현 상태는 CURRENT_STATE.md, 작업은 TASKS.md, 운영은 AI_WORKFLOW.md를 따른다.
- docs/의 세부 사양은 관련 작업에서만 읽는다. 미확정 결정의 단일 목록은 docs/DECISIONS.md다.
- 초기 준비는 개발 기반까지다. 모든 기능을 구현한 제품이 아니다.
- C#/.NET/WPF, SQLite, SkiaSharp 계열, LibVLCSharp/libVLC 방향을 유지한다. VSR은 후속 검증 단계다.
- AI 업스케일, 고급 태그/중복 분석/통계, 서버·스트리밍·클라우드·자동 분류 AI를 임의로 추가하지 않는다.

## 기존 문서 통합

| 기존 문서 | 현재 기준 |
|---|---|
| README.md, docs/PRODUCT_SPEC.md | PROJECT.md, REQUIREMENTS.md; 상세 뷰어/데이터 사양은 기존 상세 문서 유지 |
| docs/ARCHITECTURE.md | 루트 ARCHITECTURE.md |
| docs/ROADMAP.md | TASKS.md |
| docs/WORK_HANDOFF.md | AI_WORKFLOW.md, CURRENT_STATE.md |
| docs/DECISIONS.md | 충돌 해결 및 미확정/후순위 목록으로 정리 |
| docs/DATA_AND_RANDOM_POLICY.md, docs/VIEWER_PLAYER_SPEC.md | 상세 사양 유지, 확정/설계안 구분 보강 |
| docs/SHORTCUTS_AND_TRAY.md | 최신 지시에 맞춰 미확정 정책 제거 및 재정리 |

이전 문서의 제안·옵션·후속 항목은 확정 요구사항으로 승격하지 않는다. 원문은 Git의 준비 이전 커밋 9ebda2c에서 확인할 수 있다. 중복된 과거 진행 이력은 추가하지 않는다.
