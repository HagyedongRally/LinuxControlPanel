# Linux Control Panel

Windows 에서 **SSH 로 리눅스 PC 에 접속해 상태를 보고 명령을 실행하는** .NET 10 WPF 데스크톱 앱입니다.
UI 는 요청대로 MVVM 프레임워크 없이 **XAML + 코드비하인드**로 작성했습니다.

## 화면

1. **접속 화면** (`ConnectWindow`)
   - 대상 PC 의 주소/포트/사용자와 인증 방식(비밀번호 또는 개인키 파일)을 입력합니다.
   - 왼쪽 **주소록**에서 지난 접속 기록을 고르면 입력란이 자동으로 채워집니다(더블클릭하면 바로 접속).
   - 처음 보는 호스트 키는 지문을 보여 주고 신뢰할지 묻습니다. 다음부터는 저장된 지문과 같으면 묻지 않고,
     **지문이 바뀌면 경고**합니다.
2. **대시보드** (`DashboardWindow`) — 로그인 후 첫 화면
   - 호스트 이름 / 배포판 / 커널 / 가동 시간 / 호스트 키 지문
   - CPU 사용률(전체 + 코어별), 모델, 코어 수, 로드 애버리지, 온도
   - 메모리·스왑 사용량, 디스크 마운트별 사용량, 인터페이스별 실시간 송수신 속도
   - CPU 사용 상위 프로세스, 접속 중인 사용자
   - 명령 실행 콘솔(프리셋 버튼 + 직접 입력, stderr 와 종료 코드까지 표시)
   - 자동 새로고침(2/3/5/10초) 또는 수동 새로고침

## 요구 사항

- Windows 10 1809 이상
- 실행: .NET 10 데스크톱 런타임 (`Microsoft.WindowsDesktop.App 10.x`)
- 빌드: .NET SDK 10 이상 (SDK 11 로도 `net10.0-windows` 를 빌드할 수 있습니다)

## 빌드와 실행

```powershell
dotnet build LinuxControlPanel.sln -c Release
dotnet run --project src\LinuxControlPanel
dotnet test LinuxControlPanel.sln          # 단위 테스트 55개
```

배포용 단일 실행 파일:

```powershell
dotnet publish src\LinuxControlPanel -c Release -r win-x64 `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true --self-contained true
```

## 프로젝트 구조

```
src/LinuxControlPanel.Core/      UI 없는 핵심 로직 (net10.0)
  Linux/   LinuxStatParser, SystemInfoCollector, SystemSnapshot, Humanize
  Ssh/     SshSession(SSH.NET 래퍼), 호스트 키 확인, 오류 메시지
  Storage/ AddressBookStore(JSON), ISecretProtector
  Models/  ConnectionEntry
src/LinuxControlPanel/           WPF 앱 (net10.0-windows)
  ConnectWindow.xaml(.cs)        접속 화면 + 주소록
  DashboardWindow.xaml(.cs)      대시보드 + 명령 실행
  Services/DpapiSecretProtector  Windows DPAPI 로 비밀 값 암호화
  Views/Rows.cs                  스냅샷 -> 화면 표시용 행
  Theme.xaml                     다크 테마 브러시/스타일
tests/LinuxControlPanel.Tests/   파서·수집기·주소록 단위 테스트 (net10.0)
```

수집은 SSH 왕복을 줄이려고 **명령 한 번**으로 끝냅니다. `/proc/stat`, `/proc/meminfo`, `/proc/net/dev`,
`df`, `ps`, `who`, `thermal_zone*` 을 `#!구역이름` 표시와 함께 출력하게 하고(`SystemInfoCollector.CollectCommand`),
돌아온 글을 `LinuxStatParser` 가 나눠 읽습니다. CPU 사용률은 `/proc/stat` 을 0.3초 간격으로 두 번 읽어 구하고,
네트워크 속도는 직전 수집과의 차이를 `/proc/uptime` 기준으로 나눠 구합니다.

## 저장되는 것과 보안

- 주소록 파일: `%APPDATA%\LinuxControlPanel\addressbook.json`
- 비밀번호/키 암호는 **평문으로 저장하지 않습니다**. [저장]을 켠 경우에만 Windows DPAPI(현재 사용자 범위)로
  암호화한 문자열을 넣습니다. 다른 Windows 계정이나 다른 PC 에서는 복호화되지 않고, 앱이 다시 입력을 요청합니다.
- 서버의 호스트 키 지문(SHA256)을 기록해 두고 바뀌면 경고합니다.
- 처리하지 못한 오류는 `%TEMP%\LinuxControlPanel-error.log` 에 남습니다.

## 확인한 내용

- 단위 테스트 55개 통과 (파서는 실제 Ubuntu 20.04 서버 출력을 고정 입력으로 두고 검증)
- 실제 원격 Ubuntu 20.04 서버에 개인키로 접속 -> 대시보드 실시간 표시 -> 명령 실행 -> 연결 끊기 ->
  주소록에서 재접속까지 Windows 11 에서 확인
- 접속 실패(타임아웃/인증 실패)와 호스트 키 확인 대화상자 동작 확인
