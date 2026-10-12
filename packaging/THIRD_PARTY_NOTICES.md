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
| VideoLAN.LibVLC.Windows | 3.0.23.1 | nuspec는 LGPL-2.1-or-later이나 actual avcodec/swscale 바이너리에 GPL FFmpeg 증거 확인. 별도 `.GPL` 패키지 미참조만으로 LGPL-only 판정 불가. 공개 배포 차단 |

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

## 2026-10-12 실제 native 검토 — 공개 배포 차단 유지

[재배포 자료와 재현/제공 절차](release-readiness/README.md)에 동결 artifact `11703665480` / 코드 SHA `61a98bb5f51a0aa997ab37429e00cee3be18a4cf`의 전체 manifest, 1,285개 native/지원 파일 목록과 956개 plugin DLL 해시를 보존했다. x64/x86/ARM64의 공식 VLC ZIP 동일 파일 1,256개를 byte 대조했다. x64 import .lib 2개는 공식 7z와도 일치하며 나머지 아키텍처의 import .lib 대조는 미완료다.

**LGPL metadata와 결합된 코드 조건이 다르다.** 세 아키텍처의 avcodec/swscale plugin 총 6개에 FFmpeg `--enable-gpl`와 GPL license 문자열이 있다. FFmpeg 공식 https://ffmpeg.org/legal.html 에 접근 확인했고, optional GPL 부분 사용 시 적용되는 조건을 이 바이너리 증거와 대조했다. 정확한 문자열·경로·해시는 `release-readiness/GPL_FFMPEG_EVIDENCE.json`에 있다. GPL 제외 파일/폴더 규칙에 해당하는 DLL이 없다는 검사만으로 LGPL 준수를 선언하지 않는다. 앱 자체 라이선스에 GPL 또는 다른 라이선스를 자동 적용/선택한 것이 아니다.

VLC 3.0.23-2 대응 revision의 공식 source tar, nuspec commit의 LibVLCSharp source archive, native source ID가 같은 SQLite amalgamation, VLC contrib checksum과 일치한 FFmpeg 4.4.5 tar를 sources/에 보존한다. VLC의 contrib recipes/patches도 source tar에 있다. 외부 링크 소스·아키텍처별 build provenance와 플러그인별 원문 고지는 아직 누락돼 있으므로 **전체 corresponding source 제공 완료는 아니다**. `source-inputs.json`에 확보한 원문 URL·bytes·SHA256·부분 완료 상태를 명시했다.

Skia/runtime의 exact ZIP 고지·nuspec와 source revision/build 자료, VLC COPYING/COPYING.LIB/AUTHORS, LibVLCSharp/FFmpeg 라이선스 원문을 보존한다. packaging/licenses의 새 원문은 기존 폴더 복사에 의해 향후 publish에 포함되며 원래 검토 artifact에 소급 포함되지 않는다. GPL 원문 추가는 제3자 고지이며 앱 LICENSE/권리 부여가 아니다.

공개 배포는 앱 라이선스 사용자 결정, GPL FFmpeg 처리 방식 검토, exact plugin/contrib 소스·고지·제공 묶음 완성, 사전 설치 없는 Windows PC 수동 검증이 남아 차단한다. 현재 archive/recipe 확보만으로 바이너리 재빌드나 DLL 교체 검증 성공을 주장하지 않는다.
