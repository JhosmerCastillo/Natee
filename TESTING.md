# Natee — Testing and Verification Guide (`TESTING.md`)

This document explains how to build and execute Natee's automated test suite, what each test category verifies, how mocked unit tests are separated from operating-system integration tests, and what runtime environments have been verified.

---

## 1. Executing the Automated Test Suite

From the repository root (`Natee/`), run:

```bash
# Run all unit and integration tests
dotnet test Natee.sln --verbosity normal

# Run only isolated Unit tests (no real hardware or OS dependencies required)
dotnet test Natee.sln --filter "Category=Unit" --verbosity normal

# Run only Integration tests (SQLite persistence, migrations, OS providers, ViewModel workflows)
dotnet test Natee.sln --filter "Category=Integration" --verbosity normal
```

---

## 2. Test Categories and Coverage

All tests are located in `tests/Natee.Tests/` using **xUnit 2.9** on **.NET 10.0 LTS (`net10.0`)**.

### 2.1 Unit Tests (`Category=Unit`)
Use deterministic inputs, temporary virtual directories, or mocked dependencies so they run reproducibly on any machine without administrator privileges or an active Internet connection:

- **`NetworkAndDiagnosticsTests.cs`:**
  - `CalculateThroughputMbps_ComputesExpectedRateAndHandlesEdgeCases`: Verifies Mbps calculation from byte counter deltas, counter wrap/reset safety, and zero-interval guards.
  - `CalculateProbeMetrics_ComputesAccurateLatencyAndPacketLoss` & `CalculateProbeMetrics_WhenAllFail_ReturnsNullLatencyAnd100PercentLoss`: Verifies round-trip latency averaging and packet-loss percentage math.
  - `EvaluateConnectivity_SingleFailedTargetDoesNotMarkConnectionOffline`: Verifies that when 1 of 3 diagnostic targets fails to respond (e.g., ICMP rate limiting), Natee does **not** falsely declare the Internet connection offline.
  - `EvaluateConnectivity_DistinguishesFailedRecoveryDegradedAndInconclusiveStates`: Verifies state transitions across offline, healthy recovery, high-latency degradation, and inconclusive startup states.
  - `BuildDiagnosticSummary_DistinguishesDnsFailureFromGatewayAndInternetFailures`: Verifies root-cause classification when IP transport succeeds but DNS resolution fails, as well as inconclusive check handling.
- **`SystemMetricsAndSensorsTests.cs`:**
  - `CalculateMemoryMetrics_ComputesPhysicalRamUsageAccurately`: Verifies physical RAM usage and percentage math.
  - `CreateVolumeMetric_DistinguishesZeroRemainingCapacityFromUnavailableVolume`: Verifies the distinction between an accessible disk with `0` remaining bytes (`100%` used) and an inaccessible/unmounted volume.
  - `HardwareSensorProvider_HandlesAvailableAndUnsupportedSensorsCorrectly`: Uses isolated temporary sysfs trees to verify both `SensorAvailabilityStatus.Unsupported` (returning `null` temperature rather than fabricated numbers) and `SensorAvailabilityStatus.Available` (`coretemp` millidegree parsing `64500` -> `64.5 °C`).
  - `FormatUptime_FormatsDaysHoursAndMinutesCorrectly`: Verifies human-readable OS uptime formatting.
- **`NotificationAndSettingsTests.cs`:**
  - `NotificationEngine_RespectsHysteresisCooldownAndRecovery`: Verifies that isolated single-sample spikes are suppressed by consecutive-sample hysteresis, sustained breaches trigger an alert, repeated breaches during an active state or cooldown window are suppressed, and recovery alerts fire when metrics normalize.
  - `NotificationEngine_TriggersLowDiskSpaceAndConnectionLostAlerts`: Verifies low disk space and connection loss alert generation.
  - `SettingsValidator_ClampsInvalidValuesAndReportsErrors`: Verifies bounds clamping and validation error reporting for out-of-range intervals, thresholds, retention days, process counts, and invalid URLs.
- **`PrivacyAndReportingTests.cs`:**
  - `GeneratePdfAndJsonReports_AppliesPrivacyRedactionsCompletelyToExportedFiles`: Generates real `.pdf` and `.json` report files from a sensitive snapshot and inspects the JSON text, raw PDF bytes, PDF metadata, and decompressed PDF `FlateDecode` streams to prove that excluded hostnames, usernames, local/public IPs, MAC addresses, Wi-Fi SSIDs, process names, and local paths are completely absent from the exported files.

### 2.2 Integration Tests (`Category=Integration`)
Verify real SQLite database operations, file locking, HTTP speed-test stream handling, cancellation tokens, and operating-system providers:

- **`SqliteAndPlatformIntegrationTests.cs`:**
  - `SqlitePersistenceService_AppliesMigrationsSavesQueriesPrunesAndClears`: Creates an isolated SQLite database, verifies `schema_migrations` version `1` and idempotency, saves and queries diagnostics, speed tests, and alerts, verifies retention pruning (`PruneExpiredHistoryAsync`), and verifies full cleanup (`ClearAllHistoryAsync`).
  - `SpeedTestService_RespectsCancellationAndMeasuresTransferAccurately`: Verifies `OperationCanceledException` handling and stream transfer throughput math.
  - `RealOperatingSystemProviders_ReturnValidNonFabricatedMetrics`: Queries the host OS for real CPU, physical RAM, uptime, storage volumes, and top processes.
  - `SingleInstanceGuard_PreventsSecondConcurrentInstanceInSameDirectory`: Verifies that a second concurrent `SingleInstanceGuard` instance cannot acquire the lock until the first instance disposes it.
- **`OrchestratorAndViewModelTests.cs`:**
  - `MonitoringOrchestrator_SamplesMetricsGeneratesRedactedReportAndStopsCleanly`: Verifies background loop start/stop lifecycle and report document construction.
  - `JsonSettingsService_PersistsAndReloadsNormalizedSettings`: Verifies JSON settings persistence on disk.
  - `MainWindowViewModel_ExecutesSettingsAndReportGenerationWorkflows`: Verifies end-to-end ViewModel report export, report deletion, compact mode switching, and application exit commands.

---

## 3. Recorded Test Suite Results

Executed command:
```bash
dotnet test Natee.sln --configuration Release --no-build --verbosity normal
```

Observed result on **Fedora Linux 44 (KDE Plasma Desktop Edition, x86_64, .NET SDK 10.0.401 / Runtime 10.0.12, Avalonia UI 11.3.22)**:
- **Total tests:** `21`
- **Passed:** `21`
- **Failed:** `0`
- **Skipped:** `0`
- **Execution time:** `0.8566 seconds` (`1.79s` total VSTest elapsed)

---

## 4. Runtime Memory Profile (Fedora Linux 44 KDE Plasma)

Executed via `./artifacts/publish/linux-x64/Natee --smoke-test` in a live KDE Plasma Wayland session (`XDG_SESSION_TYPE=wayland`, `WAYLAND_DISPLAY=wayland-0`, `DISPLAY=:0`, windowing backend `Avalonia.X11.X11Window` via **XWayland**):

| Measurement Phase | .NET Managed Heap | .NET GC Committed | Anonymous Resident (`RssAnon`) | Private Mapped Pages (`Dirty+Clean`) | Proportional Set Size (`PSS`) | File-Backed Mappings (`RssFile+Shmem`) | Total Resident (`VmRSS`) |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **Idle Monitoring** (post-GC) | `4.68 MB` | `12.80 MB` | `50.51 MB` | `124.18 MB` | `131.97 MB` | `125.35 MB` | **`175.85 MB`** |
| **Post-Diagnosis** (before GC) | `5.86 MB` | `16.43 MB` | `61.93 MB` | `138.32 MB` | `147.13 MB` | `133.63 MB` | **`195.57 MB`** |
| **Post-Diagnosis** (after compacting GC) | `4.80 MB` | `14.43 MB` | `59.97 MB` | `136.36 MB` | `145.17 MB` | `133.63 MB` | **`193.60 MB`** |

### Metric Definitions & Overlap Warning (Do Not Add Columns Together)
The columns above represent nested or orthogonal views of the same process address space and **must not be summed together**:
- **`.NET Managed Heap` (`~4.68 MB` idle / `~4.80 MB` post-GC):** Live C# objects managed by the .NET garbage collector (`GC.GetTotalMemory`). This is a subset of `.NET GC Committed` (`~12.80–14.43 MB`), which in turn is a subset of `RssAnon`.
- **`Anonymous Resident Memory (RssAnon)` (`~50.51 MB` idle / `~59.97 MB` post-GC):** Resident pages not backed by any file on disk (`/proc/self/status`). Includes the `.NET` GC heap, CLR runtime structures, JIT-compiled native code, thread stacks, and native Skia/X11 heap allocations.
- **`File-Backed Mappings (RssFile + RssShmem)` (`~125.35 MB` idle / `~133.63 MB` post-GC):** Resident pages backed by files on disk or shared memory segments (`/proc/self/status`). Note that on Linux, `RssAnon + RssFile + RssShmem = Total VmRSS` (`50.51 + 125.35 = 175.85 MB`).
- **`Private Mapped Pages (Private_Dirty + Private_Clean)` (`~124.18 MB` idle / `~136.36 MB` post-GC):** Resident pages mapped exclusively by the Natee process (`/proc/self/smaps_rollup`). This overlaps with both `RssAnon` and `RssFile` because it includes `RssAnon` (`~50.51 MB`) plus ~`73.67 MB` of file-backed pages from Natee's self-contained `.NET 10` assemblies and bundled `libSkiaSharp.so` that no other running process has mapped.
- **`Proportional Set Size (PSS)` (`~131.97 MB` idle / `~145.17 MB` post-GC):** `Private Mapped Pages` plus Natee's proportional share ($1/N$) of OS pages shared with other processes (`libc`, Mesa/GLX drivers, X11 libraries, and system font caches).
- **`Total Resident Memory (VmRSS)` (`~175.85 MB` idle / `~193.60 MB` post-GC):** Total physical RAM pages currently resident in the process's page tables, including all private pages and the full size of shared OS/graphics libraries.

### Comparison Against Original Performance Target
- **Original Target:** Approximately `100 MB` of total process memory during normal operation.
- **Measured Deviation:** Natee **does not meet** the original `~100 MB` total resident memory target. Measured `VmRSS` exceeds the `100 MB` target by **`~75.85 MB` during idle monitoring (`175.85 MB`)** and **`~93.60 MB` after a complete diagnostic run (`193.60 MB`)** (or by `~31.97 MB` idle and `~45.17 MB` post-diagnosis when measured via `PSS`).
- **Technical Cause:** While Natee's own managed `.NET` heap is compact (`4.68–4.80 MB`), bundling a self-contained `.NET 10` runtime with `Avalonia UI`, `SkiaSharp` (`libSkiaSharp.so`), HarfBuzz/FreeType text rendering, and XWayland/Mesa GPU drivers maps `125.35–133.63 MB` of file-backed code and data pages into the process's resident working set.

---

## 5. Platform Verification Matrix & Current Limitations

| Platform / Environment | Build & Package Verified | Automated Tests Verified | Runtime GUI & Tray Verified | Notes |
| :--- | :--- | :--- | :--- | :--- |
| **Fedora Linux 44 (KDE Plasma Wayland session via XWayland, x64)** | **Yes** (`natee-v1.0.0-linux-x64.tar.gz`) | **Yes** (`21/21` passed) | **Yes** (Verified via `--smoke-test` runtime execution) | Runs windowing via `Avalonia.X11.X11Window` over **XWayland** (native Wayland windowing backend is not used) and system tray via D-Bus `StatusNotifierItem`. Reads real `/proc/stat`, `cpufreq`, `coretemp` CPU temperature, `/proc/meminfo`, `/proc/uptime`, Btrfs volumes, and active `wlo1` Wi-Fi interface. |
| **Linux GNOME Desktop** | **Yes** (same `linux-x64` binary) | N/A | **Not tested in live GNOME session** | Documented limitation: Vanilla GNOME requires `gnome-shell-extension-appindicator` for tray icons; Natee opens its main dashboard window on launch as a reliable fallback. |
| **Windows 10 / 11 (x64)** | **Yes** (`natee-v1.0.0-win-x64.zip` cross-compiled) | Unit logic covered | **Unverified interactively (cross-compiled only; no Windows host available in build environment)** | Windows P/Invoke (`GetSystemTimes`, `GlobalMemoryStatusEx`), `NetworkInterface`, and `DriveInfo` providers are compiled into the `win-x64` package; interactive Windows tray/GUI verification remains unverified until executed on a Windows x64 machine. |
