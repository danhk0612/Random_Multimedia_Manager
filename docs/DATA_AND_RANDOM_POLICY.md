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
