@echo off
chcp 65001 >nul
rem Window Rescue の 1 ファイル exe（.NET ランタイム同梱）を artifacts\release\ に作る
setlocal
cd /d "%~dp0"

tasklist /fi "imagename eq WindowRescue.exe" | find /i "WindowRescue.exe" >nul
if not errorlevel 1 (
    echo WindowRescue.exe が起動中です。トレイの「終了」で閉じてから実行してください。
    pause
    exit /b 1
)

dotnet publish src\WindowRescue\WindowRescue.csproj -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true -p:DebugType=None -o artifacts\release
if errorlevel 1 (
    echo ビルドに失敗しました。
    pause
    exit /b 1
)

powershell -NoProfile -Command "Compress-Archive -LiteralPath 'artifacts\release\WindowRescue.exe' -DestinationPath 'artifacts\release\WindowRescue-win-x64.zip' -Force -ErrorAction Stop"
if errorlevel 1 (
    echo ZIP の作成に失敗しました。
    pause
    exit /b 1
)

powershell -NoProfile -Command "$ErrorActionPreference = 'Stop'; $zip = 'artifacts/release/WindowRescue-win-x64.zip'; $line = ([BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash([IO.File]::ReadAllBytes($zip)))).Replace('-', '').ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($zip) + [char]10; [IO.File]::WriteAllText($zip + '.sha256', $line, [Text.UTF8Encoding]::new($false))"
if errorlevel 1 exit /b 1

echo.
echo 作成しました: %~dp0artifacts\release\WindowRescue.exe
echo 配布用 ZIP: %~dp0artifacts\release\WindowRescue-win-x64.zip
pause
