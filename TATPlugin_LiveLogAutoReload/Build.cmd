@echo off
setlocal
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo Could not find the .NET Framework C# compiler.
    exit /b 1
)

if not exist "%~dp0bin" mkdir "%~dp0bin"
"%CSC%" /nologo /target:library /reference:System.Windows.Forms.dll /out:"%~dp0bin\TATPlugin_LiveLogAutoReload.dll" "%~dp0TATPlugin_LiveLogAutoReload.cs"
if errorlevel 1 exit /b %errorlevel%
echo Built "%~dp0bin\TATPlugin_LiveLogAutoReload.dll"
echo Copy it next to TextAnalysisTool.NET.exe to install it.
