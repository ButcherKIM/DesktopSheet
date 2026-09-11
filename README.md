# DesktopSheet

스티커노트처럼 바탕화면에 늘 띄워 두고 숫자를 적고 계산하는 윈도우용 스프레드시트입니다.
규칙은 전부 [SPEC.md](SPEC.md)에 있고, 코드는 그 문서를 그대로 옮긴 것입니다.

## 무엇이 어디에 있나

| 자리 | 무엇 | 어디서 빌드되나 |
|---|---|---|
| `src/DesktopSheet.Core` | 표시 규칙, 수식, 시트, 저장 (사양서 1~13, 15장) | 어디서나 |
| `src/DesktopSheet.App` | 창, 그리드 그리기, 트레이 (사양서 12, 14장) | 윈도우에서만 실행 |
| `tests/DesktopSheet.Core.Tests` | 사양서 11장 검증표를 비롯한 시험 | 어디서나 |
| `tools/DesktopSheet.Bench` | 16장 성능 목표 재기 | 어디서나 |
| `tools/check_spec.py` | 사양서 무결성 검사 | 어디서나 |

계산과 표시 규칙을 UI 와 갈라 두었습니다. 창을 띄우지 못하는 환경에서도 규칙이 맞는지는 시험으로 확인됩니다.

## 빌드와 실행

윈도우에서:

```
dotnet run --project src/DesktopSheet.App
```

배포용 단일 실행 파일(사양서 17.2):

```
dotnet publish src/DesktopSheet.App -c Release
```

`bin/Release/net8.0-windows/win-x64/publish/DesktopSheet.exe` 하나만 복사하면 됩니다.
.NET 런타임을 따로 설치하지 않아도 되고, 서명하지 않았으므로 처음 실행할 때
윈도우가 띄우는 창에서 **추가 정보 → 실행**을 골라야 합니다(17.3).

## 확인

```
dotnet test                              # 시험
dotnet run --project tools/DesktopSheet.Bench -c Release   # 16장 성능 목표
python3 tools/check_spec.py              # 사양서 무결성
```

윈도우가 아닌 곳에서도 `src/DesktopSheet.App` 은 빌드까지 됩니다(`EnableWindowsTargeting`). 실행만 안 됩니다.

## 저장 자리

`%APPDATA%\DesktopSheet\book.json` 한 파일입니다. 저장 절차가 없고 마지막 입력에서
500ms 뒤에 저절로 쓰입니다(13.2). 밀려난 직전 파일이 `book.prev.json` 으로 남습니다.
