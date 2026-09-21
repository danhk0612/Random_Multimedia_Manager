# T12 삭제 구현 및 검증

## 구현 경계

`DeletionService`는 앱의 단일 `LibraryDatabase`와 삭제 저널을 공유한다. 감상 창의 삭제 확인은 휴지통을 기본 선택하고 영구 삭제를 명시적으로 선택할 수 있다. 취소/창 닫기에는 삭제 명령을 보내지 않는다. 확인에는 실제 파일 및 같은 경로의 모든 분류 기록/진행에 대한 영향을 표시한다.

`SessionCoordinator.DeleteCurrentAsync`는 기존 명령 admission을 통해 탐색과 삭제를 직렬화한다. `LibraryDatabase.CaptureDeletion`은 DB writer lock 안에서 대상 ItemId·메타데이터를 캡처하고 PathKey를 격리한다. 격리 경로의 스캔 반영·신규 항목 생성·checkpoint·CommitVisit·선호 편집은 저장 계층에서 거부한다. 후보/수동/이전·다음 열기도 격리를 확인한다. 기존 모달 감상 창 및 UI Busy는 유지한다.

순서는 격리 → Prepared durable 기록 → 진행/원래 재생 상태 캡처 → 미디어 해제 → OS 결과 → 즉시 세션 성공 반영 → 결과 durable 기록 → 기존 ApplyDeletion → 저널 제거 → 격리 해제다. 저널은 flush한 임시 파일을 Windows `MoveFileEx(REPLACE_EXISTING | WRITE_THROUGH)`로 교체한다. `Prepared` 기록 실패 시 OS 작업을 하지 않는다.

휴지통은 STA 스레드의 `IFileOperation`과 `FOFX_RECYCLEONDELETE`를 사용한다. `PreDeleteItem`에서 recycle 플래그 없는 작업을 거부하며 영구 삭제 fallback을 실행하지 않는다. 영구 삭제는 `DeleteFileW` 결과를 사용하여 이미 없는 파일을 성공으로 취급하지 않는다. 접근/잠금 실패와 부재를 구분한다. 폴더나 reparse 경로는 삭제하지 않는다.

성공하면 현재 Pending을 제거하고 같은 PathKey의 슬롯을 tombstone으로 만든다. cursor/Seen을 유지하고 자동 다음 재생을 하지 않는다. `ApplyDeletion`은 캡처한 모든 ItemId의 이력·진행·VisitCommit만 제거하고 Missing을 설정하며 선호/영구 제외를 유지한다. 실패/취소 시 같은 VisitId·억제값·최종 진행을 보존하여 재열기를 시도한다. 재열기 실패 시에도 Pending을 보존하고 이후 정상 이탈에서 기존 방문을 저장한다. 이전 미디어 operation token의 콜백은 무효다.

## 복구

앱 시작 시 메인 창을 열기 전에 저널을 검사한다. Succeeded는 기존 AppliedDeletion 멱등 정리를 재시도하고 Failed/Cancelled는 기록을 지우지 않고 저널만 정리한다. AppliedDeletion 표식은 보존한다. 저널 제거가 저장장치에 반영되기 전 전원 단절로 저널이 재등장하더라도 새 감상 기록을 다시 지우지 않도록 하기 위한 선택이며 스키마는 바꾸지 않는다.

Prepared/Unknown은 사용자 성공 또는 실패/취소 확인 전 격리한다. OS 성공 뒤 결과 저널 쓰기 실패도 Prepared로 남을 수 있으므로 파일 부재만으로 성공을 추정하지 않는다. 손상 저널에서 Path/PathKey만 신뢰성 있게 읽히면 그 경로만, 경로도 알 수 없으면 라이브러리 전체 쓰기·감상 시작을 보류한다. 대상 집합을 알 수 없는 손상 저널은 성공 정리 대상을 추측하지 않으며, 실패/취소 확인으로 기록을 보존하거나 미확인 상태를 유지할 수 있다.

감상 창의 ‘삭제 복구 확인/재시도’와 다음 시작에서 복구할 수 있다. 현재 방문이 Unknown 경로에 있으면 정상 이탈도 확인 전 차단한다. Succeeded 후 DB 실패는 현재 Pending이 이미 없으므로 다른 경로를 감상할 수 있다. 재시작은 세션을 복원하지 않고 저널의 DB 정리만 수행한다. **복구 코드에는 OS 삭제 재실행이 없다.** 같은 경로에 새 파일이 생겨도 지우지 않으며 격리 해제 후 수동 스캔으로 존재를 갱신한다.

## 자동 검증

- `Data.Tests/T12Verification.cs`: 임시 DB와 생성한 테스트 파일, 실제 저널/저장/조정자 및 OS 결과 대역. 다중 분류 정리, 취소/실패, 같은 방문 복귀와 재열기 실패, 저널 쓰기/제거 실패, DB 트랜잭션 실패, 재시작 복구, Prepared/Unknown/손상 격리, Applied 멱등성, 새 파일 보존, Busy/중복/늦은 checkpoint를 검사한다.
- `Viewing.Tests/T12NativeVerification.cs`: Windows 실제 휴지통/영구삭제, 이미 없는 파일, 잠금/ACL 권한 실패, fallback veto, 실제 ZIP/LibVLC 리소스 해제 후 삭제, 취소 후 동일 Visit·일시정지·음소거·볼륨 복원, 확인창 기본값·닫기 취소를 검사한다. 기존 T11 실제 어댑터/공통 UI 검사와 함께 실행한다.
- 기존 T03/T05/T06/T09/T11 워크플로로 Windows Release 빌드와 저장·스캔·세션·감상·미디어 회귀를 수행한다. 원본 미디어나 실사용 DB는 테스트에 사용하지 않는다.

```powershell
dotnet build RandomMultimediaManager.sln -c Release
dotnet run --project tests/RandomMultimediaManager.Data.Tests -c Release
dotnet run --project tests/RandomMultimediaManager.Viewing.Tests -c Release
```

최종 실행 결과는 CURRENT_STATE.md의 T12 절에 기록한다. 자동 UI 조작과 실제 사용자 수동 확인은 구분한다.

## 사용자 Windows 수동 확인 — 미실시

별도 폴더에 테스트용 ZIP/CBZ와 MP4/MKV 복사본만 준비하고 해당 폴더를 테스트 분류의 소스로 등록한다. 원본 파일을 대상으로 아래 검사를 하지 않는다.

```powershell
git clone --branch task/t12-file-deletion https://github.com/danhk0612/Random_Multimedia_Manager.git RMM-T12
cd RMM-T12
dotnet run --project src/RandomMultimediaManager.App -c Release
```

| 확인 | 기대 결과 |
|---|---|
| 만화/영상 감상 중 삭제 열기 → 취소 또는 X | 파일·같은 방문·이번 제외 유지 |
| 기본 휴지통 선택으로 삭제 | 휴지통에서 복사본 확인 가능, 현재 화면 비움, 자동 다음 없음 |
| 명시적 영구 삭제 선택 | 테스트 복사본 제거, 현재 화면 비움 |
| A→B에서 B 삭제 후 이전/다음 | 삭제 슬롯을 건너뛰며 유효 항목 탐색 |
| 같은 파일을 두 분류에 등록한 후 삭제 | 두 분류 모두 삭제 결과 반영, 선호/영구 제외 보존 (기록/진행은 자동 DB 검사 근거) |
| 테스트 파일 삭제 실패 후 감상 | 오류 안내, 같은 방문과 이번 제외 보존, 가능하면 기존 위치로 복귀 |
| 앱 재시작 | 완료된 삭제 재실행 없음, 미해결 저널이 있으면 복구 확인 |

DB/저널 장애는 자동 주입 검사로 검증하며 실사용 DB 권한을 변경해 재현하지 않는다. T09~T11의 수동 생략 승인을 T12에 적용하지 않는다.

## T14 이후 연결점

T14 PR #15 문서는 수정하지 않았다. 향후 T15/T16은 `SessionPhase.Deleting`, 감상 창의 진행 명령 Task, `DeletionService.Pending`, `LibraryDatabase.IsDeletionBlocked`를 대조하여 종료 순서를 연결해야 한다. 삭제 중 Close는 현재 명령 완료를 기다린다. 현재 Unknown Pending은 저장을 차단하며, Succeeded/DB 실패의 durable 저널은 재시작에서 정리할 수 있다. 빠른 숨김/트레이/강제 종료/새 종료 정책은 T12에 포함하지 않는다.

공식 API 확인: [PostDeleteItem의 실제 삭제 결과/휴지통 항목](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifileoperationprogresssink-postdeleteitem), [IFileOperation 플래그](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifileoperation-setoperationflags), [PreDeleteItem](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifileoperationprogresssink-predeleteitem), [TRANSFER_SOURCE_FLAGS](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/ne-shobjidl_core-_transfer_source_flags). 2026-09-19/21 접근 확인.
