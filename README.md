<p align="center">
  <img src="assets/branding/natee-logo.svg" alt="Natee Manatee Logo" width="128" height="128" />
</p>

<h1 align="center">Natee</h1>
<p align="center"><strong>Calm monitoring. Clear diagnostics.</strong></p>
<p align="center">
  A lightweight, minimalist, privacy-first network and system monitoring tray application built with C#, .NET 10 LTS, Avalonia UI, and SQLite.
</p>

---

## 1. Overview & Purpose

**Natee** is named after the manatee — a calm, gentle, and non-intimidating animal. Designed for both everyday desktop users and professional IT Support, Network Administration, and Cybersecurity troubleshooting workflows, Natee monitors network connectivity and system resources quietly from the system tray and clearly explains when something requires attention.

Unlike bloated optimization suites or cloud-tethered agents, Natee operates **100% locally** with **zero telemetry**, requires **no administrator privileges or kernel drivers**, and produces privacy-redacted **PDF** and **JSON** technical reports ready to attach to IT Support tickets.

---

## 2. Version 1 (MVP) Features

- **System Tray & Dual-Mode Dashboard:**
  - Original Natee manatee vector branding and high-contrast tray icon (`assets/branding/`).
  - Live tray tooltip summarizing connectivity health, download/upload throughput, latency, and CPU load.
  - Tray context menu (*Open Dashboard*, *Run Network Diagnosis*, *Run Speed Test*, *Generate Report*, *Settings*, *Exit*).
  - Continues background monitoring when the dashboard window is closed; exits cleanly with zero lingering background workers when *Exit* is chosen.
  - One-click switch between a minimalist **Compact Overview** and a **7-tab Detailed Diagnostic Interface** with Light, Dark, and System theme support.
- **Network Monitoring & Live Throughput:**
  - Displays connection health (`Healthy`, `Degraded`, `Failed`, `Inconclusive`), active network interface, connection type (`Ethernet`, `Wi-Fi (802.11)`, `VPN / Tunnel`), local IPv4/IPv6 addresses, default gateway, and configured DNS servers.
  - Measures live observed download and upload traffic (`Mbps`) from real OS interface counters (clearly distinguished from maximum ISP capacity).
  - Multi-target latency (`ms`) and packet-loss (`%`) monitoring across configurable targets (`1.1.1.1`, `8.8.8.8`, `9.9.9.9`) that avoids false offline alarms when a single remote host drops ICMP packets.
- **One-Click Network Diagnosis:**
  - Executes 10 automated checks asynchronously with cancellation support: Network Interface Status, IP Configuration, Default Gateway Reachability, DNS Configuration, DNS Name Resolution, Diagnostic Target Reachability, Network Latency, Packet Loss, Internet Reachability, and optional bounded Route Tracing.
  - Synthesizes an evidence-based root-cause summary (for example, distinguishing a DNS resolution failure over a working IP connection from a local gateway failure).
- **Explicit User-Triggered Internet Speed Test:**
  - Measures actual HTTPS download and upload throughput (`Mbps`) against configurable endpoints (default: Cloudflare Edge `https://speed.cloudflare.com/__down` and `/__up`).
  - Never runs automatically in the background and explicitly discloses data usage (~20 MB per run) and public IP exposure.
- **System Resource & Process Monitoring:**
  - **CPU:** Overall utilization (`%`), real-time operating frequency (`MHz` / `GHz`), and non-invasive hardware temperature (`°C`) via OS sensors (`/sys/class/hwmon` / `/sys/class/thermal` on Linux), clearly distinguishing `Available`, `TemporarilyInaccessible`, and `Unsupported` sensors without installing kernel drivers.
  - **Physical Memory (RAM):** Total, used, and available physical RAM (`GB` and `%`), strictly separated from swap usage.
  - **System Uptime:** True elapsed time since operating-system boot (`/proc/uptime` / `Environment.TickCount64`).
  - **Storage Volumes:** Mounted disk capacities, used/free space, usage percentages, and Linux block-device read/write throughput while safely handling unmounted or restricted drives.
  - **Top Processes:** Compact process resource table sortable by CPU% (normalized across logical cores) or physical RAM, with configurable entry count.
- **Intelligent Notifications & Local SQLite Persistence:**
  - Configurable alerts for Internet connection lost/restored, high latency, excessive packet loss, low disk space, and optional CPU/RAM pressure.
  - Built-in consecutive-sample hysteresis, anti-spam cooldowns, and automatic recovery notifications via native desktop notifications (`notify-send` / Alert Log).
  - Local WAL-mode SQLite database (`natee_history.db`) with schema migrations, configurable retention pruning (`1–90` days), and one-click history clearing.
- **Technical Reports (PDF & JSON) with Privacy Redactions:**
  - Prominent **Generate Report** workflow producing both a human-readable **PDF report** (`SkiaSharp`) and a machine-readable **JSON report** (`System.Text.Json`).
  - Privacy-preserving defaults allow redacting hostname, username, local IPs, public IPs, MAC addresses, adapter IDs, Wi-Fi SSIDs, process names, and local file paths from both exported files and PDF metadata.

---

## 3. Supported Operating Systems & Verified Environments

| Platform | Target Architecture | Build Artifact | Verification Status |
| :--- | :--- | :--- | :--- |
| **Linux (Priority: Fedora KDE Plasma)** | `linux-x64` | `artifacts/releases/natee-v1.0.0-linux-x64.tar.gz` | **Compiled, Automated Tests Passed (21/21), and Runtime Verified on Fedora Linux 44 (KDE Plasma Wayland session via XWayland / `Avalonia.X11.X11Window` & D-Bus `StatusNotifierItem` tray)** |
| **Linux (GNOME Desktop)** | `linux-x64` | `artifacts/releases/natee-v1.0.0-linux-x64.tar.gz` | **Compiled** (Requires `gnome-shell-extension-appindicator` for top-bar tray icon; main window works out of the box via X11/XWayland) |
| **Windows 10 / 11** | `win-x64` | `artifacts/releases/natee-v1.0.0-win-x64.zip` | **Cross-Compiled & Unit-Tested** (Packaged self-contained `win-x64` release archive; interactive Windows desktop execution remains unverified until run on a Windows host) |

---

## 4. Technology Stack & Measured Resource Footprint

- **Language & Runtime:** C# 14 / .NET 10 LTS (`net10.0`, verified with `.NET SDK 10.0.401`)
- **Desktop UI & MVVM:** Avalonia UI `11.3.22` + `CommunityToolkit.Mvvm 8.4.0` + `Microsoft.Extensions.DependencyInjection 10.0.12`
- **Local Persistence:** SQLite via `Microsoft.Data.Sqlite 10.0.12`
- **PDF & JSON Reporting:** `SkiaSharp 2.88.9` (`SKDocument.CreatePdf`) & `System.Text.Json`
- **Automated Testing:** `xUnit 2.9.2` (`21` unit and integration tests)
- **IDE Support:** Opens, builds, and runs cleanly in **JetBrains Rider** (`Natee.sln`)

### Measured Memory Usage vs. Original Target (Fedora Linux 44 KDE Plasma, x64)
- **Original Design Target:** Approximately `100 MB` of total process resident memory (`RSS`).
- **Actual Verified Measurements (Overlapping Metrics — Do Not Sum):**
  - **Idle Monitoring:** `~4.68 MB` managed `.NET` heap (`~12.80 MB` committed GC memory) | `~50.51 MB` anonymous resident memory (`RssAnon`) | `~124.18 MB` private mapped pages | `~131.97 MB` Proportional Set Size (`PSS`) | **`~175.85 MB` total resident memory (`VmRSS`)**.
  - **Post-Diagnostic Run (after compacting GC):** `~4.80 MB` managed `.NET` heap (`~5.86 MB` before GC) | `~59.97 MB` anonymous resident memory (`RssAnon`) | `~136.36 MB` private mapped pages | `~145.17 MB` `PSS` | **`~193.60 MB` total resident memory (`VmRSS`, `~195.57 MB` before GC)**.
- **Target Deviation & Explanation:** Natee **exceeds** its original `~100 MB` total-memory target by `~75.85 MB` (`VmRSS`) during idle monitoring and `~93.60 MB` after a full diagnostic run. While Natee's own C# objects on the managed `.NET` heap consume less than `5 MB`, total process `VmRSS` includes `~125.35–133.63 MB` of memory-mapped runtime and native library pages (`RssFile + RssShmem`) from the self-contained `.NET 10` runtime, `Avalonia UI`, `libSkiaSharp.so`, font caches, and XWayland/Mesa graphics drivers. See [ARCHITECTURE.md](ARCHITECTURE.md) and [TESTING.md](TESTING.md) for the full breakdown.

---

## 5. Quick Start — Build, Test, and Run

```bash
# Restore dependencies
dotnet restore Natee.sln

# Build solution
dotnet build Natee.sln --configuration Release --no-restore

# Run automated unit and integration tests
dotnet test Natee.sln --configuration Release --no-build --verbosity normal

# Launch Natee desktop & tray application
dotnet run --project src/Natee.App/Natee.App.csproj --configuration Release
```

Prebuilt self-contained release archives are generated in `artifacts/releases/`:
- **Linux x64:** `artifacts/releases/natee-v1.0.0-linux-x64.tar.gz`
- **Windows x64:** `artifacts/releases/natee-v1.0.0-win-x64.zip`

---

## 6. Documentation Index

- [**RELEASE_NOTES_v1.0.0.md**](RELEASE_NOTES_v1.0.0.md) — Release notes for Natee v1.0.0 (MVP), feature summary, platform matrix, and known limitations.
- [**INSTALLATION.md**](INSTALLATION.md) — Prerequisites, Linux & Windows installation, tray setup, building from source, troubleshooting, and uninstallation.
- [**ARCHITECTURE.md**](ARCHITECTURE.md) — Solution structure, Mermaid component diagram, domain/infrastructure boundaries, data flow, and design decisions.
- [**ROADMAP.md**](ROADMAP.md) — Version 1 (MVP) verified status, Version 2 (IT Support Tool), and Version 3 (DevOps & RMM).
- [**TESTING.md**](TESTING.md) — Test execution commands, unit vs. integration test breakdown, recorded results, and platform verification matrix.
- [**SECURITY.md**](SECURITY.md) — Zero-telemetry policy, external network test disclosure, report redaction rules, local data retention, and vulnerability reporting.
- [**GITHUB_RELEASE.md**](GITHUB_RELEASE.md) — Step-by-step Git initialization, remote setup, tagging, and GitHub Release publication commands.

---

## 7. License & Third-Party Notices

Natee is released under the [**MIT License**](LICENSE). All production NuGet dependencies (`Avalonia`, `CommunityToolkit.Mvvm`, `Microsoft.Extensions.DependencyInjection`, `Microsoft.Data.Sqlite`, `SkiaSharp`) are distributed under permissive open-source licenses (MIT).
