<p align="center"><img src="docs/icon.png" width="96" alt="StickerBoard 아이콘"></p>

<h1 align="center">StickerBoard</h1>

<p align="center">
Windows용 가벼운 데스크톱 보드 — 이미지를 붙이고, 메모하고, 낙서하고,<br>
스티커 메모처럼 바탕화면에 띄워 두고 쓰세요.
</p>

<p align="center">
<a href="README.md">English</a> · <b>한국어</b> · <a href="README.ja.md">日本語</a> · <a href="README.zh-Hans.md">简体中文</a> · <a href="README.zh-Hant.md">繁體中文</a>
</p>

<p align="center"><img src="docs/screenshot.png" alt="StickerBoard 스크린샷"></p>

## 기능

- **이미지** — `Ctrl+V`로 붙여넣기(캡처 화면, 브라우저 이미지, 복사한 파일) 또는 파일 끌어다 놓기. 모서리 핸들이나 마우스 휠로 크기 조절.
- **메모** — 빈 곳 더블클릭. 굵게·기울임·밑줄·취소선, 글자색, 형광펜, 부분 글자 크기, 글꼴, 글머리 기호, **체크리스트**.
- **링크** — 웹 주소를 보드에 붙여넣거나 끌어다 놓으면 페이지 제목이 표시되는 링크 카드가 생깁니다(더블클릭으로 열기). 메모에서는 주소를 입력하면 자동으로 링크가 되고, `Ctrl+K`로 선택한 글자에 링크를 걸 수 있습니다.
- **글자만 메모** — 메모지 없이 글자만. 은은한 테두리 덕분에 어떤 배경화면 위에서도 잘 읽힙니다.
- **펜** — 무엇 위에든 그리고 형광펜으로 표시, 되돌리기 지원.
- **바탕화면에 두고 쓰기** — 배경 투명도 슬라이더(완전 투명 가능), 항상 위 / 일반 / 다른 창 뒤에, 위치·크기 고정, 최대화, 내용에 맞게 창 크기.
- **가벼움** — 약 0.6MB 실행 파일, 가만히 있을 때 CPU 0%, 모든 내용 자동 저장.
- **언어** — English, 한국어, 日本語, 简体中文, 繁體中文 (Windows 언어를 따라가며 우클릭 메뉴에서 변경 가능).

## 다운로드

**[Releases](https://github.com/Eastar0102/StickerBoard/releases/latest)** 에서 최신 버전을 받으세요 (Windows 10/11, x64):

| 파일 | 크기 | 참고 |
| --- | --- | --- |
| `StickerBoard-<버전>-win-x64.exe` | 약 0.6MB | [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) 필요 |
| `StickerBoard-<버전>-win-x64-standalone.exe` | 큼 | 설치할 것 없이 바로 실행 |

설치 과정 없이 exe를 실행하면 됩니다. 코드 서명이 되어 있지 않아 처음 실행할 때 Windows SmartScreen 경고가 뜰 수 있습니다 (**추가 정보 → 실행**).

## 사용법

| 동작 | 방법 |
| --- | --- |
| 이미지 / 텍스트 붙여넣기 | `Ctrl+V` (텍스트는 메모, 웹 주소는 링크 카드가 됨) |
| 새 메모 | 빈 곳 더블클릭 또는 **+** 버튼 |
| 이동 | 이미지는 그대로, 메모는 위쪽 띠를 끌기 |
| 크기 조절 | 모서리 핸들 (`Shift` = 비율 자유) 또는 이미지 위에서 마우스 휠 |
| 삭제 / 삭제 취소 | `Del` / `Ctrl+Z` |
| 메모 서식 | `Ctrl+B` `Ctrl+I` `Ctrl+U`, 취소선 `Ctrl+Shift+X`, 글머리 기호 `Ctrl+Shift+L` |
| 체크리스트 | `Ctrl+Enter`로 체크박스 추가/체크, ☐ 클릭으로 체크 |
| 메모 글자 크기 | `Ctrl+휠` |
| 메모 속 링크 | 주소를 입력하면 자동으로 링크 · `Ctrl+K` 선택한 글자에 링크 · `Ctrl+클릭` 열기 |
| 펜 | `P` (`H` 형광펜, `E` 지우개, `Ctrl+Z` 되돌리기, `Esc` 끝내기) |
| 최대화 | `F11` 또는 상단 바 더블클릭 |
| 설정 | 보드 우클릭 |

## 데이터

모든 내용은 `%APPDATA%\StickerBoard` 에 저장됩니다 (`board.json`, `ink.isf`, `images\`).
`STICKERBOARD_DATA` 환경 변수로 다른 폴더를 지정할 수 있습니다 — 테스트하거나 별도의 보드를 하나 더 띄울 때 유용합니다.

## 빌드

Windows에 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) 가 필요합니다.

```powershell
dotnet run

# 작은 단일 exe (.NET 8 Desktop Runtime 필요)
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish

# 단독 실행 단일 exe
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish-standalone
```

아이콘은 `tools/make-icon.ps1` 로 생성합니다.

## 번역

UI 문구는 [`Strings/`](Strings) 폴더의 JSON 파일에 있습니다 (`en.json` 이 기준이며, 빠진 키는 영어로 표시됩니다).
새 언어를 추가하려면 `en.json` 을 `Strings/<코드>.json` 으로 복사해 번역하고, [`Localization.cs`](Localization.cs) 의 `L.Languages` 에 코드를 추가한 뒤 Pull Request를 보내 주세요. 기존 번역 수정도 환영합니다.

## 라이선스

[MIT](LICENSE)
