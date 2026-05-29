# Build, run the xUnit sample under the testhide logger, validate the report, and pack NuGets.
$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")

$art = Join-Path (Get-Location) "artifacts"
if (Test-Path $art) { Remove-Item $art -Recurse -Force }
New-Item -ItemType Directory -Path $art | Out-Null

Write-Host "==> Build logger + core"
dotnet build src/Testhide.Reporting.VSTest/Testhide.Reporting.VSTest.csproj -c Release

Write-Host "==> Build sample"
dotnet build tests/SampleTests/SampleTests.csproj -c Release

Write-Host "==> Run sample under the testhide logger"
$report = Join-Path $art "sample-report.xml"
# Sample has an intentional failing test; gate on the report, not the exit code.
dotnet test tests/SampleTests/SampleTests.csproj -c Release --no-build --logger "testhide;LogFilePath=$report"

Write-Host "==> Validate emitted report"
python conformance/validate_report.py $report
if ($LASTEXITCODE -ne 0) { throw "Emitted report failed conformance validation" }

Write-Host "==> Validate golden fixture"
python conformance/validate_report.py conformance/golden_report.xml
if ($LASTEXITCODE -ne 0) { throw "Golden fixture failed validation" }

Write-Host "==> Pack NuGet packages"
dotnet pack src/Testhide.Reporting.Core/Testhide.Reporting.Core.csproj -c Release -o $art
dotnet pack src/Testhide.Reporting.VSTest/Testhide.Reporting.VSTest.csproj -c Release -o $art

Write-Host "==> Done. Artifacts:"
Get-ChildItem $art | Select-Object -ExpandProperty Name
