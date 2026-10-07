# 데이터 모델 및 랜덤·감상 계약

T02 확정 계약과 T03 승인 보완. 저장 구현은 App/Data, 공통 모델은 Core에 있다. REQUIREMENTS.md의 확정 기능 안에서 정한 기본값이며, 후순위 기능은 DECISIONS.md에만 보존한다. 이 문서의 표·스키마·오류 계약을 T03 이후의 구현과 테스트 기준으로 사용한다.

## 1. 식별과 소스 관계

- ID는 앱이 생성하는 소문자 하이픈 UUID 문자열이다. Category 1:N CategorySource, Category 1:N MediaItem이다.
- 항목의 유일성은 (CategoryId, PathKey). 같은 경로를 다른 분류에 등록하면 서로 다른 ItemId이며 기록·즐겨찾기·영구 제외·진행 위치를 공유하지 않는다. 파일은 복제하지 않는다.
- 분류명은 Trim 후 1~100자, 동일 이름 허용(ID로 구분). 타입은 Comic/Video. 항목이 한 번이라도 등록된 분류는 타입 변경을 거부한다(Missing 항목 포함). 빈 분류는 변경 후 소스를 재스캔한다.
- 소스는 절대 폴더 경로, 하위 포함 기본 true, 활성 기본 true. 동일 분류의 같은 RootPathKey 재등록은 기존 소스를 반환하며 옵션을 암묵적으로 덮어쓰지 않는다. 편집 명령으로만 변경한다.
- 중첩 소스를 허용한다. 동일 분류·경로 항목을 한 번만 저장하고 후보에서도 한 번만 센다. 항목-소스 연결 테이블은 만들지 않는다. 활성 소스의 경로 포함 관계로 소속 여부를 계산한다.
- 소스 비활성/제거/경로 편집은 기록·항목을 삭제하지 않는다. 활성 소스에 더 이상 포함되지 않는 항목은 랜덤 후보에서 빠지며 수동 열기는 가능하다. 소스 제거는 물리 Missing을 뜻하지 않는다.
- 분류 활성 기본 true. 비활성 분류는 신규 랜덤에서 제외하고 수동/기존 세션 탐색은 허용한다. 현재 감상 중 분류/소스 편집은 감상을 끝내지 않는다.

### Windows 경로 계약

D13으로 네트워크 지원 목표가 추가됐다. 아래는 T02/T05 런타임의 경로 제한이며 T18A-1 공통 정규화/저장 API는 UNC도 처리한다. T18A-2는 스캔 조정자를 연결하며 UNC UI·원격 삭제/감상 연결은 후속 Task다. T18A §7의 구현 계약을 함께 따른다.

로컬 드라이브의 완전한 절대 경로만 최초 범위로 받는다. 드라이브 상대 경로(C:foo), 상대 경로, URL, UNC 네트워크 경로, 장치 경로는 등록 거부한다. 네트워크 지원 확장이 아니다.
구분자를 역슬래시로 통일하고 Windows Path.GetFullPath로 . / ..를 해소한 뒤, 루트 외 끝 구분자를 제거한다. 구성요소 끝 공백/마침표와 대체 데이터 스트림(:) 경로는 모호성을 피하려고 거부한다. 드라이브 구분용 콜론은 허용한다.
표시/열기용 Path는 대소문자를 보존하고 PathKey는 위 결과의 ToUpperInvariant 값이다. SQLite BINARY 비교를 사용한다. Unicode 정규화로 이름을 합치지 않는다. SQLite NOCASE에 Windows Unicode 비교를 맡기지 않는다.
폴더 포함은 같은 드라이브에서 경로 구성요소 단위로 판정한다(C:\A와 C:\AB를 혼동하지 않는다). 하위 미포함은 직계 파일만 포함한다.
심볼릭 링크/정션 등 reparse point 소스·항목 및 상위 경로는 따라가지 않는다. 스캔에서 건너뛰고 안내한다. 하드링크·8.3 별칭·파일 ID·내용 해시는 병합하지 않는다. 이 계약의 '같은 파일' 정리 범위는 같은 PathKey이며 별칭까지 같은 물리 실체임을 추론하지 않는다. Windows 대소문자 구분 디렉터리는 미지원으로 안내한다. T05에서 해당 검증을 구현한다.

### 이동·Missing·재등장

| 관찰 | 처리 |
|---|---|
| 이동/이름 변경으로 PathKey 변경 | 옛 항목 Missing, 새 경로 새 ItemId와 기본 상태. 상태 승계 없음 |
| 대소문자만 변경되어 PathKey 동일 | 표시 Path/메타데이터만 갱신 |
| 확실한 파일/디렉터리 없음 | 같은 PathKey의 항목을 Missing으로 표시. 기록/진행/선호 보존 |
| 접근 거부·오프라인·스캔 취소·부분 실패 | 없음으로 단정하지 않음. 이전 존재 상태 보존, 이번 열기 실패만 반환 |
| 같은 경로 재등장 | 기존 ItemId의 Missing 해제. 경로 기반 상태 보존이며 내용 동일성 판별 아님 |
| 같은 경로 내용 교체 | ItemId 유지, 크기/수정시각 변경 시 진행 위치만 제거. 기록/선호 보존 |

크기/수정시각은 내용 식별자가 아니다. 관찰하지 못한 동일 메타데이터 교체는 검출을 보장하지 않는다. 스캔 완료된 범위만 원자적으로 반영하며 취소/실패한 하위 범위를 일괄 Missing 처리하지 않는다. 파일 존재 확인은 bool File.Exists만으로 권한 오류와 부재를 합치지 않는다.

## 2. 시간·후보·세션

- 모든 시각은 UTC Unix milliseconds 정수. 표시만 현지 시각. 감상 시각 ViewedAtUtc는 **항목에서 벗어나는 전환을 저장하는 시각**이다. OpenedAtUtc는 방문 메모리 정보다.
- 전역 HistoryExclusionDays 기본 7, 정수 0~36500. D일은 D×24시간이며 달력 날짜/DST 기준이 아니다.
- 한 선택 명령에서 Now를 한 번 캡처한다. D>0이고 LastViewedAtUtc > Now-D×86400000이면 제외. 정확히 경계와 같으면 허용. 0이면 과거 기록 검사를 생략하지만 영구 제외와 세션 중복 방지는 유지한다. 미래 기록은 D>0에서 제외한다. 시계 역행 시 과거 기록을 수정하지 않는다.
- 후보는 선택된 활성 분류의 항목 중 활성 소스 포함, Missing=false, IsRandomExcluded=false, 삭제 복구 격리 아님, 현재 세션 Seen에 없음인 항목으로 위 기간 조건을 적용한다.
- 후보 ItemId별 균등 선택. 다중 분류의 같은 PathKey도 서로 다른 항목으로 각각 후보가 된다. 분류 균등·즐겨찾기 가중치/랜덤 필터·기간 override는 없다.
- 추첨 후 파일 존재와 열기를 확인한다. 실패하면 기록/Seen/경로에 추가하지 않고 현재 감상을 유지한다. 한 명령에서 다른 후보를 자동 연쇄 시도하지 않는다.
- 0개면 NoCandidates를 반환하고 현재 화면/Pending/Forward/Seen을 보존한다. 자동 완화·자동 세션 초기화 없음. 사용자는 기존 기간 설정을 바꾸거나 감상 화면을 닫고 랜덤 보기를 다시 시작할 수 있다.
- 세션 시작은 분류 선택 후 랜덤 보기 명령이다. 새 세션은 선택 분류 ID 집합을 고정한다. 진행 중 별도 랜덤 보기로 재시작할 때도 대상 열기/기존 방문 저장이 성공해야 새 세션으로 교체한다. 실패하면 이전 세션 유지.
- Seen은 성공적으로 활성화된 모든 ItemId를 포함한다(수동 열기로 경로에 들어온 항목 포함). Suppress 여부와 무관하다. Back/Forward는 Seen·기간·영구 제외·소스 활성 조건을 다시 적용하지 않고 파일 열기 가능 여부만 확인한다.
- 세션 경로는 성공한 항목 방문의 항목 ID 순서, cursor, Seen으로 구성한다. Back/Forward 재방문은 경로를 추가하지 않는다. 재방문 성공마다 **새 VisitId와 Suppress=false인 Pending**을 만든다.
- Previous는 앞쪽의 가장 가까운 유효 항목, Next는 뒤쪽 유효 Forward를 우선하고 없으면 신규 추첨이다. Missing/삭제된 슬롯만 건너뛴다. 다른 열기 실패는 자동 건너뛰지 않고 현 위치 유지. 앞쪽 유효 항목이 없으면 NoPrevious.
- 수동으로 다른 항목을 열면 현재 cursor 바로 뒤에 삽입하고 기존 Forward는 보존한다. 세션이 없으면 선택 분류 집합이 빈 탐색 세션을 만들며, 이후 신규 랜덤은 분류 선택을 요구한다. 현재와 같은 ItemId 열기 명령은 no-op(새 방문 아님).
- 삭제된 현재 슬롯은 tombstone으로 두고 현재 미디어만 비운다. 자동 다음 재생은 하지 않는다. Previous/Next는 그 cursor 기준으로 이동한다.

## 3. 감상 상태와 원자적 전환

Visit = VisitId, ItemId, OpenedAtUtc, Origin(Random/Manual/Back/Forward), SuppressHistory, 최신 Progress.
Pending은 메모리에만 있으며 활성 방문은 최대 하나. 파일을 준비만 한 상태는 방문이 아니다.
상태는 Empty, Active(Pending), Opening(기존 방문 선택적 보유), Deleting, SaveFailed, Closing으로 구분한다.
성공적으로 활성화한 후의 재생 오류/영상 끝/일시정지/페이지 오류는 Pending을 없애지 않는다.

| 명령/결과 | 기존 방문 및 저장 | 대상·세션 결과 |
|---|---|---|
| 최초 열기 실패 | 없음 | Empty, 새 Pending 없음 |
| 다른 대상 열기 시작 | Pending과 cursor 유지, 기존 미디어 보유 | 대상은 숨김·음소거 준비 |
| 대상 준비 실패/취소 | 기존 Pending/억제/위치 유지 | 준비 리소스 해제, 기존 상태 복귀 |
| 대상 준비 성공 | 기존 조작/재생을 잠시 멈춰 최종 위치를 캡처하고 진행+억제되지 않은 기록을 한 DB 트랜잭션으로 확정 | commit 성공 후 cursor 변경·대상 활성·새 Pending |
| 기존 방문 저장 실패 | rollback, Pending 유지, SaveFailed | 대상 해제, cursor/Seen 불변. 재시도/기존 재생 상태로 복귀 가능 |
| Previous/Forward 성공 | 기존 방문 확정 | 기존 슬롯으로 이동, 새 VisitId/Pending |
| 이번 제외 ON/OFF | 현재 Visit의 boolean 설정, 과거 기록 불변 | 재방문/다음에는 false |
| 즐겨찾기/영구 제외 | 해당 ItemId의 값만 트랜잭션 저장 | Pending/진행/Seen 불변 |
| 감상 화면 닫기/정상 종료 | 진행+기록 저장 성공 후 방문 종료 | 리소스 해제, 세션 폐기 |
| 단순 숨김·복원·pause·stop·영상 끝 | 기록 확정 없음 | 동일 방문 유지 |
| 빠른 종료 | 즉시 전체 숨김·음소거, 진행 중 준비 취소, 활성 방문 정상 저장 | T14의 종료 조정으로 해제·종료. 억제 명령 아님 |
| 삭제 취소/실패 | 기록·Pending 억제값 유지 | 원래 감상 복귀(재열기 필요 시 같은 Visit) |
| 삭제 성공 | 해당 경로 모든 Pending 억제, 모든 분류 기록 제거 | 현재 비움, 경로 슬롯 tombstone; §5 적용 |

전환은 '대상 준비 → 기존 저장 → 대상 활성'이다. 만화는 유효 페이지 목록과 시작 페이지 디코딩 완료, 영상은 엔진이 재생 가능한 준비 완료를 보고한 상태를 Ready로 한다. 파일 핸들만 열렸다는 이유로 성공하지 않는다. 자막 로드 실패는 영상 Ready와 별도다.
2026-09-09 사용자 승인 예외: Windows 기본 재생 장치가 없으면 영상 전용 Ready를 허용한다. 이 경우 오디오를 엔진 생성 때부터 비활성화하고 장치 연결 후 다시 열어야 소리를 사용할 수 있음을 표시한다. 장치 재연결만으로 현재 방문의 음성을 자동 활성화하지 않는다. 장치 조회 오류나 일반 디코딩 실패를 장치 부재로 간주하지 않는다. 장치가 있으면 기존 영상·오디오 준비 조건을 유지한다.

Ready 이후 활성화는 준비된 객체를 넘기는 논리적 commit이며 새 디코딩 작업을 시작하는 열기 단계가 아니다. 활성화 후 엔진 오류는 현재 방문 오류로 취급한다. 기존 미디어를 파괴한 뒤 대상 열기를 시도하는 구현은 계약 위반이다. T09에서 동시 보유/음소거 준비 가능성을 확인하고 불가능하면 T11 착수 전 Astra로 돌아온다.
저장 실패 때 자동으로 Pending을 폐기하거나 닫지 않는다. 일반 전환/닫기는 저장 재시도 가능하게 남긴다. 빠른 종료 저장 실패의 숨김 상태·재시도·복원 UX는 T14 차단 결정이며, 성공으로 위장하거나 강제 Kill하는 기본값은 없다.

### 중복 명령·비동기 소유권

모든 상태 변경은 한 조정자의 직렬 명령 처리로 수행한다. CommandId, SessionId, OperationId, VisitId를 사용한다. 같은 CommandId 재전달은 같은 결과/진행 중 결과를 반환하고 재실행하지 않는다. 토글 API는 Toggle이 아닌 SetFavorite(itemId, desiredValue), SetSuppressed(visitId, desiredValue)로 전달한다.
Opening/Deleting/저장 중 새 탐색·삭제·상태 편집은 Busy로 반환하며 큐에 쌓지 않는다. 숨김/음소거는 즉시 처리하며 Closing은 새 명령을 차단하고 진행 작업이 안전한 경계에 도달하기를 기다린다.
취소는 세대를 무효화한다. 모든 완료/위치 콜백의 세션·작업·방문 토큰을 검사하며 늦은 결과는 화면/DB/Pending에 적용하지 않고 그 결과 소유 리소스만 해제한다.
파일 삭제가 이미 시작되면 취소 요청만으로 실패로 바꾸지 않는다. 실제 결과를 끝까지 받아 §5 기록을 남긴다. 종료도 이 결과 처리를 기다린다.
VisitId는 ViewHistory와 VisitCommit PK로 사용해 저장 재시도 중 중복 적용을 막는다. 저장 시각은 최초 저장 시도 때 고정하며 재시도에서 변경하지 않는다. 동일 ID/다른 payload는 오류다. 정상 commit 확인 뒤 Pending을 정리한다. 저장 실패 후 현재 감상으로 복귀하면 해당 실패 시도의 저장 의도를 취소하고 다음 이탈 때 새 시각/최종 위치를 캡처한다. commit 여부가 불명확하면 먼저 VisitId 조회로 확인하며 확인 전 복귀/새 방문을 허용하지 않는다. 억제 방문도 VisitCommit 검증값은 저장하며 ViewHistory에는 넣지 않는다. 완료 확인 전 다음 작업을 진행하지 않는다. 스캔·삭제·진행 쓰기도 동일 항목의 순서와 삭제 격리를 지킨다.

## 4. 기록·선호·진행의 독립성

| 데이터 | 범위/의미 | 다른 데이터에 미치는 영향 |
|---|---|---|
| IsFavorite | 분류별 Item 선호 | 랜덤 확률·기간·기록 불변 |
| IsRandomExcluded | 분류별 신규 랜덤 제외, 해제 가능 | 수동/Back/Forward 열기 가능 |
| SuppressHistory | 현재 Visit 1회 기록 억제 | 과거 기록·진행 불변, OFF로 되돌릴 수 있음 |
| ViewHistory | 성공한 방문의 이탈 시 확정 | 마지막 시각만 기간 계산 |
| PlaybackProgress | 분류별 마지막 위치 | 기록 유무와 무관 |
| IsMissing | 관찰한 경로 부재 | 과거 기록/진행을 지우지 않음 |

진행 기본은 이어보기이며 ResumeMode=Resume/FromStart 설정을 둔다(기본 Resume). FromStart여도 진행은 저장한다.
만화는 0-based 기준 페이지, 세로 모드는 그 페이지 내 0~1 상대 오프셋(그 외 0). 두 페이지는 읽기 순서상 첫 페이지를 기준으로 한다. 영상은 0 이상 milliseconds. 다른 타입의 필드는 NULL이다.
5초마다 변경된 최신 위치 하나만 저장하고 pause/정지·정상 이탈 때 최종 저장한다. 위치 콜백을 모두 큐에 보관하지 않는다. 세로 위치 복원은 뷰포트 좌상단 기준 페이지 내 비율이다. 페이지 범위는 열린 파일의 [0,count-1], 영상은 [0,duration]으로 clamp한다. 길이 미확정 시 엔진이 탐색 가능해질 때 적용하며 실패하면 처음부터, 기록 성공 여부와는 분리한다. 끝부분 자동 초기화는 없다.
2026-09-09 사용자 승인 예외: 저장 위치가 길이와 같거나 길이를 넘어 clamp된 경우 재생 완료 상태로 복원한다. 표시 진행은 duration으로 유지하고 Activate에서 자동 재생/초기화하지 않는다. 종료 화면은 검은 영상 영역과 완료 상태이며 마지막 프레임 보장을 뜻하지 않는다. 재생을 명시적으로 누르면 같은 방문에서 처음부터 시작한다. 이 동작을 위해 숨김·음소거로 시작 프레임을 실제 디코딩하고 Paused 상태로 보유해도 된다. Prepare/Ready의 실제 디코딩, 세션/작업/방문 토큰, 현재 보존, 해제 계약은 유지한다. 완료 상태에서 pause/stop은 완료 위치를 보존하며 사용자의 다른 위치 탐색은 일시정지 상태로 이동한다. DB 스키마나 Pending 의미는 바꾸지 않는다.

같은 경로 내용 변경이 확인되면 기존 진행을 삭제하고 기본 시작 위치를 사용한다. 삭제 성공 시에도 진행을 제거한다. 외부 Missing은 위치를 유지한다.

## 5. 물리 삭제와 DB 정합성

기본은 확인 후 휴지통. REQUIREMENTS R10의 명시적 영구 삭제 선택은 유지하되 휴지통 실패의 자동 fallback은 없다. 실제 파일을 삭제하는 것이므로 다른 분류에도 영향을 준다는 내용을 확인에 포함한다.
정리 범위는 **동일 PathKey의 모든 ItemId**: 기록/진행/VisitCommit 검증값 제거, IsMissing=true, 즐겨찾기/영구 제외 유지. Item 행을 없애지 않는다. 해당 경로의 활성 방문/준비·세션 슬롯도 무효화한다. 이는 분류 간 상태 공유가 아니라 물리 삭제 결과의 동기화다.

SQLite와 파일 시스템은 하나의 트랜잭션이 아니다. T12는 다음 복구 프로토콜을 구현한다.

1. 경로를 격리하고 삭제 대상 ItemId 집합·크기/수정시각을 캡처한다. 같은 경로의 신규 열기/스캔 반영/진행·기록 쓰기를 차단한다. 진행 위치와 Pending은 메모리에 보유한다.
2. 앱 데이터의 delete-journal/<OperationId>.json에 Version=1, OperationId, PathKey, Path, 대상 ItemIds, Mode, CreatedAtUtc, Phase=Prepared를 원자 교체하고 디스크 flush한다. 저널 쓰기 실패면 실제 삭제를 시작하지 않는다.
3. 파일 리소스를 해제한 후 OS 휴지통/명시적 삭제를 수행한다. 반환값은 Succeeded / Failed / Cancelled / Unknown. 파일이 원래 없었다면 Succeeded로 추정하지 않고 Missing 관찰로만 처리한다.
4. 명확한 성공이면 메모리에서 즉시 삭제 반영·Pending 억제, Phase=Succeeded를 durable 기록한다. 실패/취소면 Phase=Failed/Cancelled를 기록하고 격리 해제·같은 Visit로 재열기를 시도한다. 재열기 실패해도 기존 Pending은 남고 오류 화면에서 이탈 시 정상 확정한다.
5. Succeeded인 작업은 DB 트랜잭션 하나로 위 정리와 AppliedDeletion(OperationId)를 삽입한다. commit 전후 재실행은 AppliedDeletion PK로 멱등 처리한다. commit 성공 후 저널 파일을 제거하고 격리 해제한다.
6. DB 실패면 성공 저널을 유지하고 해당 경로는 격리한 채 재시도한다. 다른 경로 감상은 가능하다. 종료/재시작 시 세션 복원 없이 저널만 재생한다.

시작 시 저널 검사를 라이브러리 공개 전에 수행한다. Succeeded는 자동 DB 재적용(이미 Applied이면 저널만 정리), Failed/Cancelled는 삭제 정리 없이 제거한다. 손상/Prepared/Unknown은 **성공 여부 불명**으로 격리하며 기록을 보존한다. 손상 때문에 PathKey를 읽을 수 없으면 복구 확인 전 라이브러리 쓰기/감상 시작 전체를 보류한다. 파일이 없다는 사실만으로 삭제 성공을 추정하지 않는다. 사용자가 삭제 성공을 확인하면 Succeeded로 기록 후 정리, 실패/취소 확인이면 기록 보존 후 격리 해제한다. 이 확인은 삭제 오류 복구 절차이며 세션 복원/일반 백업 기능이 아니다.
OS 성공 직후 저널 기록 실패/전원 단절은 Prepared만 남을 수 있어 자동 확정 불가능하다. 이 한계를 숨기지 않는다. 격리 중 같은 경로가 다시 생기면 자동 재삭제하지 않는다. 성공 저널의 대상 ItemId 집합만 정리한 후 재스캔하여 존재를 갱신한다. 격리로 신규 ItemId 생성도 차단한다.
저널은 파일 삭제 명령을 재실행하는 로그가 아니다. 복구는 DB 정리만 재실행한다. 완료된 AppliedDeletion 표식은 저널 제거 확인 뒤 삭제할 수 있으며 삭제 저널을 무한 보관하지 않는다.

## 6. SQLite v1 계약

T03은 Microsoft.Data.Sqlite 10.0.8 직접 SQL 접근을 사용한다. ORM/범용 repository/별도 Infrastructure 프로젝트는 만들지 않는다. 아래 DDL은 App/Data/SchemaV1.sql과 동일한 v1 계약이다. ID와 PathKey 생성/경로 검증은 위 계약을 따른다.

```sql
CREATE TABLE Category (
 Id TEXT PRIMARY KEY NOT NULL,
 Name TEXT NOT NULL CHECK(length(trim(Name)) BETWEEN 1 AND 100),
 MediaType TEXT NOT NULL CHECK(MediaType IN ('Comic','Video')),
 IsEnabled INTEGER NOT NULL DEFAULT 1 CHECK(IsEnabled IN (0,1)),
 UNIQUE(Id, MediaType)
);
CREATE TABLE CategorySource (
 Id TEXT PRIMARY KEY NOT NULL,
 CategoryId TEXT NOT NULL REFERENCES Category(Id) ON DELETE CASCADE,
 RootPath TEXT NOT NULL,
 RootPathKey TEXT NOT NULL COLLATE BINARY,
 IncludeSubdirectories INTEGER NOT NULL DEFAULT 1 CHECK(IncludeSubdirectories IN (0,1)),
 IsEnabled INTEGER NOT NULL DEFAULT 1 CHECK(IsEnabled IN (0,1)),
 UNIQUE(CategoryId, RootPathKey)
);
CREATE TABLE MediaItem (
 Id TEXT PRIMARY KEY NOT NULL,
 CategoryId TEXT NOT NULL,
 MediaType TEXT NOT NULL CHECK(MediaType IN ('Comic','Video')),
 Path TEXT NOT NULL,
 PathKey TEXT NOT NULL COLLATE BINARY,
 FileSize INTEGER NOT NULL CHECK(FileSize >= 0),
 LastWriteTimeUtc INTEGER NOT NULL,
 IsFavorite INTEGER NOT NULL DEFAULT 0 CHECK(IsFavorite IN (0,1)),
 IsRandomExcluded INTEGER NOT NULL DEFAULT 0 CHECK(IsRandomExcluded IN (0,1)),
 IsMissing INTEGER NOT NULL DEFAULT 0 CHECK(IsMissing IN (0,1)),
 FOREIGN KEY(CategoryId, MediaType) REFERENCES Category(Id, MediaType),
 UNIQUE(CategoryId, PathKey),
 UNIQUE(Id, MediaType)
);
CREATE INDEX IX_Item_Path ON MediaItem(PathKey);
CREATE INDEX IX_Item_Candidates ON MediaItem(CategoryId, IsMissing, IsRandomExcluded);
CREATE TABLE ViewHistory (
 VisitId TEXT PRIMARY KEY NOT NULL,
 MediaItemId TEXT NOT NULL REFERENCES MediaItem(Id) ON DELETE CASCADE,
 ViewedAtUtc INTEGER NOT NULL,
 Origin TEXT NOT NULL CHECK(Origin IN ('Random','Manual','Back','Forward'))
);
CREATE INDEX IX_History_ItemTime ON ViewHistory(MediaItemId, ViewedAtUtc DESC);
CREATE TABLE PlaybackProgress (
 MediaItemId TEXT PRIMARY KEY NOT NULL,
 MediaType TEXT NOT NULL CHECK(MediaType IN ('Comic','Video')),
 ComicPageIndex INTEGER,
 ComicPageOffset REAL,
 VideoPositionMs INTEGER,
 UpdatedAtUtc INTEGER NOT NULL,
 FOREIGN KEY(MediaItemId, MediaType) REFERENCES MediaItem(Id, MediaType) ON DELETE CASCADE,
 CHECK(
  (MediaType='Comic' AND ComicPageIndex IS NOT NULL AND ComicPageIndex>=0
   AND ComicPageOffset IS NOT NULL AND ComicPageOffset BETWEEN 0 AND 1
   AND VideoPositionMs IS NULL)
  OR
  (MediaType='Video' AND VideoPositionMs IS NOT NULL AND VideoPositionMs>=0
   AND ComicPageIndex IS NULL AND ComicPageOffset IS NULL)
 )
);
CREATE TABLE AppSettings (
 Id INTEGER PRIMARY KEY CHECK(Id=1),
 HistoryExclusionDays INTEGER NOT NULL DEFAULT 7 CHECK(HistoryExclusionDays BETWEEN 0 AND 36500),
 ResumeMode TEXT NOT NULL DEFAULT 'Resume' CHECK(ResumeMode IN ('Resume','FromStart'))
);
INSERT INTO AppSettings(Id) VALUES(1);
CREATE TABLE AppliedDeletion (
 OperationId TEXT PRIMARY KEY NOT NULL
);
CREATE TABLE VisitCommit (
 VisitId TEXT PRIMARY KEY NOT NULL,
 MediaItemId TEXT NOT NULL REFERENCES MediaItem(Id) ON DELETE CASCADE,
 PayloadHash BLOB NOT NULL CHECK(length(PayloadHash)=32)
);
CREATE INDEX IX_VisitCommit_Item ON VisitCommit(MediaItemId);
```

FileName/Extension은 Path에서 도출하며 별도 식별 필드로 저장하지 않는다.
감상 Pending·세션·Seen·fingerprint·분류 override 테이블/열은 만들지 않는다. Category 삭제 기능은 이번 계약으로 추가하지 않으며 FK가 기록 보존 정책을 우회하는 사용자 명령을 뜻하지 않는다.

연결마다 foreign_keys=ON, busy_timeout=5000; 로컬 DB journal_mode=WAL, synchronous=FULL. DB writer 하나로 직렬화한다. 읽기는 짧은 snapshot, 파일 IO/디코딩 동안 DB 트랜잭션을 열어두지 않는다.
분류/소스 편집, 성공 스캔 범위 upsert/Missing, 방문 최종 진행+기록, 삭제 정리+AppliedDeletion은 각각 원자적 쓰기 단위다. 단순 진행 checkpoint와 즐겨찾기/영구 제외는 별도 짧은 트랜잭션이다.
PRAGMA user_version=1. 빈 DB만 위 스키마로 초기화하고 순차 N→N+1 migration을 단일 트랜잭션으로 수행한다. 실패하면 rollback하고 기존 DB 보존, 오류 안내 후 데이터 기능을 열지 않는다. 지원보다 높은 버전도 쓰지 않는다. drop/recreate·자동 다운그레이드 없음. 재실행 시 동일 migration을 중복 적용하지 않는다. T03은 초기화 및 실패 원자성을 검증하며 후속 migration은 필요 Task에서 추가한다.

## 7. 설정·메모리·책임 계약

- 저장 루트: %LOCALAPPDATA%/RandomMultimediaManager/. DB는 library.db, 삭제 저널은 delete-journal/. exe 옆 저장/portable mode/클라우드 동기화는 없음. 압축 배포와 데이터 저장 위치는 별개다.
- 설정도 같은 SQLite AppSettings 단일 행으로 저장한다. 실패하면 UI가 새 값으로 확정된 것처럼 표시하지 않는다. 미확정 키/트레이·뷰어 기본값을 T03에서 미리 넣지 않는다. 이후 해당 Task에서 column migration을 추가한다.
- 세션 순서/cursor/Seen/Pending/억제/선택 분류는 메모리만. 종료·화면 닫기·새 세션 성공 시 폐기. 충돌/강제 Kill로 Pending이 사라질 수 있고 재시작 시 감상을 추정해 기록하지 않는다. 이미 저장된 기록·진행·삭제 복구만 영속한다.
- 세션 슬롯에는 ID/상태만 보관하며 디코더·이미지·미디어 객체를 보관하지 않는다. 현재와 전환 준비 대상 최대 두 미디어만 소유한다. 경로 10000 슬롯 또는 Seen 10000 ID 도달 시 새 슬롯/신규 선택을 SessionLimit으로 거부하고 현재/Back/Forward를 유지한다. 기록을 잘라 중복 방지를 깨지 않는다. 새 세션 시작 안내로 해소한다. 명령 재전달 캐시는 현재 세션 최근 256개이며 그보다 오래된 CommandId 재전달은 지원하지 않는다. VisitId DB 멱등성은 별도로 유지한다.
- Core(.NET 10, WPF/SQLite/엔진 참조 없음)에 T03 실제 모델/값 계약이 있다. T06에서 순수 후보/방문 정책을 넣는다. 이유는 UTC 경계·실패/재방문을 Windows 렌더러 없이 테스트하기 위해서다. App→Core 단방향, Core.Tests→Core. SQLite integration test는 별도 필요한 테스트 범위에서 수행한다.
- 데이터 접근/OS 파일 작업/미디어 구현은 App 내부의 필요한 폴더에만 둔다. 역할마다 프로젝트·인터페이스를 선행 생성하지 않는다. T07/T09는 T03의 공유 모델을 사용한다.

| 역할 | 입력→결과 계약 |
|---|---|
| 후보 정책(Core) | 고정 Now/기간/선택 분류/항목 snapshot/Seen → 후보 ID; IO 없음 |
| 방문 정책(Core) | 상태+명령+결과 → 다음 상태/저장 의도; WPF·엔진 호출 없음 |
| 조정자(App, T06/T11) | CommandId와 현재 토큰 검증, 준비/저장/활성 순서 소유 |
| 데이터(App, T03) | CommitVisit(VisitId, ItemId, time, origin, suppress, progress) → Committed/AlreadyCommitted/Failed; suppress면 기록 없이 진행만 저장 |
| 미디어(T07~T09) | Prepare(ItemId, Path, Progress, OperationId, cancel) → Ready(owned handle)/Failed/Cancelled; Ready는 아직 표시·소리 없음 |
| 공통 호스트(T11) | Activate(ready, VisitId), CaptureProgress(VisitId), SetMuted, Dispose; 페이지/재생 위치는 타입별 payload |
| 파일(T05/T12) | 존재: Present/Missing/Unavailable; 삭제: Succeeded/Failed/Cancelled/Unknown |
| 생명주기(T14) | Hide는 표시/소리만, Close는 조정자 종료 요청; 직접 기록 생성/삭제 금지 |

위 메서드는 의미 계약이다. 실제 인터페이스는 해당 소비자가 생기는 Task에서만 만든다. 재생 조작의 상세 API나 트레이 키는 T02에서 정하지 않는다.

## 8. 필수 정책 검증 사례와 남은 경계

| Task | 필수 사례 |
|---|---|
| T03 | DDL 제약/외래키, 같은 분류 경로 중복 거부·다른 분류 허용, 타입별 진행 NULL 제약, migration rollback/상위 버전 거부, VisitId 재시도, 삭제 정리 원자성 |
| T05 | 중첩 소스 중복, 소스 해제≠Missing, 접근 실패·취소, 대소문자/이동/재등장/내용 교체, reparse 경로 거부 |
| T06 | 7일 경계±1ms·0일·미래 기록, A→B→Back A→Forward B의 4개 Visit, 억제 재방문 초기화, 수동 삽입 Forward 보존, 후보 0/한도, Busy·늦은 Ready·commit 실패 |
| T11 | 만화↔영상 준비 실패의 기존 방문 보존, 자막 실패 분리, 이어보기/이번 제외 독립, 정상 닫기와 Hide 분리 |
| T12 | 다른 분류 동일 경로 정리, 실패/취소 기록 보존, OS 성공 뒤 DB 실패 재시작, Prepared 불명 격리, Applied 뒤 저널 제거 실패 재시도, 새 파일 재삭제 금지 |

T02 데이터/감상 정책에 미확정 차단 항목은 없다. 실제 후속 코드 착수는 T01 Windows 검증 완료와 이 계약 통합이 선행이다. T09의 Ready/동시 보유 검증이 실패하면 T11을 차단하고 Astra 판단을 받는다.
T14에는 숨김/복원·전역 키/트레이·종료 저장 실패 처리의 상세 설계를 남긴다. T08 표시 기본값, T10 자막 선택, T18 배포/OS 범위, T19 VSR은 각각의 Task에서 위임 범위 안에 결정한다. 자동 재식별·기록 승계·세션 복원·후보 완화는 승인하지 않는다.

기술 근거(2026-09-07 접근 확인): [SQLite PRAGMA](https://www.sqlite.org/pragma.html), [Microsoft.Data.Sqlite 트랜잭션](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/transactions). 제품 정책과 기본값은 위 문서에서 도출한 요구사항이 아니라 T02의 설계 결정이다.

## T03 승인 보완 및 저장 API

2026-09-07 사용자 승인으로 VisitCommit만 v1에 추가했다. 이전 스키마는 문서 상태로 제품 DB 배포 이력이 없으므로 신규 v1 초기화에 포함한다. 기존 문서 DDL로 수동 생성한 DB는 자동 변환하지 않고 오류로 거부한다.

- 목적: 다음 방문/진행 checkpoint가 PlaybackProgress를 갱신한 후에도 이전 VisitId의 서로 다른 payload를 거부한다.
- 검증값: VisitPayload의 버전 1 BinaryWriter 직렬화에 대한 SHA-256 32바이트. 순서는 버전, 소문자 D UUID VisitId/ItemId, UTC ms, Origin, SuppressHistory, MediaType, 타입별 위치다. 숫자는 BinaryWriter의 little-endian, 문자열은 UTF-8 길이 접두 방식이며 위치 offset의 -0은 +0으로 통일한다. NaN/무한대는 모델 검증으로 거부한다.
- VisitCommit은 감상 이력이 아닌 저장 재시도 검증 표식이다. 억제 여부/시각/위치 원문을 별도 방문 이력으로 보관하지 않고 검증 해시만 저장한다. Pending/세션 복원이나 랜덤 기간 계산에 사용하지 않는다.
- 새 요청은 검증값·최종 진행·억제되지 않은 이력을 하나의 트랜잭션으로 저장한다. 동일 검증값 재시도는 AlreadyCommitted이며 어떠한 진행/기록도 다시 쓰지 않는다. 다른 값은 Failed다. 호출자는 확정할 요청의 시각/위치를 고정해야 한다.
- 표식은 해당 항목의 삭제 정리 시 이력/진행과 함께 제거한다. 삭제 이후 이전 방문 재전달은 금지하며 T12 조정자가 토큰/격리로 차단한다. 오래된 명령 무제한 재생이나 삭제 취소용 로그가 아니다.
- ApplyDeletion은 Succeeded 저널의 PathKey/ItemIds를 입력받는다. 캡처 대상만 처리하고 OperationId 중복은 변경 없이 반환한다. 저널 제거 확인 후 ForgetAppliedDeletion을 호출한다. OS 삭제·격리·저널 구현은 T12 범위다.
- LibraryDatabase.Open()은 LocalAppData의 DB를 초기화하고 실패 시 예외를 전달한다. App은 오류를 알리고 데이터 기능을 열지 않는다. 앱은 단일 인스턴스를 소유하며 내부 lock으로 작업/종료를 직렬화한다. UI 호출자는 Task.Run 등으로 DB 대기를 UI 스레드에서 분리한다.
- SaveCategory, AddSource/UpdateSource/RemoveSource, GetCategories/GetSources/GetItems, ApplyObservedItems, SetFavorite/SetRandomExcluded, GetHistory/GetProgress/SaveProgress, GetSettings/SaveSettings를 제공한다. 저장 실패는 예외이며 CommitVisit/ApplyDeletion은 Failed 결과다.
- ApplyObservedItems는 T05가 제공한 성공 관찰 목록과 확정 Missing ID만 원자적으로 반영한다. 관찰 시 신규 항목 상태는 기본값, 기존 ID/선호/이력은 유지한다. 파일 접근/스캔/정규화/reparse 검사 및 같은 경로 다중 분류 Missing 대상 수집은 T05 책임이다. 저장 API는 이미 정규화된 Path/PathKey 쌍을 요구한다.
- Core.Tests는 순수 모델을 검증한다. Data.Tests는 App/Data 제품 소스와 SQL 리소스를 링크하여 동일 구현을 net10.0에서 실행한다. 제품 Infrastructure 프로젝트나 테스트용 저장 구현은 없다.

패키지: [Microsoft.Data.Sqlite 10.0.8](https://www.nuget.org/packages/Microsoft.Data.Sqlite/10.0.8), .NET Standard 2.0 대상으로 net10.0 및 net10.0-windows 호환. 직접 참조는 정확한 버전으로 고정했다. [공식 트랜잭션 문서](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/transactions)와 함께 2026-09-07 접근 확인.

네이티브 번들은 [SQLitePCLRaw.bundle_e_sqlite3 2.1.13](https://www.nuget.org/packages/SQLitePCLRaw.bundle_e_sqlite3/2.1.13)으로 고정한다. 초기 복원에서 자동 선택된 2.1.11의 NU1903 경고를 확인하여 같은 2.1 계열 패치를 명시했다. 경고 억제는 하지 않는다.

## T06 구현 API와 T11 인계

- Core `CandidatePolicy.GetCandidates`는 고정 snapshot/Now/선택/Seen/격리를 받아 ItemId 중복 없는 후보를 반환한다. 균등 추첨은 App 조정자의 `Random.Shared.Next(candidateCount)` 한 번이다. `SessionPath`는 슬롯·cursor·Seen·고정 선택 집합·방문별 Pending·한도·삭제 tombstone을 소유한다. 세션 복원/DB schema 추가는 없다.
- App `Sessions/SessionCoordinator`의 `StartRandomAsync`, `OpenManualAsync`, `PreviousAsync`, `NextAsync`, `SetSuppressedAsync`, `LeaveAsync`는 필수 CommandId를 받는다. 상태는 복사된 `View`로 조회한다. 동일 CommandId는 같은 Task/결과를 반환하며 현재 세션 최근 256개를 보존한다. Busy는 큐 적재 없이 반환한다. 실제 창/트레이/프로세스 종료는 연결하지 않았다.
- `ISessionMediaPreparer.PrepareAsync(item, progress, SessionToken, cancellation)`은 숨김·음소거 및 시작 위치 디코딩을 끝낸 `ISessionMedia` 소유권을 결과로 전달한다. 조정자가 결과를 받은 뒤에는 준비자/호스트가 별도로 해제하지 않는다. 실패 결과가 리소스를 포함하더라도 조정자가 해제하며, 예외를 던지는 준비자는 아직 반환하지 않은 리소스를 자체 해제해야 한다.
- `ISessionMedia.PauseAndCapture`는 기존 재생/조작을 중지하고 최종 위치를 반환하며 `Resume`은 저장이 안 된 것으로 확인된 방문의 기존 재생 상태를 복원한다. `Activate`는 새 열기가 아닌 논리적 소유권 전환이다. 활성화 후 엔진 오류는 새 Pending을 유지하고 Completed 결과의 Error로 안내한다. `DisposeAsync`는 소유 리소스를 모두 해제해야 한다.
- T11은 기존 `PreparedComic`/`PreparedVideo`를 이 경계에 연결하는 어댑터를 구현한다. 조정자 호출과 미디어 메서드는 소유 UI Dispatcher에서 수행하며 조정자의 await는 그 문맥을 유지한다. SQLite 작업은 Task.Run으로 분리한다. VideoOperation의 SessionId/OperationId/ItemId와 VideoVisit.VisitId를 SessionToken에 매핑하고 기존 Ready·장치 부재·끝 위치·자막 실패 분리를 보존한다. T06은 엔진이나 화면을 수정하지 않았다.
- `PreparingToken`과 `CancelOpening(sessionId, operationId)`는 준비 세대를 무효화한다. 늦은 Ready의 세션/작업/항목 토큰과 취소 여부를 검사하고 해제한다. 해제가 끝날 때까지 Busy여서 현재+준비 대상 두 개 한도를 유지한다. 저장 단계부터는 취소로 commit을 되돌리지 않는다.
- `ObserveProgress`는 일치하는 활성 Session/Operation/Item/Visit 토큰의 최신 위치 하나만 메모리에서 받는다. 최종 저장은 반드시 PauseAndCapture 결과를 사용한다. T11의 5초 checkpoint/일시정지 저장·즐겨찾기/영구 제외 UI와 스캔/삭제 동시 쓰기는 Busy/토큰/격리와 같은 순서로 연결해야 한다. T06에서 checkpoint 타이머나 UI를 추가하지 않았다.
- `FrozenCommit`은 최초 이탈 시각/위치/억제값을 고정한다. 저장 실패면 대상은 해제되고 SaveFailed에 기존 방문을 유지한다. `RetryAsync`는 원래 대상을 새로 준비한 후 동일 payload로 재시도한다. 재준비 실패도 저장 의도를 지우지 않는다. `ResumeAfterSaveFailureAsync`는 `LibraryDatabase.CheckVisitCommit`이 NotCommitted일 때만 저장 의도를 취소하고 기존 감상으로 복귀한다. Committed/PayloadMismatch/조회 오류는 복귀하지 않는다. 성공 확인된 방문은 원래 전환을 재시도하여 완료한다.
- `CheckVisitCommit`은 기존 VisitCommit 해시를 읽기만 하며 `NotCommitted/Committed/PayloadMismatch`를 구분한다. 억제 방문도 확인 가능하다. commit 반환 유실은 같은 조회로 확인하고 조회도 실패하면 CommitUnknown으로 유지한다. schema/VisitPayload/기존 CommitVisit 의미는 변경하지 않았다.
- `GetSessionSnapshot`은 하나의 짧은 SQLite 읽기 snapshot으로 분류·소스·항목·마지막 기록을 반환한다. 디코딩 동안 트랜잭션을 유지하지 않는다. 랜덤 선택은 디스크 재스캔을 수행하지 않으며 존재/디코딩 실패는 준비자가 Failed로 반환한다.
- T12 결과 연결용 `ReportDeletionAsync`는 OS 삭제를 실행하지 않는다. Failed/Cancelled는 방문/억제/기록을 유지하고 Unknown은 경로를 격리한다. 현재 경로가 격리되면 이탈 기록 저장도 막는다. Succeeded는 같은 PathKey 슬롯들을 tombstone으로 바꾸고 해당 현재 미디어/Pending만 비운다. Seen/cursor는 보존하고 자동 다음 재생은 없다. DB 삭제 정리는 T12의 성공 저널→기존 ApplyDeletion 순서이며 완료 후 `ReleaseDeletionQuarantineAsync`를 호출한다. T12는 실제 삭제 시작/리소스 해제/동일 Visit 재열기/저널/시작 복구를 구현해야 한다. 이 API에 결과를 전달하기 전부터의 삭제 직렬화·격리는 T12 책임이며 결과 보고 API가 OS 단계 전체를 대신하지 않는다.

검증 실행: `dotnet run --project tests/RandomMultimediaManager.Core.Tests -c Release`, `dotnet run --project tests/RandomMultimediaManager.Data.Tests -c Release`. Data.Tests는 App/Sessions와 실제 App/Data 소스를 링크하며 미디어만 테스트 대역이다. `.github/workflows/t06-session.yml`은 Windows x64/.NET 10 솔루션 Release 빌드와 이 검사들 및 T05 전체 스캔 회귀를 실행한다. 실제 성공 결과는 CURRENT_STATE.md에 기록한다.

## T12 구현 API와 복구 연결

- `DeletionService`/`DeletionJournal`은 앱이 공유하고, 시작 시 `InitializeAsync`로 저널 격리·DB 복구를 수행한 후 메인 창을 공개한다. OS 삭제는 `WindowsFileDeletion`의 명시적 실행 경로만 호출한다. 복구는 DB 정리만 재실행한다.
- `SessionCoordinator.DeleteCurrentAsync`는 기존 CommandId/Busy admission 안에서 Prepared→미디어 해제→OS→결과/DB 정리를 수행한다. 성공은 Pending 제거와 슬롯 tombstone, 실패/취소는 같은 Visit 재열기다. `ResolveDeletionAsync`는 사용자 확인/정리 재시도와 활성 방문을 연결한다.
- `LibraryDatabase.CaptureDeletion`은 기존 DB writer lock 안에서 캡처와 격리를 원자적으로 수행한다. `IsDeletionBlocked`/`DeletionPaths`를 후보·신규 열기에 연결하며 스캔/진행/방문 쓰기도 같은 격리를 확인한다. 스키마 및 ApplyDeletion 의미는 변경하지 않았다.
- AppliedDeletion 표식은 저널 파일 제거 후에도 보존한다. 저널 제거 직후 전원 단절에 따른 파일 재등장에도 기존 멱등 표식을 적용한다. 삭제 저널 파일 자체는 성공 정리 후 제거한다.
- 구체적인 오류·재시작·사용자 확인 및 검증 범위는 docs/T12_DELETION_VALIDATION.md. T14 생명주기 문서의 최종 연결은 T12 통합 후 대조하며 트레이/빠른 종료를 구현하지 않았다.

## T18A 변경 감지 설계 — T18A-1~2 구현, 후속 연결 대기

기준 main `c8252a0`, PR #21 merge `7fb2ad4` 포함. D13은 로컬·매핑/RaiDrive·직접 UNC 지원 목표의 승인이다. 아래는 2026-10-04 사용자가 D10~D12/D14~D16을 승인한 구현 기준이다. 제품 구현/호환성 확인 완료는 아니다. 이전 PR #21의 전역 두 bool 및 10분 전체 검사는 이 절로 대체한다. 제품 정책 결정은 DECISIONS.md D10~D12/D14~D16, 구현 인계는 TASKS.md T18A-1~6을 따른다. T18A-1은 DB v2와 저널 읽기 계약만 구현한다. 기존 식별/기록/진행 의미와 v1 저널 복구는 유지한다.

T18A-2의 소스별 스캔·생명주기 조정자는 PR #24에서 구현·Windows 자동 검증을 완료했다. 감상 엔진·원격 삭제·UI 연결은 후속 Task다. 아래 §1 표는 설계 시점 코드 대조이며 현재 API와 인계 경계는 §7을 따른다.

### 1. 설계 시점 코드 대조와 변경 책임

아래 경로는 `src/RandomMultimediaManager.App/` 기준이다. 문서의 “로컬 전용”과 달리 드라이브 문자 형식 검사는 매핑 드라이브도 통과할 수 있다. 이것은 의도된 네트워크 지원이나 검증 완료의 근거가 아니다.

| 실제 코드/계약 | 현재 동작·차이 | 필요한 설계 변경 / 담당 Task |
|---|---|---|
| ViewModels/CategoryEditorViewModel.cs, SourcePathRules.NormalizeLocalFolder | UNC 명시 거부, 드라이브 문자 절대 경로 허용 | IO 없는 공통 정규화와 비동기 소스 확인 분리 / 1, 5 |
| Core/Models.cs, Data/LibraryDatabase.Catalog.cs | PathKey=대문자 Path; (CategoryId,PathKey) 유일, 소스 소속은 경로 포함; DB 검증은 pair만 확인 | 기존 키·ID 보존, 연결 대상 바인딩/정책 별도 저장 / 1 |
| Scanning/LibraryScanner.cs | 드라이브 루트 길이 3 가정; DriveInfo.IsReady, 상위 reparse/case 검사; NotFound를 부재 prefix로 수집; 수집과 Apply 결합 | UNC 공유 경계, 소스별 수집 및 결과 증거·generation 검사 / 1, 2 |
| Data/LibraryDatabase.Catalog.cs, ApplyObservedItems | 메타데이터 변화 시 진행 제거, 이력/선호 보존; 격리 포함 시 transaction 실패; 같은 PathKey 모든 분류 Missing | 감상 전체 수명 보류, 소스 간 상충/불확실 관찰 보존 / 2 |
| Sessions/SessionCoordinator.cs, ChooseRandom/Move | 추첨 후 preparer 호출; 한 명령 한 후보, 실패 시 Seen/cursor/Pending 유지 | 연결·바인딩 확인과 일시 실패 억제. 자동 연쇄 추첨 금지 / 4 |
| Media/Comic/ComicArchive.cs, ComicViewerViewModel.cs | FileStream/ZipArchive 및 암호화 검사·seek, 페이지 복사/디코딩, 앞1/뒤2·256 MiB 캐시 | 느린 open/read/dispose 소유권 추적; 다운로드 상한 보장 불가 / 4 |
| Video/PreparedVideo.cs | Dispatcher에서 File.Exists, native 생성/Stop/Dispose 일부 Task.Run | UI의 원격 존재 확인 분리; native 호출별 스레드 제약 보존·지연 검사 / 4 |
| Video/ExternalSubtitles.cs | Directory.Exists/열거, File.ReadAllBytes, 임시 UTF-8 변환 | 자막 없음과 접근 실패 구분; 늦은 로드 폐기, 영상 Ready와 실패 분리 / 4 |
| Views/LibraryBrowserView.xaml.cs | 클릭 시 File.Exists 후 Explorer `/select,` 단일 인수 | 비동기 상태 확인, UNC 인수 회귀; DB 목록은 재열거 없이 표시 / 5 |
| Deletion/WindowsFileDeletion.cs | NotFound/Win32 2·3을 Missing=true; 영구삭제 실패 반환; recycle sink로 비휴지통 거부 | 원격 NotFound/응답 유실 분리, capability와 결과 증거 구분 / 3 |
| DeletionService/Journal, Data/LibraryDatabase.Deletion.cs/.Visits.cs | 단일 PathKey 격리·v1 저널·OperationId AppliedDeletion; 시작 복구는 OS 재실행 안 함 | 별칭 집합 격리·v2 저널, 대상별 키 검증·원자 정리 / 1, 3 |
| ViewingWindow.Run/CheckpointAsync/RequestCloseAsync, MainWindow.StopScanningAsync, Lifecycle/AppLifecycle | checkpoint/Leave·명령·scan/복구 drain 후 DB 해제; 실패 시 복원 수단 유지 | 모든 원격 작업 등록, Closing 이후 새 IO 금지·미완료 작업 drain / 2, 4, 6 |

현재 코드에서 실제 네트워크 오류를 재현한 것은 아니다. 위 차이는 소스 검토 결과이며 Windows/NAS/RaiDrive 성공 결과로 쓰지 않는다.

### 2. 경로·저장소 분류·바인딩

**문자열 정규화는 네트워크 접속 없이 수행한다.** 드라이브 절대 경로와 `\\server\share\folder`를 받는다. UNC의 최소 루트는 `\\server\share\`이며 서버만 있는 경로는 거부한다. 공유 루트도 소스로 등록 가능하다. `/`→`\`, 완전 절대 경로의 `.`/`..` 해소, 루트 외 끝 구분자 제거, 표시용 대소문자 보존·키 ToUpperInvariant·BINARY·Unicode 비병합은 유지한다. UNC `..`가 공유 루트 밖으로 나가거나 정규화 전후 서버/공유가 달라지는 입력은 거부한다. 상대/드라이브 상대/URL/장치 `\\?\`·`\\.\`/ADS/끝 공백·마침표는 계속 거부한다. 포함 비교는 루트와 구성요소 경계로 하며 `share`와 `share2`, `A`와 `AB`를 합치지 않는다.

서버명 대소문자는 키에서 같아도 호스트명/IP/FQDN/DFS/8.3/하드링크는 자동 병합하지 않는다. 원격 대소문자 구분 저장소에서 구분되는 두 이름을 한 키로 저장할 수 없으므로 충돌 또는 명시적 case-sensitive 증거가 있으면 해당 범위 반영을 보류한다. Windows 전용 case 정보 조회 미지원만으로 원격 전체를 거부하지 않되, 대소문자 비구분임을 검증했다고 주장하지 않는다. 서버/공유 위로 조상 검사를 올라가지 않는다. reparse/정션은 기존 거부를 유지하며 클라우드 placeholder가 이에 해당하면 해당 경로는 제한으로 표시한다. D13이 모든 provider 기능 지원을 뜻하지 않는다.

| 분류 | 판단 근거 | 확인 불가 시 |
|---|---|---|
| Local | 드라이브 유형·장치/볼륨 정보와 매핑 조회를 함께 확인 | 문자 또는 Fixed 보고만으로 확정하지 않음 |
| RemoteMapped | Windows 연결의 원격 대상 확인; 가능한 경우 WNetGetConnection로 대상 이름 조회 | API 실패가 로컬 증거는 아님. 다른 로그온 세션 연결도 이용 불가일 수 있음 |
| RemoteUNC | 문법상 서버+공유 루트 | 공유 접근 성공과 실제 파일 부재는 별도 |
| VirtualOrUnknown | RaiDrive 등 provider 정보가 불충분하거나 판단 상충 | 원격 보수 정책. 사용자가 “원격/가상”으로 명시 가능; “로컬” 지정만으로 삭제 안전성 승격 금지 |

**PathKey(논리 항목)와 Binding(현재 연결 대상)을 구분한다.** Binding은 드라이브 루트 또는 UNC 공유 루트 단위로 저장한다. 대상 이름/provider/가능한 볼륨 근거와 신뢰 수준을 보존하되 파일 내용 fingerprint는 만들지 않는다. 볼륨 일련번호 하나나 같은 서버 이름은 영구 물리 동일성 증명이 아니다. 소스를 제거해도 기존 항목 수동 열기에 필요한 바인딩은 남긴다. 어느 소스에도 속하지 않는 과거 항목도 같은 루트 검사를 통과해야 한다.

D14 확정: 같은 `Z:\`가 다른 공유/계정 대상으로 바뀌면 `BindingChanged`로 차단한다. 스캔 Present/Missing, 랜덤·수동/Back/Forward 열기·Explorer·삭제를 허용하지 않는다. 이전 기록은 그대로 남기고 원래 대상 복원 또는 **다른 루트 경로로 등록**하도록 안내한다. 이 버전에는 같은 경로의 새 대상을 받아들이며 기록을 초기화/이전하는 기능을 넣지 않는다. 드라이브 문자 변경이나 매핑→UNC 등록은 새 PathKey·새 ItemId이며 자동 이동 승계가 없다.

바인딩 확인은 시작의 필요 작업, 소스 저장/스캔 전후, 열기/삭제 직전 및 재연결 때 한다. 불일치/끊김/설정 변경 때 메모리 generation을 올려 이전 관찰·Ready 적용을 막는다. 최초 기존 데이터에는 원래 대상 증거가 없으므로 원격/불명 바인딩은 사용자 확인 후 채택한다. 증거를 얻을 수 없는 provider는 매 앱 시작·관찰된 재연결 때 같은 대상이라는 사용자 확인이 필요하다. 확인 전 DB 목록/기록 조회는 가능하다. 확인 후에도 OS가 숨기는 대상 변경 및 검사와 실제 IO 사이의 경합은 탐지 보장 불가이며 특히 삭제 확인창에 대상 경로를 표시한다. 보안 경계나 원격 서버 신원 인증으로 주장하지 않는다.

### 3. 소스별 갱신과 색인

DB/로컬 삭제 저널의 최소 안전 초기화 후 기존 DB 목록을 먼저 표시한다. 네트워크 조회/스캔 완료가 첫 화면 조건이 아니다. 자동 기준값은 D10~D12 **확정 정책**이다.

| 소스 | 시작 검사 기본값 | 실행 중 기본값 | 누락 회복/예약 기본값 |
|---|---|---|---|
| 확인된 Local | 켬, UI 준비 후 | 이벤트 감지 켬 | 마지막 완료 후 24시간, 앱 실행·유휴 중 한 번 |
| RemoteUNC/RemoteMapped | 끔 | 수동 기본, watcher 필수 아님 | 사용자가 예약을 켤 때 24시간 기본, 1~168시간 선택 |
| RaiDrive/Unknown | 끔 | 수동 기본, 확인 전 자동 금지 | 선택 예약도 동일; 캐시/연결 상태를 함께 표시 |

소스별 `ScanOnStartup`, `RefreshMode(Manual/Events/Scheduled)`, `IntervalHours`를 둔다. Events는 확인된 Local에서만 선택; 원격 이벤트 신뢰성 개선은 이번 필수 범위가 아니다. 시작 검사와 갱신 모드는 독립이며 모두 꺼도 수동 가능하다. 기존/신규 소스 기본은 D10 확정값을 사용한다. 로컬이라고 뒤늦게 확인돼도 사용자가 저장한 옵션은 덮어쓰지 않는다. 주기는 마지막 성공한 소스 범위 검사 기준, 앱이 꺼져 있던 횟수를 몰아서 실행하지 않고 재시작/절전 복귀 시 자격 있는 한 건만 합친다. OS 예약 작업은 추가하지 않는다.

색인은 이름/종류/확장자/크기/수정시각의 디렉터리 메타데이터만 사용한다. 해시·썸네일·ZIP 목록·영상 probe·자막 내용은 색인 중 읽지 않는다. provider가 메타데이터 요청만으로 다운로드하는 것까지 차단할 수 없으며 그런 provider는 수동 사용을 권한다. 앱이 미디어 본문을 요청하지 않는지 오류 주입/IO 계측으로 검사한다.

앱 소유 단일 조정자가 **SourceId 단위** 요청을 합친다. 원격 한 소스의 예약이 같은 분류의 수동 전용 다른 소스까지 열거하게 하지 않는다. 분류 수동 스캔은 활성 소스 각각에 대한 명시적 요청이다. 파일 유일성은 여전히 분류+PathKey이며 중첩 결과는 중복 저장하지 않는다. 비활성/제거 소스와 이번에 관찰하지 않은 범위는 Missing 근거가 아니다. 다른 분류의 동일 키 Missing도 같은 바인딩과 확정 증거가 있을 때만 적용한다.

Local watcher는 dirty 신호일 뿐 DB 쓰기가 아니다. 소스/구성 generation+dirty version을 사용하고 파일별 무제한 큐는 두지 않는다. 기술값 제안은 2초 debounce/최대 30초 합침이다. watcher를 먼저 붙인 뒤 기준 스캔하며, 중복/순서 역전/디렉터리 이동/overflow는 해당 소스 재검사로 수렴한다. 실행 중 신호는 완료 때 지우지 않는다. 수동 요청 우선, 같은 실행은 공유하고 소스 순환으로 편중을 막는다. 끔→켬은 새 기준 검사, 끔은 자동 열거 취소 요청·이미 시작한 DB 쓰기 완료 대기다.

자동 재시도는 켜진 소스에만 적용한다. 일시 오류는 30초→2분→10분 상한 backoff 제안이며 성공 시 초기화한다. 인증/권한/바인딩 문제는 자동 반복 대신 사용자 조치 대기다. 수동 취소는 즉시 자동 재시작하지 않는다. 완료 시각은 부분 실패로 갱신하지 않으며 오류 주입으로 재시도 폭주가 없는지 검사한다. 원격 수동 소스를 backoff라는 이름으로 자동 열거하지 않는다.

### 4. 연결·부재·후보 및 감상

연결 상태(Unknown/Available/Unavailable/AccessDenied/BindingChanged)와 항목 IsMissing을 분리한다. 연결 상태는 메모리 관찰이며 재시작 시 Available을 신뢰하지 않는다. UI의 기존 목록은 마지막 관찰값이며 실시간 온라인 목록이 아니다.

| 관찰 | DB·기록 처리 | 다음 동작 |
|---|---|---|
| 시작 오프라인/인증·권한 실패/열거 중 단절 | 기존 Present/Missing·기록·진행·선호 유지 | 상태 표시, 명시적 재확인 또는 켜진 정책의 재시도 |
| 원격 빈 목록/캐시된 목록 | 열거 성공만으로 누락 항목 Missing 금지 | 같은 binding의 부모 접근+개별 명시적 NotFound 증거가 추가로 필요 |
| 원격 NotFound | 서버/공유 자체 부재는 Unavailable; 정상 부모와 대상 확인이 상충/불명하면 보존 | provider가 authoritative 부재를 구분 못 하면 Missing을 자동 확정하지 않음 |
| 일부 열거 뒤 실패 | 바인딩 유지가 확인된 성공 Present만 반영 가능; 실패 prefix 부재 금지 | 부분 완료 표시. 바인딩 변경이면 그 소스 결과 전부 폐기 |
| 로컬 정상 조상 아래 명시적 부재 | 적용 직전 준비 상태·지원 조상·재등장 재확인 후 Missing | 외부 부재는 기록/진행을 삭제하지 않음 |
| 같은 key Present/Missing 충돌 | Present 우선, 불명 범위 보존 | dirty 유지해 새 관찰. rename 자동 승계 금지 |
| 재연결 | 온라인 추정만으로 Missing 해제/삭제 성공 처리 금지 | 바인딩 재확인 후 소스 정책에 맞춰 새 스캔 |
| DB 실패/적용 전 취소 | transaction rollback/해당 적용 묶음 미반영 | 오래된 결과 재사용 없이 새 관찰 |

원격 캐시를 우회하는 범용 보장은 없다. D16 정책은 “부재 증거를 제공 못하는 provider는 자동 Missing 제한”을 허용한다. 반복 관찰이나 일정 시간 경과만으로 부재를 확정하지 않는다. 관찰 직후 외부 변경까지 원자적으로 막는다는 보장은 없다.

D16 확정: 연결 불가/바인딩 미확인 소속 항목은 신규 랜덤에서 일시 제외한다. 여러 소스로 덮이면 같은 binding에서 접근 가능한 소스가 하나 있는 경우만 허용한다. 파일별 열기 실패는 현재 세션의 별도 실패 집합에 보관해 다음 추첨에서 반복 선택을 막는다. Seen/영구 제외/IsMissing을 대신 수정하지 않는다. 해제는 명시적 재확인 성공 또는 새 감상 세션이고, 자동 순환 재추첨은 여전히 없다. 수동/Back/Forward는 명시적 재확인 기회지만 실패 시 기존 cursor/Forward/Pending을 유지한다. UI는 기간상 후보 없음과 연결 문제로 일시 제외됨을 구분한다. 이 후보 계약 확장은 D16 승인 범위에서 T18A-4가 구현한다.

이미 Ready 후 끊김/디코딩 오류는 기존 Active Pending을 지우지 않는다. 마지막 유효 진행을 보존하며 정상 Leave에서 기존 억제 여부대로 저장한다. 재연결됐다고 자동으로 새 Visit를 만들거나 재생을 재시작하지 않는다. 다른 후보 준비 실패는 기존 감상 유지, 늦은 Ready는 token+binding generation 확인 후 폐기/해제한다.

ZIP은 중앙 디렉터리/암호화 검사와 페이지 seek가 원격 읽기를 유발한다. 앱 전체 추출 없이 기존 페이지 캐시를 유지하더라도 provider는 큰 부분 또는 전체 파일을 캐시할 수 있다. 영상은 기존 libVLC 파일 열기/버퍼·seek를 사용하고 URL 스트리밍 엔진으로 바꾸지 않는다. 자막은 해당 영상 폴더와 선택된 파일만 읽으며 자동 선택 순서를 유지한다. 자막 접근 실패는 “없음”과 구분하고 영상 실패로 합치지 않는다. 별도 다운로드 관리자·영구 미디어 캐시·RaiDrive 로그인/API는 추가하지 않는다.

### 5. 별칭·휴지통·삭제 복구

D14/D15 확정 정책이다. 매핑 경로와 UNC는 **서로 다른 PathKey/ItemId/기록**을 유지한다. 단, 삭제 안전성에 한해 현재 Windows가 알려준 매핑 대상+상대 경로가 정확히 같은 UNC인 경우를 검증된 별칭으로 취급한다. 호스트 IP/별명/DFS·내용·크기·시각으로 추론하지 않는다. 삭제 대상 바인딩 재확인 후 모든 분류의 동일 키 및 검증된 별칭 키를 캡처한다. 성공 정리는 이 사전 캡처 집합만 대상으로 하며 상태 병합 기능이 아니다.

미확인 별칭의 기록은 성공 정리 대상에 추정 추가하지 않는다. 원격 삭제의 영향 집합을 완전히 확인할 수 없을 때는 **모든 원격/Unknown 항목의 열기·checkpoint·스캔 반영·편집/새 삭제를 임시 격리**한다. durable 저널에 이 광역 격리 범위를 기록해 재시작에도 복원한다. 소스 밖 과거 원격 항목도 포함한다. 정상 결과 정리 후 해제하고 나머지 별칭은 후속 관찰로 Missing만 갱신하며 기록은 보존한다. 이 비용과 “미확인 별칭의 기록까지 지우지 않음”은 D15 사용자 승인에 포함된다. 로컬 하드링크 등의 기존 비식별 한계는 확대하지 않는다.

| 휴지통 capability / 작업 결과 | 처리 |
|---|---|
| Supported (해당 provider/대상에서 검증됨) | 기본 휴지통 유지, 매 작업 recycle-only 보장 필요. 과거 성공이 이번 성공 증거는 아님 |
| Unsupported | 휴지통 선택 불가·이유 표시. 사용자가 별도 영구삭제 확인을 해야만 새 작업 시작 |
| Unknown | 비파괴 조회·provider별 시험 없이 Supported 추정 금지. recycle-only가 보장되지 않으면 휴지통 작업을 시작하지 않음 |
| OS 발행 전 명확한 실패/취소 | 기록 보존·저널 정리 후 기존 방문 복귀; 오프라인을 Missing으로 바꾸지 않음 |
| OS 발행 후 응답 단절/timeout/불완전 callback | Unknown, 저널·격리·Pending 보존; 경로가 안 보인다는 이유로 성공 판단 금지 |
| 명확한 성공 callback/영구삭제 성공 응답 | Succeeded durable 기록→DB 원자 정리+AppliedDeletion→저널 제거→격리 해제 |
| 명확한 실패/취소 응답 | OS 변경 없음을 확인할 수 있을 때만 Failed/Cancelled; 불명은 Unknown |

현재 recycle flag/sink는 보존하되 원격 provider가 recycle-only를 지키는지 검증해야 한다. NAS/클라우드 자체 휴지통과 Windows Shell 휴지통은 같은 기능으로 간주하지 않는다. 영구삭제는 서버 측 보존/스냅샷 완전 제거까지 보장하지 않는다. 실패 후 자동 영구삭제·로그인·권한 상승·OS 삭제 자동 재실행은 금지한다.

저널 v2 제안: 기존 OperationId/모드/시각/phase와 실행 Path/PathKey에 binding 근거·generation, 격리 범위, 각 target의 ItemId+PathKey+기존 size/time을 추가한다. 삭제 전 대상 집합과 격리를 DB 경계에서 캡처하고 durable Prepared 쓰기 성공 전 OS 호출 금지다. 네트워크 탐색을 DB writer lock 안에서 하지 않는다. 별칭 확인 결과는 admission 아래 재검증하고 대상 캡처 때 일치 여부를 검사한다.

ApplyDeletion은 v2의 각 target 키를 검증하고 모든 대상 기록/진행/VisitCommit 정리 및 AppliedDeletion을 한 transaction으로 처리한다. 일부만 완료 처리하지 않는다. 재시작 복구는 저장된 집합을 사용하고 새 매핑에 대상을 다시 해석하지 않는다. Succeeded는 실제 파일 재조회 없이 DB 정리만 재시도; Prepared/Unknown은 원래 대상 정보를 표시하는 기존 사용자 확인으로 해결한다. 재매핑 상태에서도 새 대상 파일을 삭제하지 않는다. 살아 있는 OS 작업이 있으면 사용자 성공/실패 확인으로 먼저 격리를 풀 수 없다. 저널 손상으로 집합/범위를 못 읽으면 전역 차단을 유지한다.

v1 저널은 기존 단일 키 의미로 읽고 OS 재실행 없이 복구한다. v1이 원격일 가능성이 있으면 alias 증거가 없으므로 보수 광역 격리를 추가하되 성공 정리는 원래 targets에만 적용한다. v1 저널을 파일 IO로 보강·덮어쓰지 않는다. 미완료 성공 삭제 및 현재 Pending에 걸린 Unknown은 계속 종료 차단이다.

### 6. 지연 IO·직렬화·정상 종료

UI에서 remote Exists/GetAttributes/열거/연결 조회/open/dispose를 동기 호출하지 않는다. IO를 worker로 옮겨도 취소 가능성이 생기는 것은 아니다. native player/WPF 생성·UI 조작은 해당 스레드 계약을 따르고 무조건 Task.Run으로 옮기지 않는다. 각 IO의 소유 Task와 리소스가 누구에게 있는지 등록한다. timeout은 지연 안내/취소 요청 기준일 뿐 실제 완료나 해제 성공이 아니다. 종료 시간 상한은 보장하지 않는다.

| 상태 | 허용/차단 및 순서 |
|---|---|
| DB/저널 초기 복구 | 네트워크 스캔보다 우선; 격리 확보 후 목록 표시. 복구 UI가 해결 전 OS 호출을 만들지 않음 |
| 유휴 | 단일 admission에서 검사+등록을 await 없이 수행(T18A-2 조정자 lock). 앱 전체 스캔 하나, 소스별 요청 병합 |
| 열거 중 | 편집/감상/복구는 취소 요청 후 실제 Task drain; 반환되지 않으면 대기 상태 유지. 새로운 worker로 재시도 금지 |
| Apply 진입 이후 | 취소로 성공을 무효화하지 않고 transaction 결과까지 기다림. DB lock 안에서 IO/Dispatcher 대기 금지 |
| 감상 창 존재(Empty/Opening/Active/SaveFailed 포함) | 스캔 반영·재바인딩 보류, dirty만 유지. pause/숨김/Busy=false로 예외 허용 안 함 |
| 삭제·복구 | 실행 스캔 drain 후 배타 입장. 저널/OS 결과/DB 정리/재열기 끝까지 명령 소유권 유지 |
| durable 격리만 남음 | 관련 분류 전체 스캔 보류; 원격 광역 격리는 원격 포함 분류 보류. 무관한 로컬만 가능 |
| Hidden | D12에 따라 허용된 유휴 정책만 실행; 감상 중 보류. 오류·완료가 Show/Restore/소리 발생시키지 않음 |
| Closing | 새 사용자 명령·타이머·watcher·재시도 admission 차단, generation 무효화, 열거/준비 취소 요청 |
| 종료 drain | 실제 IO·명령·미디어/자막 해제·목록 읽기·삭제 결과/DB 경계 완료 대기; dirty 소진 대기는 하지 않음 |
| 지연/ExitBlocked | 원래 Task를 계속 추적. 복원·대기 상태 확인 가능, 중복 종료는 동일 작업 공유. 새 scan/open/OS 삭제 시작 불가 |
| drain 완료 | 기존 Leave 저장→미디어 해제→DB→트레이/후크→최종 전역 키 해제. 실패 시 기존 복원 경로 유지 |

진행 중 파일 IO를 버리고 DB부터 닫는 fire-and-forget, timeout 뒤 강제 Kill, 미완료 thread 강제 종료는 추가하지 않는다. 지연 안내 시점(예: 10초)은 기술값 제안이며 사용자 선택 timeout으로 종료 성공 처리하지 않는다. 영원히 반환하지 않는 provider라면 정상 종료도 대기할 수 있음을 표시한다. 별도 helper process 격리는 이번 범위 밖이며 필요 판정 시 별도 구조 검토다.

Closing과 감상 닫기가 서로 scan gate를 기다리는 순환을 만들지 않는다. 이미 감상 중이면 실행 스캔이 없어야 한다. Quick Hide는 기존 실제 복원 키+트레이 조건과 mute만 사용하며 Pending 확정이 아니다. ExitBlocked에서 Restore/SetExitRequested(false)만으로 감지를 재개하지 않는다. 실제 DB 생존·모든 IO 완료·감상 해제·삭제 복구 완료 및 명시적 정상 작업 복귀를 확인한 뒤 새 generation으로 재개한다. DB가 이미 닫혔으면 복원 UI만 유지한다.

### 7. 모델·migration 및 T18A-1 구현 계약

기존 SchemaV1.sql은 변경하지 않았다. T18A-1은 SchemaV2.sql과 같은 transaction 안의 루트 추출로 v1→v2를 적용한다. 신규 DB도 v1→v2 경로를 사용한다. 설정 두 bool만 추가하는 PR #21 안은 폐기한다.

| 저장 위치 | 구현 필드/제약 | 의미 |
|---|---|---|
| SourceRefreshPolicy (새 표) | SourceId PK/FK, ScanOnStartup bool, RefreshMode enum, IntervalHours nullable(있으면 1~168), LastCompletedAtUtc nullable | 소스별 자동 범위. 소스 삭제 시 정책만 cascade, 항목/기록 보존 |
| StorageBinding (새 표) | RootKey PK, Kind enum, ExpectedTarget nullable, EvidenceKind enum, Revision 정수≥0, RequiresConfirmation bool, Provider/Device/Volume nullable | 루트 재매핑 방지. 원격 비밀번호/token 저장 금지; 소스 제거에도 보존 |
| 기존 CategorySource/MediaItem 등 | 기존 ID·Path/PathKey·유일성·분류 관계 유지 | 바인딩은 루트로 조회, 항목-소스 관계 표/자동 상태 병합 없음 |
| 삭제 저널 (파일 v2) | §5 target별 key/바인딩·격리 범위 | SQL user_version과 독립 버전, v1 읽기 호환 |

RefreshMode=Manual이면 주기는 null, Events/Scheduled면 주기 필수라는 제약을 둔다. Kind는 마지막 분류 정보이며 실시간 연결 상태가 아니다. 사용자 확인의 영속 기록과 이번 프로세스의 실제 확인 여부를 구분하며 Unknown은 재시작 시 다시 미확인이다. 루트 revision은 명시적 설정/바인딩 변경 시 증가시키고 실행 generation은 메모리에 둔다.

migration은 네트워크 IO를 수행하지 않는다. 기존 소스·과거 항목의 서로 다른 루트를 Unknown/RequiresConfirmation로 추출하고 키/이력/진행/VisitCommit/AppliedDeletion은 그대로 보존한다. 이후 Local 확인 경로에서 D10 승인 기본값을 적용하되 수동 저장 여부를 구분해야 하므로 **정책 행 부재를 미선택 상태**로 사용한다. 사용자 저장 또는 최초 분류 후 승인 기본값을 채택할 때 행을 생성한다. 그 전 유효 정책은 Manual/시작 끔이다. 저장 실패 시 이전 정책/감지를 유지한다. 기존 SaveSettings(기간/Resume)와 별도 source-policy API를 사용해 상호 덮어쓰지 않는다.

migration 전체 rollback·재실행·상위 버전 거부·손상 표/설정 검증·외래키 검사를 유지한다. v2 DB를 v1 앱으로 열면 지원하지 않는 버전으로 거부하며 자동 downgrade 하지 않는다. 남은 저널을 복구하기 전에 스캔/감상 admission을 열지 않는다. 저널 v2 적용 코드가 준비되기 전 원격 삭제 UI는 활성화하지 않는다.

#### T18A-1 공개 API와 인계 경계

- Core `WindowsPath.Normalize`는 IO 없이 Windows 드라이브/UNC를 정규화하고 Path/PathKey/Root/RootKey를 반환한다. `IsSameOrDescendant`는 루트·구성요소 경계를 검사한다. `SourcePathRules.NormalizeFolder`는 같은 정규화를 제공한다. 기존 UI의 `NormalizeLocalFolder`는 이 함수를 사용하는 드라이브 전용 진입점이며 UNC UI는 T18A-5까지 연결하지 않는다. scanner의 관찰/조상 IO는 T18A-2에서 소스 범위로 변경했다.
- `StorageObservation.Classify`는 수집된 매핑 조회 결과·드라이브 종류·장치/볼륨/provider 근거를 분류한다. 실패한 WNet 조회 또는 Fixed 보고만으로 Local이 되지 않는다. 실제 WNet/장치 조회 worker는 T18A-2의 WindowsStorageProbe가 담당하며, 이 API가 네트워크를 호출하지 않는다. ExpectedTarget은 정규 UNC 대상 또는 로컬 장치 근거이며 비밀번호/token을 넣지 않는다. Provider/Device/Volume은 보조 근거이며 영구 물리 신원 증명이 아니다.
- `GetStorageBinding(path)`는 소스 소속 여부와 무관하게 루트 바인딩을 조회한다. `ConfirmStorageBinding(path, expectedRevision, observation, userConfirmed)`는 revision을 비교하여 오래된 결과를 거부한다. 기존 ExpectedTarget/근거가 달라지면 BindingChanged 오류로 보존하고, 원격/불명 최초 채택에는 사용자 확인을 요구한다. 같은 경로의 새 대상을 수용하는 API는 없다. 성공 시 revision 증가와 미선택 소스의 기본 정책 삽입을 한 transaction으로 처리한다. 이미 저장된 정책은 덮어쓰지 않는다.
- `BindingVerification`은 프로세스 내 확인 계약이다. 생성 시 Unconfirmed이고 `Invalidate()`로 generation을 증가시켜 끊김/설정 변경 전 결과를 버린다. `Observe(generation, observation, userConfirmed)`는 재매핑을 차단하며 `CanAccess(path, generation)`는 같은 루트의 과거 항목에도 적용한다. Unknown은 매 시작/관찰된 재연결 후 다시 확인한다. 이 객체의 호출/IO admission 연결은 T18A-2/4/5의 책임이며 T18A-2 스캔은 이 검사를 연결했지만 감상 IO 연결은 T18A-4에 남긴다. 조정자는 같은 admission 안에서 generation 확인→DB 확인→새 verification 생성/후속 IO를 연결해야 한다.
- `GetSourceRefreshPolicy(sourceId)`의 null은 미선택이다. `GetEffectiveSourceRefreshPolicy`는 미선택을 Manual/시작 끔으로 반환한다. `SaveSourceRefreshPolicy`는 기존 기간/Resume 설정과 독립 저장하며 Manual=null 주기, Events/Scheduled=1~168시간, Events=확인된 Local만 검증한다. LastCompletedAtUtc는 후속 조정자가 성공 완료 시 갱신할 값이다. 행 저장 자체는 스캔/타이머/watcher를 실행하지 않는다. 확인된 루트에 추가한 새 소스는 승인 기본값을 저장하며 중복 등록은 기존 정책을 보존한다. 기존 Events 정책을 원격/Unknown 루트로 옮기는 편집은 정책을 먼저 수정하기 전까지 rollback한다.
- 모든 기존 ID/Path/PathKey/이력/진행/VisitCommit/AppliedDeletion/AppSettings는 migration에서 재작성하지 않는다. 기존 소스 및 소스 밖 항목의 루트를 Unknown/RequiresConfirmation으로 생성하고 정책 행은 만들지 않는다. 신규 소스/항목 저장도 루트 행을 보장한다. 소스 삭제는 정책만 cascade하고 바인딩과 항목/기록은 남긴다. 새 쓰기의 경로 쌍은 공통 정규화와 일치해야 하며 기존 행의 키는 정규화 명목으로 바꾸지 않는다.
- `DeletionRecord` v2는 기존 공통 필드에 `Bindings: DeletionBinding[]`와 `QuarantineScope(Targets/RemoteAndUnknown)`를 추가한다. 각 binding은 `StorageBinding` snapshot과 `Generation≥0`, 각 target은 기존 ItemId/size/time과 `PathKey`를 가진다. 실행 경로와 모든 target의 루트 근거, 중복 ID/루트, enum/필수 필드를 검증한다. v1은 추가 필드 없이 읽고 쓸 수 있으며 읽기 과정에서 원본을 보강/덮어쓰지 않는다. SQL 버전과 저널 버전은 독립이다.
- **단계적 안전 경계:** 현재 삭제 실행은 기존 v1이다. v2 읽기는 지원하지만 T18A-3의 target별 원자 정리/광역 격리 연결 전에는 v2 저널을 전역 차단하고 자동/사용자 확인 복구·OS 실행을 거부한다. 손상 v2는 실행 경로만 복구해 부분 격리하지 않는다. v1의 원격 가능성 판단·보수 광역 격리와 v2 실제 복구는 T18A-3가 연결한다. 따라서 v1/v2 혼재 읽기 통과를 원격 복구 완료로 해석하지 않는다.

검증 근거와 실물 미검증은 `T18A_1_PATH_STORAGE_VALIDATION.md`를 따른다.

#### T18A-2 조정자 API와 인계 경계

- 앱은 `MainWindow.Scans` 하나를 사용하고 시작 복구 뒤 `Start()`한다. `ScanSourceAsync`/`ScanCategoryAsync`는 SourceId 요청을 합치며 실행 중 같은 소스·설정 generation의 수동 요청은 probe 대기부터 같은 관찰/수집 결과를 공유한다. 관찰만 하던 discovery에 합류하면 필요한 수집을 한 번 연결하며, 취소된 worker·다른 소스·이전 generation에는 합류하지 않는다. `CancelManual`은 실제 worker 소유권을 유지한 채 취소를 요청한다. 완료/취소된 요청의 Progress는 소유권에 따라 해제하며, 이전 worker의 정리가 새 요청의 대기자/콜백을 지우지 않는다. collector가 늦게 Report하거나 Progress<T>가 UI에 늦게 전달해도 worker/token 및 UI 요청 소유권을 확인한다. 기존 `LibraryScanner.ScanCategoryAsync`는 독립 테스트 호환용 transient 조정자다.
- `WindowsStorageProbe`는 WNet 매핑, 실제 장치 종류/볼륨 근거를 worker에서 수집한다. `CollectAsync`는 한 소스의 메타데이터만 반환한다. 관찰 전후 DB 바인딩 revision과 프로세스 generation을 검사한다. 원격 Missing은 반영하지 않고 로컬 Missing도 조상/개별 경로를 재확인한다. 부분 실패는 완료 시각을 갱신하지 않는다.
- `SavePolicyAsync`와 `ConfirmBindingAsync`는 배타 lease를 사용하며 확인은 실제 근거를 재관찰한다. Unknown은 명시적 확인을 요구한다. 미연결 UI를 대신해 원격을 암묵 승인하지 않는다. 로컬 근거가 입증된 기존 미설정 소스에만 D10 기본값을 저장하며 사용자 정책은 보존한다.
- `EnterExclusiveAsync`는 신규 scan admission 차단 → 취소 요청 → 실제 worker 완료 순서로 lease를 반환한다. 감상 창 전체 수명, 소스 편집, 기존 삭제 복구를 보호한다. `ConfigurationChanged`는 성공한 편집의 lease 안에서 호출하여 이전 관찰을 무효화한다. 삭제 격리 분류는 자동/수동 검사 모두 보류한다.
- `CloseAsync`는 sticky Closing, timer/watcher 폐기, queued 요청 취소와 실제 worker drain을 제공한다. Restore는 이를 해제하지 않는다. 기존 MainWindow/AppLifecycle의 저장·복구·목록 읽기 drain 다음 DB를 해제한다. 숨김은 Closing이 아니며 유휴 자동 검사가 UI를 노출하지 않는다. `App.RequestShutdownAsync`는 시작 실패를 포함한 앱 내부 종료 요청을 같은 AppLifecycle 종료 작업으로 연결한다. Scans 시작 뒤 WPF SessionEnding은 Cancel을 동기 설정한 후 이 비동기 경계로 들어간다. OnExit에서 async 대기하거나 Dispatcher를 막지 않는다. OS 요청은 거부될 수 있어 앱 종료 후 사용자가 로그오프/시스템 종료를 다시 요청해야 할 수 있으며 강제 OS 종료/프로세스 제거는 정상 저장·drain을 보장하지 않는다.
- 실제 원격 삭제/v2 복구, 별칭 정리, 감상 후보/엔진 IO, UNC·정책 UI는 T18A-3~5에 남긴다. 검증 범위와 실물 미검증은 `T18A_2_SCAN_LIFECYCLE_VALIDATION.md`를 따른다.

### 8. 지원·검증 기준 (T18A-1~2 자동 검증, 실물/후속 검증 대기)

| 환경 | 목표 지원 | 제약 / 반드시 실제 확인 |
|---|---|---|
| Local NTFS | 기존 등록·스캔·ZIP/영상/자막·Explorer·삭제 + 이벤트 | 기존 reparse/case 제한, 분리 드라이브·watcher 누락·휴지통 회귀 |
| SMB 직접 UNC | 공유 루트/하위 등록, 수동/선택 예약, 감상·위치 열기 | 인증은 Windows 선행, 서버/권한/단절 구분, Windows 휴지통은 별도 검증 |
| SMB 매핑 | 드라이브 경로 + 위 기능, 매핑/UNC 삭제 격리 | 동일 로그온 세션, 대상 변경 차단, 별칭별 상태 비공유 |
| RaiDrive/가상 드라이브 | Windows 파일 경로를 통한 위 기능 | backend/version별 seek·캐시·메타데이터·recycle 차이, placeholder 제한, 대상 미확인 시 사용자 확인 |

| ID | 오류 주입·자동 검증 조건 | Windows/NAS/RaiDrive 실제 확인 |
|---|---|---|
| N01 | UNC 공유 경계/한글/공백/슬래시/대소문자·경로 탈출·장치/ADS 거부, 기존 키 불변 | Explorer 단일 인수/공유 루트 및 provider case 충돌 |
| N02 | v1 데이터/빈 DB→v2, 실패 rollback/재시작/상위 버전 거부, 별도 설정 비덮어쓰기 | 사용자 DB 복사본, v1/v2 저널 혼재 복구 |
| N03 | WNet 실패를 Local로 오판 안 함, 같은 문자 대상 변경/늦은 결과 폐기, 과거 소스 밖 항목 차단 | 매핑 해제/다른 공유 재매핑, 앱 재시작·로그온 세션 차이 |
| N04 | 소스별 시작/모드/예약, 누락·overflow·폭주·편집 generation·수동 우선/취소 | 로컬 실제 watcher 수렴, 원격 수동 소스 자동 IO 0 |
| N05 | 빈 캐시/권한/오프라인/중간 NotFound/부분 결과에 거짓 Missing 0 | NAS 연결 끊기/재연결, RaiDrive 캐시 목록·비어 보이는 경우 |
| N06 | 한 명령 한 후보·일시 실패 억제/명시 재확인·Seen 불변, 기존 방문 보존 | 만화 페이지/영상 seek 중 단절, 자막 실패 분리 |
| N07 | Prepared 이전 OS 호출 0, 별칭 target별 원자 정리·광역 격리·v1 호환·손상 전역 차단 | 테스트 복사본을 매핑+UNC로 등록해 성공/실패/결과 불명 |
| N08 | 원격 NotFound/응답 유실 Unknown, 중복 복구 AppliedDeletion 멱등·재매핑 후 OS 재삭제 0 | NAS/각 RaiDrive backend의 recycle 지원/미지원·명시 영구삭제 |
| N09 | 멈춘 IO fake를 해제할 때까지 gate 유지, 늦은 Ready 폐기, 신규 작업 수 상한 | 느린 share에서 UI/복원 키 응답·open/Stop/Dispose 지연 |
| N10 | Active/Empty/Opening/checkpoint/SaveFailed 중 스캔 DB 반영 0, 닫기 성공 후 반영 | 감상 중 외부 교체 후 진행 부활 없음 |
| N11 | Closing 새 IO 0·실제 drain 후 DB 해제·중복 종료·DB 닫힌 ExitBlocked 재개 금지 | 숨김/트레이 실패+저장 실패+지연 IO 복합 확인 |
| N12 | 색인 미디어 본문 open 0, T17 필터/선택 보존·늦은 읽기 차단 | provider 전송량/seek/cache와 실제 ZIP/영상/자막·Explorer |

T18A-6에서 Windows x64/.NET 10 Release 및 기존 T03/T05/T06/T07/T09/T10/T11(T12~17 포함) 중 영향 회귀를 수행한다. 오류 주입 통과가 NAS/RaiDrive 실물 통과를 대신하지 않는다. 외부 삭제 시험은 새 테스트 복사본만 사용한다. 실물 환경이 없으면 환경·backend/version과 미검증 항목을 적고 사용자 실사용 확인으로 넘긴다. 기존 수동 미검증·승인 생략·AVI 조사 보류는 유지한다.

### 9. 확인한 기술 근거

2026-10-03 아래 공식 페이지 본문 접근을 재확인했다. API 설명을 근거로 안전 경계를 설계했으며 provider 동작 검증을 대신하지 않는다. 기본값/주기와 D14~D16은 2026-10-04 사용자 승인 정책이다.

- [WNetGetConnectionW](https://learn.microsoft.com/en-us/windows/win32/api/winnetwk/nf-winnetwk-wnetgetconnectionw): 매핑 대상 조회와 로그온 세션 제한. 모든 가상 provider의 식별 성공을 보장하지 않는다.
- [FileSystemWatcher](https://learn.microsoft.com/en-us/dotnet/api/system.io.filesystemwatcher?view=net-10.0): 이벤트 중복·누락을 전제로 재관찰한다.
- [Canceling Pending I/O Operations](https://learn.microsoft.com/en-us/windows/win32/fileio/canceling-pending-i-o-operations): 취소 요청과 IO 완료는 별개다.
- [IFileOperation.SetOperationFlags](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifileoperation-setoperationflags): recycle 관련 플래그만으로 원격 휴지통 존재를 주장하지 않는다.
