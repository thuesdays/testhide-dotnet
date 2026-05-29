@echo off
REM ============================================================================
REM Publish all Testhide.Reporting.* NuGet packages.
REM Reads tokens/env from .env.local (gitignored). Copy .env.local.example first.
REM Usage:  publish.bat
REM ============================================================================
setlocal enabledelayedexpansion
cd /d "%~dp0"

if not exist ".env.local" (
  echo [publish] ERROR: .env.local not found.
  echo [publish] Copy .env.local.example to .env.local and add your NuGet API key.
  exit /b 1
)

for /f "usebackq eol=# tokens=1,* delims==" %%a in (".env.local") do set "%%a=%%b"

if "%NUGET_API_KEY%"=="" (
  echo [publish] ERROR: NUGET_API_KEY is not set in .env.local
  exit /b 1
)

echo [publish] Building logger + sample...
dotnet build src\Testhide.Reporting.VSTest\Testhide.Reporting.VSTest.csproj -c Release || exit /b 1
dotnet build tests\SampleTests\SampleTests.csproj -c Release || exit /b 1

echo [publish] Conformance gate (sample run via the testhide logger)...
REM The sample has an intentional failing test, so `dotnet test` exits non-zero;
REM the gate is the emitted report's conformance, validated below.
dotnet test tests\SampleTests\SampleTests.csproj -c Release --no-build --logger "testhide;LogFilePath=%cd%\sample-report.xml"
python conformance\validate_report.py "%cd%\sample-report.xml" || exit /b 1

echo [publish] Packing all src packages...
if exist artifacts rmdir /s /q artifacts
for /r "src" %%p in (*.csproj) do (
  dotnet pack "%%p" -c Release -o artifacts || exit /b 1
)

echo [publish] Pushing to NuGet...
dotnet nuget push "artifacts\*.nupkg" --api-key %NUGET_API_KEY% --source https://api.nuget.org/v3/index.json --skip-duplicate || exit /b 1

echo [publish] Done.
endlocal
