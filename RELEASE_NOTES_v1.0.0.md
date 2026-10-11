# Natee v1.0.0 — Release Notes (MVP)

- **Tagline:** *"Calm monitoring. Clear diagnostics."*
- **Release Version:** `1.0.0` (MVP)
- **Target Framework:** `.NET 10.0 LTS` (`net10.0`)
- **UI Framework:** Avalonia UI `11.3.22`
**License:** MIT

---

## 1. Overview

**Natee** is a lightweight, minimalist, privacy-first network and system monitoring tray application built with C#, .NET 10 LTS, Avalonia UI, and SQLite. Named after the manatee — a calm, gentle, and non-intimidating animal — Natee monitors network connectivity and system resources quietly in the background and provides clear, actionable diagnostics for everyday desktop users, IT Support technicians, Network Administrators, and Cybersecurity analysts.

---

## 2. Implemented Features in v1.0.0 (MVP)

### System Tray & Dual-Mode Interface
- **Original Manatee Branding:** Custom scalable vector (`natee-logo.svg`, `natee-tray.svg`) and raster (`256px`, `64px`, `32px` PNG and ICO) assets designed to remain recognizable at system-tray icon sizes.
- **System Tray Integration:** Live tray tooltip summarizing connectivity health, download/upload throughput, latency, and CPU load, paired with a native context menu (*Open Dashboard*, *Run Network Diagnosis*, *Run Speed Test*, *Generate Report*, *Settings*, *Exit*).
- **Background Monitoring & Clean Shutdown:** Closing the dashboard window hides Natee to the system tray while background monitoring continues. Selecting *Exit* stops all background workers, releases locks, and terminates cleanly.
- **Single-Instance Guard & Session Autostart:** Exclusive OS file lock prevents duplicate monitoring instances; optional user-level session startup (`.desktop` entry on Linux, Startup script on Windows) is disabled by default.
- **Compact & Detailed Views:** One-click toggle between a compact overview card layout and a 7-tab detailed diagnostic interface with Light, Dark, and System theme support.

### Network Monitoring & Diagnostics
- **Live Network Condition:** Displays connection status (`Healthy`, `Degraded`, `Failed`, `Inconclusive`), active network adapter, connection type (`Ethernet`, `Wi-Fi (802.11)`, `VPN / Tunnel`), local IPv4/IPv6 addresses, default gateway, and configured DNS resolvers.
- **Live Observed Throughput:** Measures real interface receive and transmit traffic in `Mbps` from OS counters (explicitly distinguished from maximum ISP capacity).
- **Multi-Target Latency & Packet Loss:** Concurrent ICMP/TCP probes across configurable targets (`1.1.1.1`, `8.8.8.8`, `9.9.9.9`) that prevent a single unresponsive remote server from falsely declaring the Internet connection offline.
- **One-Click Network Diagnosis:** Runs 10 automated checks asynchronously with cancellation support (Network Interface Status, IP Configuration, Default Gateway Reachability, DNS Configuration, DNS Name Resolution, Diagnostic Target Reachability, Network Latency, Packet Loss, Internet Reachability, and optional bounded Route Tracing) and produces an evidence-based root-cause summary.
- **User-Initiated Internet Speed Test:** Measures real HTTPS download and upload throughput (`Mbps`) against configurable endpoints (default: Cloudflare Edge `https://speed.cloudflare.com/__down` and `/__up`). Never runs automatically in the background and explicitly discloses data usage (~20 MB per run) and public IP exposure.

### System & Process Monitoring
- **CPU Monitoring:** Overall CPU utilization (`%`), real-time operating frequency (`MHz` / `GHz`), and non-invasive hardware temperature (`°C`) via OS sysfs sensors (`/sys/class/hwmon`, `/sys/class/thermal` on Linux). Clearly distinguishes `Available`, `TemporarilyInaccessible`, and `Unsupported` sensors without installing kernel drivers or requiring Administrator/root privileges.
- **Physical Memory (RAM):** Total, used, and available physical RAM (`GB` and `%`), strictly separated from swap/pagefile usage.
- **System Uptime:** True elapsed time since operating-system boot (`/proc/uptime` / `Environment.TickCount64`).
- **Storage Volumes:** Mounted disk capacities, used/available space, usage percentages, and Linux block-device read/write throughput, safely handling disconnected or restricted volumes.
- **Process Resource Summary:** Top resource-consuming processes sortable by CPU% (normalized across logical cores) or physical RAM, with configurable entry count (`1–25`).

### Intelligent Notifications, SQLite Persistence & Technical Reports
- **Intelligent Notifications:** Configurable alerts for Internet connection lost/restored, high latency, excessive packet loss, low disk space, and optional CPU/RAM pressure, featuring consecutive-sample hysteresis, anti-spam cooldowns, and automatic recovery alerts.
- **Local SQLite Persistence:** WAL-mode SQLite database (`natee_history.db`) with versioned schema migrations, automatic retention pruning (`1–90` days, default `14` days), and one-click history clearing.
- **PDF & JSON Technical Reports with Privacy Redaction:** Generates human-readable PDF reports (`SkiaSharp`) and machine-readable JSON reports (`System.Text.Json`) for manual IT Support ticket attachment. Privacy-preserving defaults redact hostname, username, local IPs, public IPs, MAC addresses, adapter IDs, Wi-Fi SSIDs, process names, and local paths from both exported files and PDF metadata.

---

## 3. Release Artifacts & Installation Summary

Both release archives are self-contained `.NET 10.0` builds (no separate .NET runtime installation is required by end users):

| Artifact Filename | Target OS & Architecture | Size | SHA-256 Checksum | Installation Summary |
| :--- | :--- | :--- | :--- | :--- |
| `natee-v1.0.0-linux-x64.tar.gz` | Linux x64 (`linux-x64`, priority: Fedora KDE Plasma) | `43.09 MB` | `8cd6206abb03f1cc5e8e0c958a53bcc3879330f3f5f090821b7343748f7ae776` | Extract (`tar -xzf natee-v1.0.0-linux-x64.tar.gz`), ensure `./Natee` is executable, and run `./Natee`. |
| `natee-v1.0.0-win-x64.zip` | Windows 10/11 x64 (`win-x64`) | `47.44 MB` | `3683afaee658aa0c1194f2ce8c0db77dc88f39aed6d034eef53685bf15b6342e` | Extract the ZIP archive and run `Natee.exe`. |

See [INSTALLATION.md](INSTALLATION.md) for complete setup, desktop launcher, autostart, and uninstallation instructions.

---

## 4. Platform Verification Matrix (Tested vs. Untested Platforms)

| Platform / Desktop Environment | Compilation & Packaging | Automated Test Suite | Interactive Runtime & Tray Verification |
| :--- | :--- | :--- | :--- |
| **Fedora Linux 44 (KDE Plasma Wayland session via XWayland, x64)** | **Verified** (`natee-v1.0.0-linux-x64.tar.gz`) | **Verified** (`21/21` tests passed on `.NET 10.0`) | **Verified** (Launched and verified against live KDE Plasma Wayland session via `Avalonia.X11.X11Window` / **XWayland**, D-Bus `StatusNotifierItem` tray, `wlo1` Wi-Fi interface, `coretemp` CPU thermal sensor, Btrfs volumes, and SQLite storage) |
| **Linux GNOME Desktop (x64)** | **Verified** (uses `linux-x64` build) | Covered by Linux test run | **Untested in live GNOME session** (Vanilla GNOME requires `gnome-shell-extension-appindicator` for top-bar tray icons; main dashboard window opens normally via X11/XWayland as fallback) |
| **Windows 10 / 11 (x64)** | **Verified** (Cross-compiled `natee-v1.0.0-win-x64.zip`) | Core & provider unit tests passed | **Unverified interactively** (Cross-compiled on Linux; requires execution on a Windows x64 host to verify interactive Windows notification-area behavior) |

---

## 5. Known Limitations & Performance Notes

1. **Linux Windowing Backend (XWayland vs. Native Wayland):** On Linux Wayland sessions, `Avalonia.Desktop` runs its windowing layer via `Avalonia.X11` over **XWayland** (`Avalonia.X11.X11Window`), while system tray integration uses the native freedesktop.org D-Bus `StatusNotifierItem` protocol. Native Wayland windowing protocol execution is not enabled in v1.0.0.
2. **Memory Footprint Breakdown & Target Deviation:**
   - **Original Target vs. Measured RSS:** Natee was originally targeted at approximately `100 MB` of total process memory. Actual verified total resident memory (`VmRSS`) on Fedora Linux 44 KDE Plasma (`x64`) **exceeds** that target at **`~175.85 MB` during idle monitoring** (`+75.85 MB` above target) and **`~193.60 MB` after a complete diagnostic run and garbage collection** (`+93.60 MB` above target; `~195.57 MB` prior to compacting GC).
   - **Managed Heap vs. Total Process Memory (Overlapping Metrics — Do Not Sum):** Within that `175.85–193.60 MB` total `VmRSS`, Natee's own C# objects on the **`.NET` managed heap** consume only **`~4.68 MB` idle** and **`~4.80 MB` post-diagnosis** (`~12.80–14.43 MB` committed `.NET` GC memory). **Anonymous resident memory (`RssAnon`)** — which includes the `.NET` GC heap, CLR/JIT structures, thread stacks, and native Skia/X11 heap allocations — is **`~50.51 MB` idle** and **`~59.97 MB` post-diagnosis**. **Private mapped pages (`Private_Dirty + Private_Clean`)** are **`~124.18 MB` idle / `~136.36 MB` post-diagnosis** (comprising `RssAnon` plus private file-backed pages from bundled self-contained `.NET 10` DLLs and `libSkiaSharp.so`), **Proportional Set Size (`PSS`)** is **`~131.97 MB` idle / `~145.17 MB` post-diagnosis**, and total **file-backed mappings (`RssFile + RssShmem`)** account for **`~125.35 MB` idle / `~133.63 MB` post-diagnosis** (`RssAnon + RssFile + RssShmem = Total VmRSS`).
3. **Windows Interactive Execution:** Because the release artifacts were built and verified on a Fedora Linux 44 KDE Plasma workstation, the `win-x64` package is cross-compiled and has not yet been interactively launched on a physical Windows machine.
4. **Unprivileged Windows CPU Temperature & Dynamic Clock Frequency:** Windows does not expose CPU package temperatures or real-time per-core clock scaling to standard unprivileged user-space processes without ring-0 kernel drivers. By design, Natee avoids installing kernel drivers or requiring Administrator privileges and reports `Unsupported` for those sensors on Windows.
5. **GNOME System Tray Extension Requirement:** GNOME desktops without an AppIndicator/StatusNotifier extension installed do not render third-party tray icons; Natee opens its main dashboard window on startup so all features remain accessible.
6. **Manual Ticket Attachment in v1.0.0:** Reports are exported locally as `.pdf` and `.json` files for manual upload to IT Support tickets. Direct API integrations (such as Zammad or Jira Service Management) are scoped for Version 2 via the `ITicketIntegrationBoundary` interface.

## Additional known issue — Internet speed test

The Internet speed test currently returns HTTP 403 Forbidden in the tested environment. This issue will be investigated in a future release.
