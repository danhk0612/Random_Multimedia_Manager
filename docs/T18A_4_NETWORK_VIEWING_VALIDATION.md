# T18A-4 네트워크 감상 검증

기준 main `9f85fc7`, T18A-3 PR #25 merge `7437e9d`. 이 작업은 감상 후보·바인딩·지연 IO만 연결하며 T18A-5 UI/T18A-6 실물 통합·배포는 시작하지 않는다.

## 구현과 검증 경계

- `ViewingAccess`는 감상 창의 scan exclusive lease 안에서 루트 근거와 소스 접근 메타데이터만 확인한다. 후보 결정마다 바인딩 집합을 읽고 같은 루트의 접근 가능한 활성 소스가 하나 이상인 항목만 허용한다. 미확인 원격 최초 채택은 하지 않는다. Unknown은 이번 프로세스의 명시 확인을 요구하고 관찰된 전체 접근 실패 이후 같은 확인을 재사용하지 않는다.
- 실제 열기 전후 binding revision/generation과 live 삭제 격리를 검사한다. 늦은 Ready는 실제 Dispose 완료까지 소유하며 기존 cursor/Forward/Pending을 유지한다. 실패 집합은 세션 메모리이며 Seen/영구 제외/Missing/기록을 바꾸지 않는다. 명시적 열기 성공과 새 세션으로 해제한다. 한 명령 한 후보, 자동 연쇄 추첨·재연결 자동 재생 없음.
- ZIP 페이지 작업은 페이지별 중복 소유를 합치고 프리로드 수를 제한한다. 비동기 해제는 신규 읽기를 차단·취소 요청한 뒤 실제 읽기와 압축 해제를 기다린다. 현재 페이지 실패는 기존 페이지/방문을 유지한다.
- 영상 경로 메타데이터·준비 Play/복원 seek·감상 Play/seek·자막 AddSlave·Stop/Dispose는 worker에서 실행한다. HWND 생성/연결/제거와 UI/방문 상태는 Dispatcher에서 유지한다. 영상 IO의 소유 Task가 끝나기 전에 native/임시 자막 파일을 해제하지 않는다. native timeout은 해제 성공이 아니다. 오류 후 진행은 마지막 유효 위치를 유지한다.
- 자막 검색 접근 실패를 빈 목록으로 처리하지 않으며 읽기/인코딩/트랙 적용 실패는 영상 Pending과 분리해 안내한다. Closing 이후 검색 결과의 UI 적용·후속 자막 읽기/AddSlave·신규 미디어 IO를 차단한다. 이미 발행한 작업은 실제 반환과 정리까지 기다린다.

## 최초 제출 자동 검증

검증 코드/테스트 SHA **`9862d87d50518afa38c8dc1875ab06a0ad2e2d6c`**. Windows x64 / OS 10.0.26100 / .NET SDK 10.0.401의 Release 빌드 **경고 0·오류 0**, 영향 CI **7개 모두 통과**했다. 이 SHA에서 재실행 없이 성공했다. 최초 제출 헤드 `e025220`까지 후속 변경은 문서 5개뿐이다. main `f44ed67` 검토 지시 이후의 코드 보완과 최종 결과는 아래 별도 절을 따른다.

| 최종 코드 SHA의 Windows CI | 결과 |
|---|---|
| [T03 저장·N06/N09/N11·셸 37854095795](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/37854095795) | 통과 |
| [T05 스캔/조정자 37854095766](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/37854095766) | 통과 |
| [T06 후보·Pending·기록 37854095764](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/37854095764) | 통과 |
| [T07 ZIP/페이지·지연 해제 37854095759](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/37854095759) | 통과 |
| [T09 영상/native 37854095815](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/37854095815) | 통과 |
| [T10 외부 자막 37854095772](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/37854095772) | 통과 |
| [T11 감상·삭제·숨김/복원·종료 및 미디어 37854095880](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/37854095880) | 통과 |

| 코드/조건 | 검사 |
|---|---|
| Data.Tests T18A4Verification | N06 소스 중첩·연결 제외·Unknown 재연결 확인·실패 집합/재확인·Back/Forward·Seen/DB 불변·새 세션/기간 차이 |
| Data.Tests T18A4Verification | N03/N09 늦은 Ready와 revision 변경 폐기, 실제 해제 전 Busy·기존 Pending/진행/기록 유지 |
| Data.Tests T18A4Verification | N11 취소 무시 probe 완료까지 명령/DB 소유 유지, Closing 후 미디어 open 0 |
| Comic.Tests T08Checks | 실제 ZIP + 결정적 페이지 지연, 중복 읽기 합침·Closing 읽기 0·실제 drain 후 파일 잠금 해제·Active 읽기 실패의 진행 보존/해제 분리 |
| Viewing.Tests T18A4NativeVerification | 실제 libVLC/WPF + Play/seek/자막/Stop 발행 경계 지연·Closing 후 자막 적용 0, UI 응답·신규 IO 거부·drain 후 HWND 제거 |
| 기존 영향 회귀 | T03/T05/T06/T07/T09/T10/T11, 삭제 격리 bulk SQL·Pending·기록·만화·영상·자막·숨김/복원·종료 |

연결 대상·가용성·준비 실패·지연은 결정적 오류 주입이다. SQLite/저널과 Windows 로컬 ZIP/영상/자막/native/WPF는 실파일/실제 API를 사용한다. 오류 주입 결과는 원격 장비 지원 증거가 아니다.

## 실물 미검증 및 보존 사항

실제 NAS SMB 매핑/UNC, RaiDrive backend/version·캐시/전송량·읽기/seek/Stop/Dispose·재매핑·권한·단절/재연결·숨김 복원 응답은 장비 접근이 없어 미검증이다. 원격 휴지통 Unknown/차단은 T18A-3 그대로다. OS가 숨기는 대상 교체와 최종 검사/실제 IO 사이 경합을 원자적으로 탐지한다는 보장은 없다. 사용자 확인/UNC/소스 정책 UI는 T18A-5에 남는다.

기존 수동 미검증·사용자 승인 생략·T11 파일 선택창 복원 최초 실패와 1회 재실행 이력·AVI 음성 조사 보류는 기존 검증 문서 그대로 유지한다. 신규 테스트 복사본만 사용하며 사용자 운영 파일을 수정하지 않는다.

## 결과 근거와 초기 실패

T03 run의 job 로그에서 T18A4Verification 7개 PASS를 확인했다. T11 job `113573989104`에서 새 WPF/native seek·자막·Stop·Play drain과 Closing 자막 차단, 기존 T12~17 및 T18A-2 회귀가 통과했다. T07에서 페이지 중복 읽기/지연 drain/진행 보존/실제 압축 해제 6개 assertion이 통과했다. 지연 hook는 native 발행 경계의 작업을 보류한다. 해제/Closing 시 후속 발행을 거부하고, 이미 발행된 native 작업·기존 실파일 회귀의 실제 Stop/Dispose는 완료까지 기다린다. 이 결과를 실제 원격 read/seek 지연 시험으로 주장하지 않는다.

- T18A-3 bulk 회귀는 100/10,000항목·48소스에서 snapshot=3, DeletionPaths=2, Pump=3, 수동 요청=59 SQL을 유지했다. StartRandom은 추첨된 단건의 준비 전후 live 검사 2개를 추가해 **20/20 SQL**이다. 후보별 SQL은 없다. 새 ViewingAccess의 같은 루트 1/48소스 판정은 모두 **3 SQL**이며 소스별 메타데이터 확인과 DB 집합 조회를 분리한다.
- 최초 `81947f4`는 만화 소유권 코드 적용 위치와 fixture DB API 호출의 컴파일 오류로 실패했다. `9d2c3ae`에서 위치를, `fc3e9ed`에서 fixture 호출을 수정했다. 이 실행을 통과로 간주하지 않는다.
- 기존 bulk 검사의 “선택 단건 binding SQL 최대 1”은 새 pre/post live 확인을 포함하지 않아 실패했다. `e7bd8ca`에서 선택 단건의 고정 최대 3 및 100/10,000 전체 조회 수 동일성을 검사하도록 갱신했다. 항목별 조회를 허용한 변경이 아니다.
- 새 async ZIP 지연 검사를 module initializer에서 동기 대기해 테스트 실행이 정체됐다. `326ac7a`에서 Main으로 옮겨 모듈 초기화 완료 후 실행한다. 무작위 GUID 순서에 의존한 새 후보 fixture의 수동 열기 assertion도 `f7521de`에서 고정 ID로 결정화했다. 테스트 결함을 NAS/제품 IO 정체로 기록하지 않는다. 최종 SHA에서는 이 검사와 영향 CI 전체가 정상 종료했다.

최초 제출 당시 PR #26은 구현/검증 완료·재검토 대기이며 미병합이었다. 다음 기준은 PR #26 통합 이후 최신 main이며 T18A-5/6·배포는 이번 작업에서 시작하지 않는다.

## PR #26 병합 전 보완 (main f44ed67)

main의 병합 검토 지시를 기존 브랜치에 반영했다. 검토 헤드 `e025220`과 최초 검증 코드 `9862d87`의 기록은 위에 보존하며, 보완의 재현·최종 검증은 이 절을 기준으로 한다.

수정 전 제품 코드에 회귀 검사만 추가한 `ff616495f954d127bfd322559eb01b610588e89c`에서 두 결함을 Windows에서 재현했다.

- [T03 37897106374](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/37897106374): 소스 루트/바인딩은 정상이고 B 파일의 준비 후 메타데이터 접근만 실패하도록 주입했다. Ready 실제 해제를 기다리고 기존 방문을 보존한 뒤 파일 접근을 복구했을 때, 다음 랜덤의 “재시도 0” assertion이 실패했다. T06/T11도 같은 Data.Tests assertion에서 실패했다.
- [T09 37897106340](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/37897106340): Windows 로컬 실파일/libVLC로 끝 위치 복원 후 동일 완료 위치의 SeekAsync를 실행했다. 요청은 수락됐지만 native seek 0 assertion이 실패했다. 첫 실패에서 종료했으므로 수정 전 PlayAsync 재생 결과를 통과/실패로 판정하지 않는다.

`SessionCoordinator.Move`는 준비 후 재확인의 false 및 일반 예외 결과를 현재 세션의 실패 집합에 반영한다. OperationCanceledException과 취소/Closing 요청 이후 반환된 일반 예외·false는 Cancelled로 처리하며 실패 집합을 바꾸지 않는다. 준비 전 일반 예외도 취소 요청을 먼저 확인한다. 기존 finally의 실제 Ready 해제 완료까지 Busy/소유 Task를 유지한다. Seen·Missing·영구 제외·DB 기록은 수정하지 않는다.

`PreparedVideo.SeekAsync`는 정규화된 target이 복원된 완료 위치와 같으면 native seek 없이 성공을 반환한다. 내부 디코더는 처음 위치의 준비 상태를 유지하고 완료 표시/진행은 끝 위치로 유지하므로, 이후 PlayAsync는 동일 방문에서 처음부터 재생한다. 다른 위치 요청은 기존 소유 IO/native seek와 완료 상태 해제를 그대로 사용한다.

| 보완 검사 | 검증 경계 |
|---|---|
| post-prepare 파일 접근 실패 | 소스는 정상; 파일만 오류 주입. 실제 Ready 해제 대기/Busy, 다음 랜덤 준비 호출 증가 0, 한 명령 한 후보, 기존 Pending/cursor/Seen/슬롯/DB flags·기록 불변, 명시 재확인 성공 |
| stale ticket false | revision 변경에 의한 false 결과의 Ready 폐기/세션 억제, 새 세션에서 억제 해제 |
| 취소/Closing | post-check IO 보류 중 CancelOpening/SetExitRequested, 늦은 IOException 및 OperationCanceledException. 실제 완료까지 drain, Ready 해제와 Pending 보존, 다음 후보 재시도 허용 |
| 기존 Forward | 파일의 post-check 실패 뒤 Forward 슬롯·cursor/Pending/Seen·기록 보존, 명시 Forward 재확인 성공 |
| 완료 위치 비동기 seek | 실제 Windows/libVLC MP4/MKV/음성 MP4/AVI 테스트 복사본. 끝 복원→동일 끝 seek(native 발행 0)→PlayAsync(Playing/처음 1.5초 이내), 동일 방문·끝 진행 보존 |
| 다른 위치 seek | 완료 복원에서 다른 위치 native 발행 1/완료 상태 해제, privacy mute 의도·방문 보존. 기존 WPF 숨김/복원·소유 IO/drain 회귀도 실행 |

보완 코드·테스트 **`ccb982a497328ca61a4e30f0f981a2ed32506295`**의 Windows x64 / OS 10.0.26100 / .NET SDK 10.0.401 Release 빌드는 **경고 0·오류 0**, 영향 CI **7개 모두 통과**했다. 해당 SHA에서 재실행 없이 성공했으며 이후 변경은 문서 3개뿐이다.

| 보완 코드 SHA의 Windows CI | 결과 |
|---|---|
| [T03 37897357077](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/37897357077) | 통과 |
| [T05 37897357301](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/37897357301) | 통과 |
| [T06 37897357081](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/37897357081) | 통과 |
| [T07 37897357036](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/37897357036) | 통과 |
| [T09 37897357023](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/37897357023) | 통과 |
| [T10 37897357097](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/37897357097) | 통과 |
| [T11 37897357075](https://github.com/danhk0612/Random_Multimedia_Manager/actions/runs/37897357075) | 통과 |

T03 job `113711644595`에서 새 보완 시나리오 6개를 포함한 T18A4Verification 13개 PASS를 확인했다. 100/10,000항목·48소스의 SQL은 snapshot=3, paths=2, Pump=3, manual=59, random=20으로 동일하다. T09 job `113711644449`에서 4종 로컬 영상 복사본의 동일 완료 위치 native seek 0·처음부터 PlayAsync 및 다른 위치 seek assertion이 모두 통과했다. T11 job `113711644547`도 Data/Viewing/Scanner/Comic/Video/Subtitle 전체와 기존 삭제·숨김/복원·종료 회귀를 통과했다. libVLC의 CI 디스플레이 어댑터/thumbnail 런타임 진단은 빌드 경고와 구분하며 GPU/provider 실물 지원 근거로 사용하지 않는다.

파일 접근/지연/바인딩 변경은 결정적 오류 주입이고 영상 검사는 Windows 로컬 실파일과 실제 libVLC다. 실제 NAS/RaiDrive backend/version·캐시·전송량·원격 seek/해제·단절/재연결은 장비 접근이 없어 미검증이며 오류 주입 통과로 대체하지 않는다. 기존 수동 미검증·T11 재실행 이력·AVI 조사 보류는 유지한다. PR #26은 미병합이고 T18A-5/6·배포는 시작하지 않았다.
