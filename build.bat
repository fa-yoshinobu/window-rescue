@echo off
chcp 65001 >nul
rem Window Rescue の 1 ファイル exe（.NET ランタイム同梱）を dist\ に作る
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
  -p:EnableCompressionInSingleFile=true -p:DebugType=None -o dist
if errorlevel 1 (
    echo ビルドに失敗しました。
    pause
    exit /b 1
)

powershell -NoProfile -Command "Compress-Archive -LiteralPath 'dist\WindowRescue.exe' -DestinationPath 'dist\WindowRescue-win-x64.zip' -Force -ErrorAction Stop"
if errorlevel 1 (
    echo ZIP の作成に失敗しました。
    pause
    exit /b 1
)

echo.
echo 作成しました: %~dp0dist\WindowRescue.exe
echo 配布用 ZIP: %~dp0dist\WindowRescue-win-x64.zip
pause
