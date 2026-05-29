# testhide-dotnet

Emit **Testhide-format** (JUnit-extended) test reports from .NET, so the Testhide build agent
parses your results correctly and the dashboard/AI features get full data.

It ships as a **VSTest logger** — one component that works for **xUnit, NUnit and MSTest**, because
they all run through `dotnet test`. Output is identical to the
[`testhide-pytest-plugin`](https://github.com/thuesdays/testhide-pytest-plugin) contract
(`fail_id`, `test_resolution`, `<system-out>`, suite metadata, `testhide_schema_version=1`).
Canonical spec:
[Testhide Report Format v1](https://github.com/thuesdays/testhide/blob/main/docs/specs/REPORT-FORMAT-V1.md).

## Packages

| Package | Purpose |
|---|---|
| `Testhide.Reporting.VSTest` | the `dotnet test` logger (FriendlyName `testhide`) — this is all you need |
| `Testhide.Reporting.Core` | shared report writer (pulled in automatically) |
| `Testhide.Reporting.Xunit` | convenience meta-package (depends on VSTest) for discoverability |
| `Testhide.Reporting.NUnit` | convenience meta-package (depends on VSTest) |
| `Testhide.Reporting.MSTest` | convenience meta-package (depends on VSTest) |

> The single VSTest logger covers xUnit / NUnit / MSTest — it's the only one you technically need.
> The per-framework packages are thin meta-packages (no code; they just depend on
> `Testhide.Reporting.VSTest`) provided purely for discoverability, e.g. `dotnet add package
> Testhide.Reporting.Xunit`.

## Install & use

```bash
dotnet add package Testhide.Reporting.VSTest
dotnet test --logger "testhide;LogFilePath=junittests.xml"
```

Logger parameters (`name=value`, `;`-separated):

| Parameter | Meaning |
|---|---|
| `LogFilePath` | output report path (default `junittests.xml`; relative paths resolve under `TestResults/`) |
| `SuiteName` | `<testsuite name="...">` (default `dotnet`) |
| `meta.KEY=VALUE` | add a suite `<property>` (e.g. `meta.build=1042;meta.branch=main`) |
| `JiraUrl` / `JiraUsername` / `JiraPassword` | optional Jira enrichment by `fail_id` |

Example with metadata:

```bash
dotnet test --logger "testhide;LogFilePath=junittests.xml;SuiteName=api-tests;meta.build=1042;meta.branch=main"
```

## What it captures

- Outcomes: passed / failed / skipped (from VSTest `TestOutcome`).
- `fail_id` = `md5("module.class.method.ExceptionType(message)")` — stable failure key (dedup + Jira).
- Failure message + stack trace (CDATA), duration, `test_resolution`, suite counts + metadata.
- Source `file`/`line` when the test framework provides them (e.g. with portable PDBs / SourceLink).
- Per-test metadata via test **traits** named `docstr` / `jira` / `info` (framework-dependent),
  and result attachments → `attachment` properties.

## Verify (conformance)

`conformance/` vendors the canonical validator + golden fixture. CI runs the sample suite through
the logger and validates the output:

```bash
python conformance/validate_report.py junittests.xml
```

## Build locally

```bash
pwsh scripts/build.ps1      # or: bash scripts/build.sh
```
Builds, runs the xUnit sample under the logger, validates the report, and packs both NuGet packages
into `artifacts/`.

## Publishing (maintainers)

**Local publish (Windows):**
```bat
copy .env.local.example .env.local   :: then edit .env.local and add NUGET_API_KEY
publish.bat
```
`publish.bat` loads `.env.local` (gitignored), runs the conformance gate (sample run through the
logger + validator), packs **all** `src/` packages, and pushes them to NuGet.

`.env.local`:
```
NUGET_API_KEY=...      # https://www.nuget.org/account/apikeys  (scope: Testhide.Reporting.*)
```

**CI publish (GitHub Actions):** run the *Publish to NuGet* workflow (manual `workflow_dispatch`,
pass the version). Required repository secret:
- `NUGET_API_KEY`.

## License

MIT.
