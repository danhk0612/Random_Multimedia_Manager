# Random Multimedia Manager — Windows 검토 패키지

공개 Release가 아닌 T18 PR 검토용 패키지다. Windows x64용이며 폴더 전체가 필요하다.

## 실행

1. ZIP을 쓰기 가능한 로컬 폴더에 **전부 압축 해제**한다. ZIP 안에서 바로 실행하지 않는다.
2. `RandomMultimediaManager.App.exe`를 실행한다. .NET Desktop Runtime과 libVLC를 포함하도록 구성했으므로 SDK 또는 별도 VLC 설치를 전제로 하지 않는다. 깨끗한 Windows 10/11 PC에서의 실제 확인은 검증 문서를 따른다.
3. 분류를 만들고 만화/영상 유형을 고른 뒤 소스 폴더를 등록한다. 수동 스캔 후 분류에서 랜덤 보기를 실행한다. 만화는 ZIP/CBZ, 영상은 포함된 libVLC가 처리하는 형식이다. 라이브러리 목록에서도 파일을 선택해 감상할 수 있다.
4. 즐겨찾기·랜덤 제외·이번 기록 제외는 서로 다른 동작이다. 정상 감상에서 벗어나면 기록을 저장하며, 이번 기록 제외는 과거 기록을 지우지 않는다. 삭제는 확인창에서 휴지통/명시적 영구삭제/취소를 선택한다.
5. 메인 창 X는 트레이로 숨긴다. `종료` 버튼 또는 트레이 종료로 정상 종료한다. `Ctrl+Shift+H`는 모두 숨기기/복원, `Ctrl+Shift+Q`는 빠른 정상 종료다. 키 충돌/트레이 실패 시 화면의 안내를 확인한다. 감상 단축키는 저장소 `docs/SHORTCUTS_AND_TRAY.md`를 따른다.

## 데이터와 교체

DB는 `%LOCALAPPDATA%\RandomMultimediaManager\library.db`, 삭제 복구 저널은 같은 폴더의 `delete-journal`에 저장한다. 현재 세션/Pending은 메모리이며 재시작 복원 대상이 아니다. 실행 폴더를 바꾸어도 같은 Windows 계정의 데이터 위치는 같다. 다른 사용자/관리자 계정은 데이터와 매핑 드라이브가 다를 수 있다.

업데이트 시 앱을 **정상 종료한 뒤** 새 ZIP을 새 폴더에 전부 풀고 새 exe를 실행한다. exe 하나만 교체하거나 서로 다른 버전의 DLL/plugins를 섞지 않는다. 기존 실행 폴더 제거가 LocalAppData 데이터를 제거하지는 않는다. DB와 삭제 저널을 임의로 삭제·부분 교체하지 않는다. migration 후 이전 버전으로 되돌리는 것은 보장하지 않는다. 자동 업데이트/설치 관리자/백업 기능은 없다.

## 지원 범위와 제한

- .NET 10 공식 지원표의 Windows 11 x64를 사용자 검증 대상으로 한다. Windows 10은 지원표에 남은 특정 Enterprise/LTSC·IoT 에디션에 한정되며 일반 Windows 10 전체 지원을 선언하지 않는다. WPF 실행 가능한 Desktop 환경이 필요하다. x86/ARM/Linux/macOS/Server Core는 대상이 아니다. Server CI 자동 검사와 실제 PC 검증은 별개다.
- 로컬·SMB 매핑·직접 UNC·RaiDrive는 지원 목표다. **실제 NAS/RaiDrive 연결·단절·재매핑·캐시·원격 삭제는 미검증**이며 전체 호환성을 보장하지 않는다. 원격 기본은 수동 갱신이며 필요 probe와 자동 디렉터리 열거를 구분한다.
- 원격 휴지통은 현재 Unknown으로 실행 차단한다. 임의 영구삭제로 전환하지 않는다. 명시적 영구삭제는 복구할 수 없으므로 테스트에는 복사본을 사용한다. 삭제 결과 불명 상태는 격리와 기존 복구 절차를 따른다.
- 정상 종료는 실제 IO와 리소스 해제를 기다린다. 네트워크/provider가 반환하지 않으면 **종료 시간 상한이 없다**. 강제 종료로 안전한 저장을 보장하지 않는다. 저장/삭제/해제 실패는 종료 차단으로 남길 수 있다.
- RaiDrive/provider가 자체적으로 파일 일부 또는 전체를 캐시할 수 있다. 앱 색인에서 미디어 본문을 요청하지 않는다는 것이 provider 전송량 0을 뜻하지 않는다.
- 기존 사용자 수동 미검증/승인 생략은 유지한다. AVI 음성 조사 보류, 실제 하드웨어/청취/물리 단축키·Explorer·로그오프 미검증도 포함한다. VSR은 없다.

실사용 점검표: 저장소 `docs/T18A_6_INTEGRATION_VALIDATION.md`. 패키지 결과: `docs/T18_DISTRIBUTION_VALIDATION.md`.
`build-info.json`은 소스 SHA와 빌드 설정, `files.json`은 파일별 SHA256, ZIP 옆 `.sha256`은 ZIP SHA256이다. 앱 자체 라이선스와 재배포 최종 검토가 남아 있다. 고지 파일은 `THIRD_PARTY_NOTICES.md`, `dependency-notices`를 확인한다.

## 라이선스·소스

동봉 LICENSE는 자체 코드·작성 문서의 MIT 라이선스다. 전체 ZIP과 제3자 DLL/plugins를 MIT로 재표시하지 않는다. THIRD_PARTY_NOTICES.md, licenses/, dependency-notices/의 원문 조건을 함께 확인한다. 라이브러리 수정·교체 및 그 변경을 디버깅하기 위한 reverse engineering을 금지하는 추가 EULA는 두지 않는다.

동일 commit의 `RandomMultimediaManager-source-review-<sha>.zip`은 **미완성 검토용 sidecar**다. status.json은 전체 대응 소스 미완료·공개 승인 false를 표시한다. 일반 upstream 링크나 이 sidecar만으로 조건 충족을 선언하지 않는다. 공개 전 정확한 linked contrib/patches/toolchain과 파일별 고지 검토를 완료하고 완전한 소스를 바이너리와 같은 배포 위치에서 제공해야 한다.
