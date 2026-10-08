# T18A-3 원격 삭제·별칭 격리·v2 복구 검증

기준 main `b5debb3`, T18A-2 PR #24 merge `d2848f5`. 작업 브랜치는 `task/t18a-3-network-deletion`, PR #25는 main 미통합이다. D14/D15와 DATA_AND_RANDOM_POLICY T18A §2/5~8의 승인된 계약만 구현했다. 기존 미검증/승인 생략과 AVI 조사 보류는 유지한다.

## 구현 및 안전 경계

- 실제 OS의 Windows 매핑 대상+상대 경로가 정확히 같은 UNC인 항목만 모든 분류에서 사전 캡처한다. 호스트 IP/이름·DFS·내용/size/time은 별칭 추론 근거가 아니다. 로컬 삭제는 실행 루트만 관찰하며 원격 삭제는 등록된 과거 항목의 루트 근거를 수집한다. 미확인/변경/접근 실패 alias 루트는 정리 집합에 넣지 않는다.
- 루트 관찰은 worker에서 수행하고 캡처 전/OS 직전 재검증한다. DB writer에서 네트워크를 호출하지 않는다. 캡처된 ItemId/PathKey/size/time·바인딩/revision/generation·격리 scope를 v2 Prepared에 durable 저장하기 전 OS 호출은 없다. 같은 live Prepared는 한 번만 실행할 수 있다. 재시작 저널을 Execute로 넘겨도 OS 호출을 거부한다.
- 원격/불명은 RemoteAndUnknown 광역 격리한다. 소스가 제거된 과거 원격/Unknown 항목도 포함한다. 신규 열기/후보·checkpoint·기록/선호·분류/소스/정책 편집·스캔 반영을 기존 게이트와 DB transaction에서 차단한다. 격리 합집합은 모든 미해결 저널에서 다시 계산하고 정상 결과/저널 제거 뒤 해제한다. 추정 별칭에는 기록 정리나 Missing을 적용하지 않는다.
- Supported/Unsupported/Unknown과 recycle-only 보장 여부를 분리했다. Unsupported/보장 근거 부재는 실행하지 않는다. 기존 로컬 Shell flag/sink는 유지한다. **실제 NAS/RaiDrive backend의 recycle-only 검증 자료가 없어 원격 기본 판정은 Unknown/실행 차단이다.** 테스트에서 주입한 Supported는 provider 지원을 증명하지 않는다. Unknown을 영구삭제로 자동 변경하지 않고 기존 확인창의 명시적 별도 영구삭제 선택만 받는다.
- OS 발행 후 원격 NotFound·단절·응답 유실·불완전 결과는 Unknown이며 시간 경과/부재는 성공 근거가 아니다. 명확한 성공 응답을 durable 기록한 후 캡처 target별 키 검증·전체 Missing/기록/진행/VisitCommit 정리·AppliedDeletion을 하나의 DB transaction으로 처리한다. 일부 대상이나 표식만 성공하지 않는다.
- v1/v2 시작 복구는 저장된 집합의 DB 정리만 수행한다. 새 매핑이나 alias를 다시 해석하지 않고 OS 삭제/파일 조회를 하지 않는다. v1 원격 가능성은 보수 광역 격리하지만 원래 target만 정리하며 파일을 자동 보강/재작성하지 않는다. 손상 v2/알 수 없는 버전은 전역 차단하고 성공 target 추측을 거부한다. AppliedDeletion은 저널 제거 뒤에도 유지한다.
- Unknown은 기존 Pending/억제/마지막 진행을 보존한다. 실패/사용자 실패 확인 후 재열기는 원래 실행 binding을 다시 확인하며 remap/Closing이면 새 대상을 열지 않고 같은 방문을 보존한다. 성공은 검증된 alias 슬롯만 tombstone으로 바꾸며 자동 다음 재생/새 방문은 없다.
- DeletionService.BeginClosing/DrainAsync와 기존 viewing 명령·삭제 복구 대기를 연결했다. 실제 IO가 살아 있을 때 성공/실패 확인으로 격리를 풀지 못한다. Closing 뒤 새 probe/삭제 IO는 시작하지 않고 실제 반환까지 기다린다. Quick Hide·mute/복원·ExitBlocked·DB 해제 순서는 기존 계약을 유지한다. timeout 성공/강제 종료는 없다.

공개 API와 T18A-4/5 경계는 DATA_AND_RANDOM_POLICY T18A §7을 따른다. 원격 최초/Unknown process 확인을 대신하는 UI나 provider 로그인은 추가하지 않았다. 일반 감상 후보/엔진 IO·UNC/설정 UI·배포는 이번 범위가 아니다.

## 자동 검사: 오류 주입과 실제 로컬 실행 구분

`Data.Tests/T18A3Verification.cs`는 실제 SQLite/저널을 사용하며 연결 근거와 OS 결과는 대역이다. Windows 실제 NAS/RaiDrive를 이용하는 검사가 아니다.

| 검사 | 확인한 결과 |
|---|---|
| N07 정확한 매핑/UNC 집합·다중 분류·IP 추정 별칭 | 캡처 3항목/2루트, 소스 밖 Unknown 광역 hold, 확인된 Local 보존, 추정 별칭 이력 보존 |
| N07 Prepared 실패/저장 후 acknowledgement 유실 | OS 0회, 의도 부재는 격리 해제, durable Prepared는 격리 보존 |
| N08 recycle capability 주입 | Unknown/Unsupported/보장 없는 Supported는 Prepared/OS 0회, 실패 후 영구삭제 호출 0회 |
| N08 NotFound·응답 유실·재시작 | Unknown/Present·과거 기록·진행 유지, 원격 광역 격리 복원, 사용자 실패 확인으로만 해제 |
| N08 재매핑 전/후 복구 | OS 직전 remap은 OS 0회, 복구는 저장된 target만 정리하고 새 alias/OS 재삭제 0회 |
| N08 미디어 해제 시점 재매핑 | 원래 Pending 유지, OS 0회, preparer 추가 호출 0회, 마지막 유효 진행으로 정상 Leave |
| N07 target 키 불일치·늦은 SQL trigger 실패 | 전체 target의 기록/Missing 및 AppliedDeletion rollback, Succeeded 시작 복구의 DB replay만 허용 |
| N08 저널 제거 실패·오래된 저널 재등장 | hold 유지, 멱등 재처리, 복구 후 새 방문/진행 보존 |
| N07 v1/v2·손상 저널 | v1 Prepared bytes 보존/원래 집합만 정리, 손상 v2 전역 hold/성공 추측 금지 |
| N09 취소 불가 지연 OS 대역 | 실제 반환 전 drain/실행 Task 미완료, live 성공/실패 확인·재실행·Closing 새 IO 거부, 실제 완료 뒤 정리 |

`Viewing.Tests/T12NativeVerification.cs`는 Windows 로컬 임시 복사본의 실제 Shell 휴지통/DeleteFileW·잠금/ACL·ZIP/libVLC 해제와 실패 복귀를 검사한다. 원격 오류 코드 2/3/5/53/64/121의 Unknown/Missing=false 판정은 **오류 주입**이다. 기존 T15/T16/T18A-2 native 회귀의 실제 WPF·트레이/전역 키·지연 삭제/스캔·종료/복원은 로컬 러너 검사이며 사용자 로그오프나 원격 장비 검증은 아니다.

## Windows Release 및 영향 회귀

검증 코드 `7ab2448`(제품 코드는 `cecde62`와 동일, 마지막 차이는 scanner fixture/편집 거부 assertion 1파일). Windows Server 2025 Datacenter 10.0.26100 x64 / .NET SDK 10.0.401.

Release 빌드 **경고 0·오류 0**, N07~N09 새 시나리오 **10개 통과**, 영향 CI **5개 모두 최초 실행 통과**.

| Windows 실행 | 결과 |
|---|---|
| [T03 저장/새 N07~N09/셸 시작·종료 37786438406](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/37786438406) | 통과 |
| [T05 스캔/조정자·watcher·취소·격리 37786438394](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/37786438394) | 통과 |
| [T06 Pending/기록/세션 37786438355](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/37786438355) | 통과 |
| [T09 실제 영상 37786438380](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/37786438380) | 통과 |
| [T11 WPF·삭제/종료·숨김/복원·만화/영상/자막 37786438351](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/37786438351) | 통과 |

T11 job `113342280311`에서 실제 로컬 휴지통/영구삭제·잠금/ACL·미디어 해제, Succeeded/Failed/Cancelled 지연 삭제 중 종료의 실제 완료 대기, T16 모달/키 복원, T18A-2 SessionEnding 대역/scan drain도 통과했다. 현재 헤드에서 재실행은 없으며 아래 초기 개발 실패 및 이전 T18A-2 재실행 이력은 별도 보존한다.

첫 `e441fbe`는 Release 빌드 경고 0·오류 0이었으나 새 분류 격리 검사에서 SQLite read에 활성 transaction을 전달하지 않아 T04 저장 및 basic scan이 실패했다([T03 37785292349](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/37785292349), [T05 37785292262](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/37785292262)). `159a070`에서 모든 격리 read를 같은 transaction으로 수정했다.

제품 코드 `cecde62`의 T03/T06/T09/T11은 통과했고 [T05 37786018836](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/37786018836)는 기존 GenerationAndQuarantine fixture가 격리 후 소스를 추가해 실패했다. `7ab2448`에서 sibling source 준비를 격리 이전으로 이동하고 격리 중 편집 거부 assertion을 추가했다. 기존 stale revision/분류 스캔 차단/소스 비활성 검사는 보존했다. 제품 코드를 완화하거나 이 검사를 생략하지 않았다.

기존 T18A-2 T11의 네이티브 파일 선택창 복원 실패(`native dialog restored False`)와 제품/테스트 변경 없는 1회 재실행 통과 이력은 [T18A_2_SCAN_LIFECYCLE_VALIDATION.md](T18A_2_SCAN_LIFECYCLE_VALIDATION.md)에 보존한다. 이 작업으로 이전 실패/미검증을 통과로 바꾸지 않는다.

```powershell
dotnet build RandomMultimediaManager.sln -c Release
dotnet run --project tests/RandomMultimediaManager.Data.Tests -c Release -- --t18a3
dotnet run --project tests/RandomMultimediaManager.Data.Tests -c Release
dotnet run --project tests/RandomMultimediaManager.Scanner.Tests -c Release -- lifecycle
dotnet run --project tests/RandomMultimediaManager.Viewing.Tests -c Release
```

## 실제 장비: 미검증 및 후속 조건

실제 NAS/SMB share, RaiDrive 설치·backend/version·원격 계정, 사용자 로그오프/시스템 종료 환경은 제공되지 않았다. 매핑/UNC alias·remap·응답 유실·대기 IO·provider recycle 판정의 오류 주입을 실제 장비 통과로 보고하지 않는다. 실제 원격 휴지통 Supported를 등록/주장하지 않았다.

T18A-5의 명시적 binding 확인 UI 및 T18A-6의 실물 검증 환경에서 새 테스트 복사본만 사용한다. 같은 파일의 매핑/UNC 등록, 원래 대상 및 remap, share 연결 단절/복구, backend별 recycle-only/명시 영구삭제, 미확인 alias의 기록 보존, 느린 IO 중 복원 키와 정상 종료 완료를 확인한다. 확인할 수 없는 backend는 계속 Unknown/휴지통 차단이다. 서버 보존/스냅샷까지 완전 제거하거나 숨겨진 대상 교체/최종 검사 뒤 경합을 탐지한다고 보장하지 않는다.

PR #25 검토·main 통합 전에는 T18A-4를 시작하지 않는다. 배포는 T18A-6 이후 T18의 별도 승인 범위다.
