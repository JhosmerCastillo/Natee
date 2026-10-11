# Natee — Product Roadmap (`ROADMAP.md`)

This document tracks the planned evolution of **Natee** across three versions and records the verified implementation status of **Version 1 (MVP)**.

Status legend:
- **Implemented (Verified):** Complete, covered by automated tests, and verified in the target runtime environment.
- **Implemented (Cross-Compiled / Unit-Tested):** Complete in source and release build artifacts, verified via unit/mocked tests; requires native target OS desktop session for full interactive GUI verification.
- **Planned (Future Version):** Intentionally scoped for Version 2 or Version 3 and not implemented in the MVP.

---

## Version 1 — MVP (Current Release: `v1.0.0`)

| Feature / Requirement | Status | Notes & Architectural Context |
| :--- | :--- | :--- |
| Original Natee manatee branding & vector/raster icon assets | **Implemented (Verified)** | Custom SVG (`natee-logo.svg`, `natee-tray.svg`), PNG (`256px`, `64px`, `32px`), and ICO assets in `assets/branding/` and embedded in `Natee.App`. |
| System tray integration, tooltip & context menu | **Implemented (Verified)** | Tray icon with dynamic status tooltip and menu (*Open Dashboard*, *Run Network Diagnosis*, *Run Speed Test*, *Generate Report*, *Settings*, *Exit*). |
| Background monitoring while dashboard is closed | **Implemented (Verified)** | `ShutdownMode.OnExplicitShutdown` + `MainWindow.OnClosing` hides window to tray; `MonitoringOrchestrator` continues sampling until explicit *Exit*. |
| Single-instance process lock & optional session autostart | **Implemented (Verified)** | Exclusive OS file lock (`SingleInstanceGuard`) and user-level XDG (`~/.config/autostart/natee.desktop`) / Windows Startup folder manager (disabled by default). |
| Compact Overview & 7-tab Detailed Diagnostic UI | **Implemented (Verified)** | One-click toggle between minimalist Compact Overview and full tabbed interface with Light/Dark/System theme support. |
| Live network interface status, IPv4/IPv6, gateway, DNS & throughput | **Implemented (Verified)** | Real byte counter deltas (`Mbps`), clearly labeled as observed interface traffic rather than maximum ISP capacity. |
| Multi-target latency & packet loss monitoring | **Implemented (Verified)** | Concurrent ICMP/TCP probes across configurable targets (`1.1.1.1`, `8.8.8.8`, `9.9.9.9`) without false offline alarms from a single unresponsive host. |
| Explicit user-triggered Internet speed test | **Implemented (Verified)** | Real HTTPS download/upload bandwidth measurement via configurable endpoints (default: Cloudflare Edge `speed.cloudflare.com/__down` and `/__up`). Never runs automatically. |
| One-Click Network Diagnosis (10 checks + root-cause summary) | **Implemented (Verified)** | Evaluates adapter, IP, gateway, DNS config, DNS resolution, target reachability, latency, packet loss, Internet reachability, and optional bounded route trace. |
| CPU utilization, operating frequency & hardware temperature | **Implemented (Verified)** | Real OS counters (`/proc/stat`, `cpufreq/scaling_cur_freq`, `/sys/class/hwmon`, `GetSystemTimes`). Distinguishes `Available`, `TemporarilyInaccessible`, and `Unsupported` sensors without kernel drivers. |
| Physical RAM, OS uptime, storage volumes & top processes | **Implemented (Verified)** | Physical RAM excluding swap, true OS boot uptime, mounted disk capacities + Linux I/O throughput, and top processes sortable by CPU% or RAM. |
| Intelligent notifications with hysteresis, cooldown & recovery | **Implemented (Verified)** | Configurable alerts for connection loss/recovery, high latency, packet loss, low disk space, and optional CPU/RAM pressure via `notify-send` / Alert Log. |
| PDF and JSON technical reports with privacy redactions | **Implemented (Verified)** | SkiaSharp PDF and `System.Text.Json` exporters with deep structured + regex scrubbing of excluded fields. |
| Local SQLite persistence & schema migrations | **Implemented (Verified)** | WAL-mode SQLite database in user app-data directory with versioned migrations, retention pruning, and one-click history clearing. |
| Linux x64 (`tar.gz`) & Windows x64 (`zip`) release builds | **Implemented (Verified on Linux / Cross-Compiled for Windows)** | Self-contained `.NET 10.0` release packages generated in `artifacts/releases/`. Runtime verified on Fedora Linux 44 (KDE Plasma); Windows x64 cross-compiled from Linux and remains unverified interactively until tested on a Windows host. |

---

## Version 2 — IT Support & Troubleshooting Tool (Planned — Not Implemented in MVP)

Version 2 builds on Natee's clean provider and reporting interfaces (`INetworkDiagnosticsService`, `ILocalPersistenceService`, `ITicketIntegrationBoundary`) to streamline corporate Help Desk workflows:

- **Extended Diagnostic History & Trend Analysis:** Interactive timeline views of historical latency, packet loss, and throughput stored in SQLite.
- **Diagnostic Session Comparison:** Side-by-side diffing of two `DiagnosticReport` snapshots (for example, comparing office Ethernet vs. home Wi-Fi or pre-VPN vs. post-VPN).
- **Configurable Diagnostic Profiles:** Exportable/importable JSON profiles for corporate environments (custom internal DNS resolvers, intranet gateways, proxy checks, and split-tunnel VPN checks).
- **Guided Troubleshooting Workflows:** Interactive step-by-step remediation guides for common faults (DHCP lease renewal verification, captive portal detection, MTU fragmentation checks, Wi-Fi channel congestion hints).
- **Direct Support-Ticket Integrations:** Concrete implementations of `ITicketIntegrationBoundary` for **Zammad**, **Jira Service Management**, and **ServiceNow**, allowing explicit user-approved ticket creation and attachment upload.
- **Additional Export Formats:** Markdown and HTML single-file diagnostic bundles and Spanish/multilingual UI localization packs.

---

## Version 3 — DevOps & Fleet Observability / RMM (Planned — Not Implemented in MVP)

Version 3 extends Natee into an optional, privacy-conscious fleet observability agent for small-to-medium IT and DevOps teams while preserving Natee's standalone local mode:

- **Optional Centralized Monitoring Server:** Opt-in registration with a self-hosted management backend.
- **Authenticated Device Registration & mTLS:** Mutual TLS device identity and zero-trust enrollment tokens.
- **Multi-Device Fleet Dashboard:** Aggregated health views, alert routing (Webhook, Slack, PagerDuty, Email), and hardware/OS asset inventory.
- **Role-Based Access Control (RBAC) & Immutable Audit Logs:** Granular permissions and cryptographic audit trails for compliance.
- **Scalable Telemetry Ingestion:** OpenTelemetry (OTLP) metric and diagnostic export support.
- **Explicitly Authorized Diagnostic Workflows:** Remote triggering of read-only network diagnostics and report collection with user consent and full auditability (never arbitrary remote command execution).
