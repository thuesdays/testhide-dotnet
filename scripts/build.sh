#!/usr/bin/env bash
# Build, run the xUnit sample under the testhide logger, validate the report, and pack NuGets.
set -euo pipefail
cd "$(dirname "$0")/.."

ART="$PWD/artifacts"
rm -rf "$ART"; mkdir -p "$ART"

echo "==> Build logger + core"
dotnet build src/Testhide.Reporting.VSTest/Testhide.Reporting.VSTest.csproj -c Release

echo "==> Build sample"
dotnet build tests/SampleTests/SampleTests.csproj -c Release

echo "==> Run sample under the testhide logger"
REPORT="$ART/sample-report.xml"
# The sample includes an intentional failing test, so `dotnet test` exits non-zero.
# The real gate is whether the emitted report conforms — validate that, not the exit code.
dotnet test tests/SampleTests/SampleTests.csproj -c Release --no-build \
    --logger "testhide;LogFilePath=$REPORT" || true

echo "==> Validate emitted report against the conformance kit"
python conformance/validate_report.py "$REPORT"

echo "==> Validate golden fixture (sanity)"
python conformance/validate_report.py conformance/golden_report.xml

echo "==> Pack NuGet packages"
dotnet pack src/Testhide.Reporting.Core/Testhide.Reporting.Core.csproj -c Release -o "$ART"
dotnet pack src/Testhide.Reporting.VSTest/Testhide.Reporting.VSTest.csproj -c Release -o "$ART"

echo "==> Done. Artifacts:"
ls -1 "$ART"
