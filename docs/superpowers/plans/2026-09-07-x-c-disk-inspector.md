# X C盘巡检官 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a production-ready Windows desktop disk inspection and conservative cleanup application that explains C-drive usage, enforces an allow-list cleanup boundary, produces auditable reports, and publishes as a single-file Windows EXE.

**Architecture:** The solution is split into six projects. `Core` owns scan models and single-pass metadata scanning; `Rules` owns the embedded JSON knowledge base and path classification; `Cleanup` owns preview validation, path safety, deduplication and execution; `Reporting` owns JSON/HTML exports; `App` is a WPF/MVVM shell that never deletes files directly; `Tests` verifies the safety boundary in isolated temporary directories. All destructive operations are derived from scan output, revalidated against the active rule version immediately before execution, and rejected unless an explicit cleanup rule allows them.

**Tech Stack:** C# 14 / .NET 10 / WPF / xUnit / System.Text.Json / GitHub Actions on `windows-latest`.

**Spec:** `docs/specs/2026-09-07-x-c-disk-inspector-design.md`

## Global Constraints

- Product name: `X C盘巡检官`.
- Target platform: Windows desktop; `C# / .NET 10 / WPF`.
- Scanning reads filesystem metadata only and never reads file contents.
- First launch must not scan automatically and must not request administrator rights.
- Reparse points, directory junctions and symlinks are not followed by default.
- Cleanup is allow-list only: any path without a currently valid cleanup rule is rejected.
- Never auto-delete personal folders, AI models, Python virtual environments, `node_modules`, chat/browser/app databases, WSL/Docker VHDX files, `WinSxS`, `Windows\\Installer`, driver stores or unknown paths.
- Cleanup tests operate only inside isolated temporary fixture directories and never delete from the real C drive.
- Reports must contain timestamp, app version, rules version, admin status, capacity, risk, consequence and recommendation, plus author `Aix`, Douyin `xch03209527` and GitHub `Aix9527/Codex-Doctor`.
- Publish output must include a single-file `win-x64` EXE.

---

## Planned File Map

- `XDiskInspector.sln` — solution entry point.
- `Directory.Build.props` — common nullable, implicit usings, language and version metadata.
- `src/XDiskInspector.Core/` — scan contracts, models, bounded large-file collection and scanner.
- `src/XDiskInspector.Rules/` — embedded `directory-rules.json`, rule loader and environment-aware path matcher.
- `src/XDiskInspector.Cleanup/` — safety policy, final preview builder, cleanup executor and recycle-bin adapter.
- `src/XDiskInspector.Reporting/` — JSON and HTML report writers.
- `src/XDiskInspector.App/` — WPF shell, theme, MVVM infrastructure, view models, pages and system-guidance commands.
- `tests/XDiskInspector.Tests/` — isolated fixture tests for scan, rule, selection, cleanup and reporting behavior.
- `assets/x-disk-inspector-icon.svg` and `assets/x-disk-inspector.ico` — icon source and Windows icon.
- `.github/workflows/windows-ci.yml` — restore, test, WPF build, single-file publish and artifact upload.
- `scripts/publish-win-x64.ps1` — reproducible release command.
- `docs/SAFETY.md`, `docs/USER_GUIDE.md`, `docs/example-report.json`, `docs/example-report.html` — delivery documentation.

### Task 1: Establish solution contracts and model behavior

**Files:**
- Create: `Directory.Build.props`, `XDiskInspector.sln`
- Create: `src/XDiskInspector.Core/XDiskInspector.Core.csproj`
- Create: `src/XDiskInspector.Core/Models/*.cs`, `src/XDiskInspector.Core/Abstractions/*.cs`
- Create: `tests/XDiskInspector.Tests/XDiskInspector.Tests.csproj`
- Test: `tests/XDiskInspector.Tests/Models/SelectionStateTests.cs`

**Interfaces:**
- Produces: `RiskLevel`, `CleanupRecommendation`, `ScanItemKind`, `ScanItem`, `ScanReport`, `ScanProgress`, `ScanOptions`, `DirectoryUsage`, `AccessIssue`, `IPathClassifier`.
- `ScanItem` contains `Selectable`, `Selected`, `RiskLevel`, `CleanupRuleId`, `BlockedReason`, `LastWriteTimeUtc` and `IsReparsePoint`.

- [ ] Write tests first proving a non-selectable item cannot become selected and a selectable item can be selected.
- [ ] Run `dotnet test tests/XDiskInspector.Tests/XDiskInspector.Tests.csproj --filter SelectionStateTests` and verify the tests fail because the model does not exist.
- [ ] Implement the minimal models and selection guard.
- [ ] Re-run the filtered tests and verify they pass.
- [ ] Commit as `feat: establish disk inspection core contracts`.

### Task 2: Implement the embedded software/directory rule library

**Files:**
- Create: `src/XDiskInspector.Rules/XDiskInspector.Rules.csproj`
- Create: `src/XDiskInspector.Rules/Models/DirectoryRule.cs`
- Create: `src/XDiskInspector.Rules/RuleLibrary.cs`
- Create: `src/XDiskInspector.Rules/PathRuleMatcher.cs`
- Create: `src/XDiskInspector.Rules/directory-rules.json`
- Test: `tests/XDiskInspector.Tests/Rules/PathRuleMatcherTests.cs`

**Interfaces:**
- Consumes: `IPathClassifier`, `RiskLevel`, `CleanupRecommendation` from Core.
- Produces: `RuleLibrary.Version`, `RuleLibrary.Rules`, `PathRuleMatcher.Classify(string path, FileSystemInfo? metadata = null)`.

- [ ] Write failing tests for `%LOCALAPPDATA%\\Temp`, WSL `ext4.vhdx`, `WinSxS`, NVIDIA OTA cache and an unknown path.
- [ ] Verify RED with `dotnet test ... --filter PathRuleMatcherTests`.
- [ ] Implement environment-variable expansion, `*` wildcard segment matching, case-insensitive Windows normalization and embedded JSON loading.
- [ ] Encode every first-version rule from the approved spec, including consequences, risk, recommendation, auto-clean permission and optional `minAgeDays`.
- [ ] Verify unknown paths receive no software guess and no cleanup rule.
- [ ] Run the full test project and commit as `feat: add conservative directory knowledge base`.

### Task 3: Implement the single-pass metadata scanner

**Files:**
- Create: `src/XDiskInspector.Core/Scanning/BoundedLargeFileSet.cs`
- Create: `src/XDiskInspector.Core/Scanning/FileSystemScanner.cs`
- Test: `tests/XDiskInspector.Tests/Scanning/FileSystemScannerTests.cs`

**Interfaces:**
- Produces: `Task<ScanReport> FileSystemScanner.ScanAsync(ScanOptions options, IProgress<ScanProgress>? progress, CancellationToken cancellationToken)`.
- Scanner receives an `IPathClassifier` and records rule-recognized items without retaining every uninteresting file.

- [ ] Write failing tests for top-level aggregation, descending large-file ordering, cancellation producing `IsComplete=false`, and no traversal through a directory reparse point where the platform permits creating one.
- [ ] Verify RED.
- [ ] Implement iterative single-pass traversal using a stack, metadata-only `FileInfo`/`DirectoryInfo`, batched progress updates, access-issue capture and a bounded large-file collection.
- [ ] Ensure inaccessible/disappearing entries are recorded or skipped without aborting the scan.
- [ ] Ensure report metadata contains timestamp, application version, rule version and administrator state.
- [ ] Run scanner tests and commit as `feat: add cancellable single-pass filesystem scanner`.

### Task 4: Implement cleanup allow-list validation and final preview

**Files:**
- Create: `src/XDiskInspector.Cleanup/XDiskInspector.Cleanup.csproj`
- Create: `src/XDiskInspector.Cleanup/Safety/SafePathPolicy.cs`
- Create: `src/XDiskInspector.Cleanup/Preview/CleanupPreviewService.cs`
- Create: `src/XDiskInspector.Cleanup/Models/*.cs`
- Test: `tests/XDiskInspector.Tests/Cleanup/SafePathPolicyTests.cs`
- Test: `tests/XDiskInspector.Tests/Cleanup/CleanupPreviewServiceTests.cs`

**Interfaces:**
- Produces: `PathSafetyResult SafePathPolicy.Validate(string path)` and `CleanupPreview CleanupPreviewService.Build(IEnumerable<ScanItem> selectedItems, string expectedRuleVersion)`.
- Preview contains only normalized, selected, selectable candidates with a valid rule id, current matching rule version and no parent/child duplicates.

- [ ] Write failing tests rejecting drive roots, user-profile root, Windows root, wildcard targets, reparse targets, missing cleanup rule ids and disabled items whose `Selected` field is tampered with.
- [ ] Write failing tests proving parent/child selections are deduplicated and preview mode does not mutate fixture files.
- [ ] Verify RED.
- [ ] Implement path normalization, forbidden-root checks, reparse detection, allow-list rule lookup and parent/child deduplication.
- [ ] Re-run cleanup preview tests and commit as `feat: enforce cleanup allow-list preview boundary`.

### Task 5: Implement conservative cleanup execution

**Files:**
- Create: `src/XDiskInspector.Cleanup/Execution/CleanupExecutor.cs`
- Create: `src/XDiskInspector.Cleanup/Execution/RecycleBinService.cs`
- Test: `tests/XDiskInspector.Tests/Cleanup/CleanupExecutorTests.cs`

**Interfaces:**
- Produces: `Task<CleanupRunResult> CleanupExecutor.ExecuteAsync(CleanupPreview preview, CleanupExecutionOptions options, CancellationToken stopAfterCurrentItem)`.
- Produces per-item states `Deleted`, `PartiallyDeleted`, `Skipped`, `Failed`; cancellation is checked only between items.

- [ ] Write failing tests proving only files older than seven days are deleted for the temp rule, changed timestamps are skipped, locked/permission failures are reported, and cancellation prevents the next item from starting.
- [ ] Verify RED.
- [ ] Implement immediate pre-delete revalidation of absolute path, rule id/version, last-write timestamp and reparse state.
- [ ] Implement file deletion and safe directory-content cleanup without taking ownership, killing processes or modifying ACLs.
- [ ] Implement recycle-bin clearing through the Windows Shell API behind `IRecycleBinService`; require an explicit irreversible-action flag before calling it.
- [ ] Re-run cleanup tests and commit as `feat: execute auditable conservative cleanup`.

### Task 6: Implement JSON and HTML reporting

**Files:**
- Create: `src/XDiskInspector.Reporting/XDiskInspector.Reporting.csproj`
- Create: `src/XDiskInspector.Reporting/JsonReportWriter.cs`
- Create: `src/XDiskInspector.Reporting/HtmlReportWriter.cs`
- Test: `tests/XDiskInspector.Tests/Reporting/ReportWriterTests.cs`

**Interfaces:**
- Produces: `Task WriteAsync(ScanReport report, string destinationPath, CancellationToken cancellationToken)` for each format.

- [ ] Write failing tests asserting author metadata, capacity, risk, consequence, recommendation and incomplete-scan status are present in both formats.
- [ ] Verify RED.
- [ ] Implement UTF-8 JSON with readable enum strings and a self-contained dark HTML report with encoded user/path text.
- [ ] Verify reports never serialize file contents or credential material because the model contains metadata only.
- [ ] Run reporting tests and commit as `feat: add auditable json and html reports`.

### Task 7: Build the WPF/MVVM application shell and all five product pages

**Files:**
- Create: `src/XDiskInspector.App/XDiskInspector.App.csproj`
- Create: `src/XDiskInspector.App/App.xaml(.cs)`, `MainWindow.xaml(.cs)`
- Create: `src/XDiskInspector.App/Themes/DarkTheme.xaml`
- Create: `src/XDiskInspector.App/Mvvm/*.cs`
- Create: `src/XDiskInspector.App/ViewModels/*.cs`
- Create: `src/XDiskInspector.App/Views/HomeView.xaml`, `ScanResultsView.xaml`, `CleanupView.xaml`, `OptimizationView.xaml`, `AboutView.xaml` and code-behind files
- Test: compile gate in `.github/workflows/windows-ci.yml`

**Interfaces:**
- `MainViewModel` owns navigation and shared `ScanSessionViewModel` state.
- UI calls services (`FileSystemScanner`, `CleanupPreviewService`, `CleanupExecutor`, report writers); no view deletes filesystem entries directly.

- [ ] Create the WPF project only after the service tests are green.
- [ ] Implement graphite background, cyan normal state, orange judgment state and red irreversible-action state; create a radar/capacity-ring home hero and respect `SystemParameters.ClientAreaAnimation` before starting optional animations.
- [ ] Implement Start Scan / Load Last Report / Safe Cleanup without auto-scan or startup elevation.
- [ ] Implement scan progress with current directory, file count, logical bytes, elapsed time and cancel.
- [ ] Implement Results filters (size/risk/software/recommendation), large-file threshold, safe-select-all and fixed selected summary.
- [ ] Implement Cleanup groups, final preview dialog, separate recycle-bin irreversible checkbox, stop-after-current-item behavior and per-item results.
- [ ] Implement About/report export UI with author metadata.
- [ ] Build `src/XDiskInspector.App/XDiskInspector.App.csproj -c Release` on Windows CI and commit as `feat: build X C盘巡检官 WPF experience`.

### Task 8: Add official system optimization guidance

**Files:**
- Create: `src/XDiskInspector.App/Services/SystemOptimizationService.cs`
- Extend: `src/XDiskInspector.App/ViewModels/OptimizationViewModel.cs`
- Extend: `src/XDiskInspector.App/Views/OptimizationView.xaml`
- Test: `tests/XDiskInspector.Tests/Optimization/SystemOptimizationServiceTests.cs`

**Interfaces:**
- Produces safe commands that open Windows settings or invoke DISM only; never exposes direct deletion for component-store, installer-cache or VHDX targets.

- [ ] Write tests proving `WinSxS`, `Windows\\Installer` and VHDX actions never resolve to direct filesystem deletion.
- [ ] Implement pagefile detection/advice, `ms-settings:appsfeatures`, DISM component-store analyze/start-component-cleanup actions, and WSL/Docker compact/cleanup guidance.
- [ ] Keep admin elevation scoped to the explicit official operation that needs it.
- [ ] Run tests and commit as `feat: add official system optimization guidance`.

### Task 9: Add release assets, documentation and reproducible single-file publish

**Files:**
- Create: `assets/x-disk-inspector-icon.svg`, `assets/x-disk-inspector.ico`
- Create: `scripts/publish-win-x64.ps1`
- Create: `docs/USER_GUIDE.md`, `docs/SAFETY.md`, `docs/example-report.json`, `docs/example-report.html`
- Create: `README.md`

**Interfaces:**
- Publish command: `dotnet publish src/XDiskInspector.App/XDiskInspector.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true`.

- [ ] Add the X + disk ring + scan beam icon in SVG and ICO form.
- [ ] Configure app assembly metadata and application icon.
- [ ] Write user and safety documentation that explicitly lists what the application refuses to delete.
- [ ] Add example JSON/HTML reports containing no real personal paths or secrets.
- [ ] Commit as `docs: add release packaging and safety documentation`.

### Task 10: Add Windows CI, publish artifact verification and final repository review

**Files:**
- Create: `.github/workflows/windows-ci.yml`

**Interfaces:**
- CI stages: checkout → setup .NET 10 → restore → test → Release build → `win-x64` single-file publish → assert exactly one app EXE exists → upload artifact.

- [ ] Configure `actions/setup-dotnet` with `10.0.x` and NuGet caching.
- [ ] Run the full xUnit suite on `windows-latest`.
- [ ] Build the WPF project in Release.
- [ ] Publish single-file `win-x64` and fail CI if the expected EXE is missing.
- [ ] Upload the publish directory as `X-C盘巡检官-win-x64`.
- [ ] Review the final tree against every section of the approved spec, verify there are no `TODO`/`TBD` placeholders, and ensure all destructive test paths originate from temporary fixtures.
- [ ] Push the implementation branch and create a pull request against `main`.
