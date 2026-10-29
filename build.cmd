@echo off
rem Builds DandyPicker.exe here: one file, nothing to install. Needs the .NET 10 SDK (dot.net).
rem Close DandyPicker before rebuilding, or the .exe can't be overwritten.
setlocal
cd /d "%~dp0"
dotnet publish src\DandyPicker.csproj -c Release -r win-x64 -o dist\win-x64 -nologo
if errorlevel 1 (echo Build failed.& exit /b 1)
copy /y dist\win-x64\DandyPicker.exe DandyPicker.exe >nul
if errorlevel 1 (echo Couldn't copy DandyPicker.exe - is DandyPicker still open?& exit /b 1)
echo Built DandyPicker.exe
