# 데이터 모델 및 랜덤/기록 정책

## 1. 목적

이 문서는 기존 상세 설계안을 보존한다. 확정 요구사항은 루트 REQUIREMENTS.md가 우선한다.
스키마와 필드, fingerprint, 분류 override, 즐겨찾기 랜덤 필터, 후보 완화, 세션 영속/복원, 기록 초기화는 설계 제안이며 T02 또는 사용자 결정 전 구현하지 않는다. 다음 영역을 검토한다.

- 파일을 어떻게 식별할지
- 랜덤 후보를 어떻게 계산할지
- 감상 기록을 언제 남길지
- 이어보기 위치와 감상 기록을 어떻게 분리할지
- 삭제/제외/즐겨찾기 상태를 어떻게 다룰지
- 현재 랜덤 세션의 이전/다음 동작을 어떻게 유지할지

## 2. 권장 데이터 모델

실제 스키마 이름은 구현 단계에서 조정할 수 있지만 의미는 유지한다.

### Category

- `Id`
- `Name`
- `MediaType` (`Comic`, `Video`)
- `IsEnabled`
- `HistoryExclusionDaysOverride` nullable
- `CreatedAt`
- `UpdatedAt`

### CategorySource

- `Id`
- `CategoryId`
- `RootPath`
- `IncludeSubdirectories`
- `IsEnabled`

하나의 Category에 여러 Source를 연결한다.

### MediaItem

- `Id`
- `CategoryId`
- `Path`
- `NormalizedPath`
- `FileName`
- `Extension`
- `FileSize`
- `LastWriteTimeUtc`
- `Fingerprint` nullable
- `IsFavorite`
- `IsRandomExcluded`
- `IsMissing`
- `CreatedAt`
- `UpdatedAt`

### ViewHistory

- `Id`
- `MediaItemId`
- `ViewedAt`
- `Source` (`Random`, `Manual`, 기타 필요 시)

랜덤 제외 기간 계산은 `MediaItemId`별 마지막 `ViewedAt`을 사용한다.

### PlaybackProgress

- `MediaItemId`
- `ComicPageIndex` nullable
- `ComicScrollPosition` nullable
- `VideoPositionMs` nullable
- `UpdatedAt`

감상 기록과 분리한다.

### RandomSession

- `Id`
- `StartedAt`
- `EndedAt` nullable
- `IsRestorable`

### RandomSessionItem

- `Id`
- `SessionId`
- `MediaItemId`
- `SequenceIndex`
- `Status` (`Available`, `Deleted`, `Missing` 등)

세션의 Back/Forward 동작을 위해 순서를 보존한다.

## 3. 파일 식별 정책

사용자 확정: 분류가 다르면 상태를 공유하지 않으며 이동 시 이전 상태를 승계하지 않는다.
T02는 분류와 정규화 경로를 기준으로 식별하는 계약을 정한다. 경로 변경 시 새 항목으로 처리하며 자동 fingerprint 병합/기록 승계를 초기 구현하지 않는다.
물리 파일 삭제 시 같은 경로를 참조하는 여러 분류의 존재 상태 및 기록 정리 범위를 T02에서 명시한다.

## 4. 랜덤 후보 계산

기본 전역 설정을 `HistoryExclusionDays`라고 하며 사용자 확정 기본값은 7일이다.

분류에 override가 있으면 해당 값을 우선한다.

후보 조건:

```text
Category selected
AND Category enabled
AND MediaItem.IsMissing = false
AND MediaItem.IsRandomExcluded = false
AND file exists
AND no disqualifying recent history
AND not newly selected in current random session
AND favorite filter matches
```

### 최근 기록 제외 판정

예:

```text
오늘: 2026-09-06
제외 기간: 30일
마지막 감상일: 2026-08-20
→ 후보 제외
```

경계일 포함 여부는 구현 전반에서 일관되게 처리한다. 권장 규칙은:

```text
LastViewedAt >= Now - ExclusionPeriod
→ 제외
```

## 5. 여러 분류 선택

여러 Category를 동시에 선택할 수 있다.

- Comic + Comic 가능
- Video + Video 가능
- Comic + Video 가능

후보 집합을 합친 뒤 전체에서 랜덤 선택한다.

초기 버전은 분류별 가중치를 두지 않는다. 따라서 파일 수가 많은 분류가 더 자주 뽑힐 수 있다.

분류별 균등 확률/가중치는 후속 기능으로 둔다.

## 6. 즐겨찾기 필터

랜덤 선택 시 세 가지 모드를 제공할 수 있다.

- `All`
- `FavoritesOnly`
- `ExcludeFavorites`

즐겨찾기 자체는 감상 기록이나 랜덤 제외 기간에 영향을 주지 않는다.

## 7. 랜덤 대상 영구 제외

`IsRandomExcluded = true`인 항목은 기간과 관계없이 랜덤 후보에서 제외한다.

- 파일은 삭제되지 않는다.
- 수동 탐색/열기는 가능하다.
- 사용자가 다시 해제할 수 있다.

## 8. 현재 세션 중복 방지

새 랜덤 후보를 뽑을 때 현재 `RandomSession`의 모든 신규 선택 이력을 제외한다.

중요:

- `이전`으로 과거 항목을 다시 보는 것은 허용
- `다음`으로 기존 Forward 항목을 다시 보는 것은 허용
- 세션 끝에서 새 항목을 뽑을 때만 중복 방지 적용

예:

```text
A → F → K
        ↑

이전 → F
다음 → K
K에서 다음 → A/F/K를 제외한 새 후보 선택
```

## 9. 후보가 0개일 때

기본 동작은 사용자에게 알린다.

선택지:

- 가장 오래 전에 본 항목부터 허용
- 제외 기간 변경
- 취소

`가장 오래 전에 본 항목부터 허용`은 랜덤 정책을 완전히 무시한다는 뜻이 아니라, 현재 조건에서 가장 오래된 마지막 감상 기록을 가진 그룹부터 후보를 완화하는 방식으로 구현하는 것을 권장한다.

설정에서 자동 완화를 선택할 수 있다.

## 10. 감상 Pending 상태

현재 파일을 열었을 때 즉시 `ViewHistory`를 생성하지 않는다.

메모리 상의 현재 컨텍스트 예:

```text
CurrentMedia
- MediaItemId
- PendingView = true
- SuppressViewHistory = false
- WasDeleted = false
```

## 11. 감상 기록 확정 시점

기본적으로 현재 항목에서 벗어나는 시점에 기록을 확정한다.

- 이전 랜덤
- 다음 랜덤
- 수동으로 다른 파일 열기
- 뷰어/플레이어 닫기
- 정상 앱 종료

다음 조건이면 기록하지 않는다.

- `SuppressViewHistory = true`
- 현재 파일이 성공적으로 삭제됨
- 파일 열기에 실패하여 실제 감상이 시작되지 않음
- 빠른 종료의 Pending 처리: 미확정 D03 결정 후 반영

## 12. 이번 감상 기록 제외

현재 항목에만 적용되는 토글이다.

```text
이번 감상 기록 제외 = ON
```

효과:

- 현재 Pending 감상 기록을 확정하지 않음
- 이전에 존재하던 과거 ViewHistory는 삭제하지 않음
- PlaybackProgress 저장 여부와는 별개

즉 `기록하지 않음`은 과거 기록 삭제가 아니라 **이번 1회 기록 억제**다.

## 13. 이어보기 위치 저장

PlaybackProgress는 ViewHistory와 독립적이다.

따라서 다음 조합이 가능하다.

```text
이번 감상 기록 제외 = ON
영상 위치 = 00:37:20 저장
```

즉 다음 랜덤 제외 기간에는 영향을 주지 않지만 나중에 수동으로 열면 이어볼 수 있다.

진행 위치까지 저장하지 않는 옵션이 필요해지면 별도 옵션으로 추가한다. 초기에는 기록 제외가 진행 위치 저장까지 막지는 않는다.

## 14. 삭제 처리

삭제 순서:

1. 사용자의 삭제 정책 확인
2. 실제 파일 삭제/휴지통 이동 시도
3. 성공 여부 확인
4. 성공 시 현재 Pending 감상 기록 억제
5. 해당 파일의 감상 기록 제거 및 DB 항목 상태 반영 (동일 물리 경로의 분류별 정리 계약은 T02에서 확정)
6. 현재 RandomSessionItem을 `Deleted` 상태로 변경

삭제 실패 시:

- MediaItem을 정상 항목으로 유지
- Pending 감상 규칙도 임의 변경하지 않음
- 오류를 표시하고 현재 화면 유지 가능

## 15. Missing 파일 처리

DB에는 있지만 실제 파일이 없으면:

- `IsMissing = true`
- 랜덤 후보에서 제외
- 재스캔에서 동일 파일을 재식별하면 복구 가능

랜덤 선택 직전에도 파일 존재 여부를 최종 확인한다.

## 16. 세션 Back/Forward 정책

현재 인덱스를 `CurrentSequenceIndex`로 관리한다.

- 이전: `index - 1`
- 다음: `index + 1`이 존재하면 기존 항목 이동
- 다음: 마지막 인덱스면 새 랜덤 항목 생성 후 append

과거로 이동한 뒤 새로운 랜덤 분기를 만드는 일반 브라우저식 Forward 삭제 동작은 초기에는 사용하지 않는다.

예:

```text
A → B → C
    ↑

다음 → C
```

항상 기존 세션 경로를 우선한다.

## 17. 세션 복원

후속 또는 초기 옵션 기능.

저장 가능 정보:

- 선택했던 Category 목록
- 랜덤 필터
- SessionItem 순서
- 마지막 CurrentSequenceIndex

복원 시 실제 파일 존재 여부를 다시 검증한다.

## 18. 기록 초기화

관리 기능 후보:

- 전체 ViewHistory 삭제
- 특정 Category의 ViewHistory 삭제
- 특정 MediaItem의 ViewHistory 삭제
- PlaybackProgress만 초기화

기록 초기화와 실제 파일 삭제는 절대 연결하지 않는다.

## 19. DB 안정성

- SQLite transaction을 사용해 관련 변경을 원자적으로 처리
- 파일 삭제와 DB 삭제는 순서를 분리하여 실제 파일 삭제 성공 전에 DB에서 먼저 제거하지 않음
- 빠른 종료의 DB transaction 처리와 종료 수준은 D03/T14에서 결정
- OS 강제 프로세스 Kill과 같은 비정상 종료 후에도 다음 시작 시 DB 및 라이브러리 정합성 점검 가능 구조로 설계
