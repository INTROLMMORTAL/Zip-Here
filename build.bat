@echo off
setlocal
chcp 65001 >nul
cd /d "%~dp0"

set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo Не найден csc.exe. Включите .NET Framework 4 в компонентах Windows.
  if /i not "%~1"=="nopause" pause
  exit /b 1
)

if not exist dist mkdir dist

"%CSC%" /nologo /target:winexe /optimize+ /codepage:65001 /out:dist\ZipHereSetup.exe ^
  /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll ^
  /r:Microsoft.VisualBasic.dll ^
  src\Installer.cs
if errorlevel 1 (
  echo Сборка не удалась.
  if /i not "%~1"=="nopause" pause
  exit /b 1
)

echo.
echo Готово: dist\ZipHereSetup.exe
if /i not "%~1"=="nopause" pause
