# 의존성·재배포 검토 고지

이 패키지는 공개 배포 승인 전 CI 검토용이다. 앱 자체 라이선스는 저장소에서 미확정이며 여기서 임의 지정하지 않는다. 이 문서는 개별 원문 라이선스를 대체하지 않는다.

## 포함 방향과 조건

| 구성 | 고정 버전 | 라이선스/필요 조치 |
|---|---|---|
| .NET / Windows Desktop runtime | 10.0.12 (SDK 10.0.401) | 배포물의 LICENSE/THIRD-PARTY-NOTICES 원문 유지. MIT 및 개별 third-party 조건 |
| Microsoft.Data.Sqlite | 10.0.8 | MIT 및 실제 NuGet metadata/고지 유지 |
| SQLitePCLRaw bundle/core/provider/lib.e_sqlite3 | 2.1.13 및 잠금 파일의 실제 전이 버전 | SQLitePCLRaw Apache-2.0 고지, SQLite public-domain 설명 및 번들 원문 확인 |
| SkiaSharp / NativeAssets.Win32 | 4.151.2 및 잠금 파일의 실제 해석 결과 | managed wrapper MIT, Skia 및 결합된 라이브러리별 고지 원문 유지; MIT 하나로 네이티브 전체를 단정하지 않음 |
| LibVLCSharp / WPF | 3.10.1 | LGPL-2.1-or-later, 고지·해당 소스 접근 및 교체 가능한 동적 연결 조건 확인 |
| VideoLAN.LibVLC.Windows | 3.0.23.1 | 실제 패키지 nuspec/포함 COPYING/플러그인 목록 대조. 별도 `.GPL` 패키지는 추가하지 않음. libVLC와 plugins의 라이선스는 구별 |

실제 버전·패키지 content SHA512는 `dependencies.json`과 `packages.lock.json`, 실제 DLL/plugins·SHA256는 `files.json`에 있다. `dependency-notices/<id>/<version>/`에 각 NuGet 패키지의 nuspec, 제공된 license/COPYING/NOTICE/copyright와 nupkg SHA512를 원문 복사한다. 런타임 publish가 제공하는 LICENSE/THIRD-PARTY-NOTICES도 제거하지 않는다. 파일이 없다는 사실이 고지 의무 면제를 뜻하지 않는다.

libVLC는 수정 없이 외부 DLL/plugins 폴더로 유지하며 단일 exe에 봉인하거나 trim하지 않는다. VideoLAN은 일부 모듈의 라이선스를 구분하도록 안내한다. LGPL 동적 연결을 유지하는 것만으로 소스 제공·고지 의무가 자동 충족됐다고 선언하지 않는다. 공개 배포 전 **정확한 번들의 license 원문, corresponding source와 제공 방식, 플러그인별 조건**을 확정해야 한다. 현재 CI 수집은 metadata와 제공 고지 증거이며, 전체 corresponding source 묶음은 아니다. 필요한 경우 D08에 따라 외부 libVLC 설치/연결 방식을 검토할 수 있으나 이번에 제품 코드/엔진을 바꾸지 않는다.

## 2026-10-11 공식 출처 확인

- [.NET 10 지원 OS](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md), [Windows 설치/지원표](https://learn.microsoft.com/en-us/dotnet/core/install/windows): 접근 확인. Windows 10 일반 전체 지원 근거로 사용하지 않음.
- [SkiaSharp 4.151.2](https://www.nuget.org/packages/SkiaSharp/4.151.2), [SQLitePCLRaw 2.1.13](https://www.nuget.org/packages/SQLitePCLRaw.bundle_e_sqlite3/2.1.13): 접근 확인. 최종 원문은 CI NuGet 추출 증거와 대조.
- [VideoLAN libVLC 설명](https://github.com/videolan/vlc), [VideoLAN NuGet 프로필](https://www.nuget.org/profiles/videolan): 접근 확인. upstream libVLC LGPL와 별도 GPL plugins 패키지 구분.
- VideoLAN 법률/모듈 안내 및 정확한 native NuGet 버전 페이지는 이번 웹 접근 일부 실패. 실패한 페이지를 검토 완료 근거로 사용하지 않으며 정확한 NuGet archive의 metadata/고지로 보완한다.

앱 자체 라이선스 선택과 위 재배포 미확정 사항은 공개 배포 판단의 차단 항목이다. 공개 Release/tag는 발행하지 않는다.

## exact NuGet archive 대조

첫 검토 artifact에서 모든 wrapper/native NuGet nuspec의 버전·license expression을 직접 확인했다. LibVLCSharp/WPF 및 VideoLAN.LibVLC.Windows는 LGPL-2.1-or-later, Microsoft.Data.Sqlite/Core와 SkiaSharp/NativeAssets는 MIT, SQLitePCLRaw 4개는 Apache-2.0이다. Skia Win32의 LICENSE/THIRD-PARTY-NOTICES 원문은 포함된다. VideoLAN NuGet에는 license expression/URL만 있고 COPYING 원문이 없어 `licenses/LGPL-2.1-or-later.txt`를 별도 제공한다(SPDX license-list-data 원문 blob `c6487f4fdf701640ac0f559eb0130cdf76c5d3f4`). Microsoft.Data.Sqlite 고지는 dotnet/efcore `v10.0.8`의 LICENSE.txt 원문(blob `a616ed188dfce68ee308f674350ad242d8588c2b`)으로 보완한다. runtime pack의 LICENSE/THIRD-PARTY-NOTICES도 package cache에서 수집한다. corresponding source/플러그인별 최종 검토 미완료는 유지한다.
