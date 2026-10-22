@echo off
rem Builds ..\DandyPicker.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
rem Close DandyPicker before rebuilding, or the .exe can't be overwritten.
setlocal
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
"%CSC%" /nologo /target:winexe /optimize+ /out:..\DandyPicker.exe /win32icon:..\dandelion.ico ^
  /r:System.Management.dll /r:System.Web.Extensions.dll /r:Microsoft.VisualBasic.dll /r:Microsoft.CSharp.dll ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll ^
  DandyPicker.cs
if errorlevel 1 (echo Build failed.& exit /b 1)
echo Built ..\DandyPicker.exe
