# Natee — Security and Privacy Policy (`SECURITY.md`)

Security and privacy are foundational architectural requirements of **Natee**. The application is designed to operate as a trustworthy, non-invasive local diagnostic utility suitable for both home users and corporate IT environments.

---

## 1. Core Security & Privacy Principles

- **Zero Telemetry or Analytics:** Natee includes no telemetry SDKs, analytics trackers, crash-reporting uploads, or background phone-home mechanisms.
- **No Mandatory Cloud Account:** All monitoring, diagnostics, SQLite history storage, and PDF/JSON report exports operate locally on the user's machine.
- **No Undisclosed Network Traffic:** Network monitoring is strictly read-only except for periodic lightweight connectivity probes to user-configurable targets and explicit user-initiated diagnostics or speed tests.
- **No Arbitrary Remote Command Execution:** Natee does not listen on network ports, accept remote commands, or include remote management backdoors.
- **No Elevated Privileges or Kernel Drivers:** Natee runs as a standard unprivileged user process. It never installs kernel-mode hardware drivers (such as WinRing0), modifies firewall rules, alters system DNS settings, or terminates system processes.
- **No Hardcoded Secrets or Credentials:** The repository and compiled binaries contain zero API keys, tokens, or embedded credentials.

---

## 2. External Network Communication & Disclosure

Natee performs external network communication only in the following documented scenarios:

1. **Periodic Connectivity & Latency Probes:**
   - **Default Targets:** `1.1.1.1` (Cloudflare DNS), `8.8.8.8` (Google Public DNS), and `9.9.9.9` (Quad9 DNS).
   - **Mechanism:** Lightweight ICMP Echo (`Ping`) or TCP handshake probes (port `443` / `53` fallback when unprivileged ICMP sockets are restricted) at the user-configured interval (default: every `3` seconds).
   - **Control:** Targets, probe timeout, and polling interval are fully configurable in **Settings**.
2. **One-Click Network Diagnosis (User-Triggered):**
   - Resolves the configured test domain (default: `cloudflare.com`) via system DNS, probes the local default gateway and configured diagnostic targets, and optionally executes a bounded 5-hop route trace if explicitly enabled in Settings.
3. **Internet Speed Test (Explicitly User-Triggered Only):**
   - **Default Provider & Endpoints:** Cloudflare Edge HTTPS (`https://speed.cloudflare.com/__down?bytes=15000000` and `https://speed.cloudflare.com/__up`).
   - **Usage Terms & Operational Considerations:** These endpoints back `speed.cloudflare.com` and Cloudflare's open-source speed test client. While publicly reachable over HTTPS without authentication, Cloudflare provides no formal third-party SLA for standalone desktop clients and may apply rate-limiting or bot-management challenges against automated or high-frequency traffic. To respect upstream infrastructure:
     - Natee **never** executes speed tests automatically, on startup, or on a background timer.
     - Both download and upload URLs are fully configurable in **Settings** (`SpeedTestDownloadUrl`, `SpeedTestUploadUrl`) so corporate IT teams or power users can point Natee to an internal HTTP/HTTPS test server or self-hosted endpoint.
   - **Cancellation, Timeout & Memory Safety (`CloudflareSpeedTestService`):**
     - Uses a linked `CancellationTokenSource` combining explicit user cancellation with a bounded timeout (`SpeedTestTimeoutSeconds`, default `30s`, clamped `5–120s`).
     - Streams both download (`HttpCompletionOption.ResponseHeadersRead`) and upload (`SyntheticUploadStream` via `StreamContent`) payloads in `64 KB` chunks rather than buffering multi-megabyte arrays on the .NET Large Object Heap (LOH), honoring cancellation immediately between chunks and clearly distinguishing user cancellation from timeout expiration.
   - **Data & Privacy Exposure:** Running a manual speed test transfers approximately `15 MB` of download payload and `5 MB` of upload payload over HTTPS and exposes the client's public IP address and `User-Agent: Natee/1.0.0` header to the configured endpoint.

---

## 3. Technical Report Privacy & Redaction

Before generating PDF and JSON technical reports for IT Support tickets, Natee applies `PrivacyRedactionService` using privacy-preserving defaults. Users can individually toggle redaction for:

| Redaction Option | Default | Effect on Exported PDF & JSON Files |
| :--- | :--- | :--- |
| **Computer Hostname** | **Enabled (`true`)** | Replaces machine hostname with `[REDACTED-HOSTNAME]` across structured fields and diagnostic text. |
| **OS Username** | **Enabled (`true`)** | Replaces active username with `[REDACTED-USER]` and excludes username from PDF document metadata. |
| **Local IP & Gateway** | **Enabled (`true`)** | Replaces RFC1918/link-local IPv4, IPv6, default gateway, and private DNS addresses with `[REDACTED-LOCAL-IP]`. |
| **Public IPs & External DNS** | **Enabled (`true`)** | Replaces public IP addresses and external resolver IPs with `[REDACTED-PUBLIC-IP]`. |
| **MAC Addresses** | **Enabled (`true`)** | Scrubs hardware Ethernet/Wi-Fi MAC addresses (`[REDACTED-MAC]`). |
| **Network Interface IDs** | Disabled (`false`) | When enabled, replaces adapter names and OS interface GUIDs with `[REDACTED-INTERFACE]`. |
| **Wi-Fi SSID** | **Enabled (`true`)** | Replaces wireless network names with `[REDACTED-SSID]`. |
| **Process Names** | **Enabled (`true`)** | Replaces running application names with `[REDACTED-PROCESS-N]` and zeroes PIDs while preserving aggregate CPU/RAM figures. |
| **Local File & Mount Paths** | **Enabled (`true`)** | Replaces custom filesystem mount paths and executable paths with `[REDACTED-PATH]`. |

Redactions are applied to the underlying `TechnicalReportDocument` **before** PDF rendering or JSON serialization, ensuring that excluded values are never retained in hidden fields or PDF metadata.

---

## 4. Local Storage, Retention & Data Removal

- **Location:**
  - **Linux:** `~/.local/share/Natee/` (or `$XDG_DATA_HOME/Natee/`)
  - **Windows:** `%LOCALAPPDATA%\Natee\`
- **Files Stored:**
  - `settings.json` — User preferences, notification thresholds, and default privacy toggles.
  - `natee_history.db` — Local SQLite database storing recent diagnostic summaries, manual speed test results, and alert notifications.
  - `Reports/` — Default output directory for user-generated PDF and JSON reports.
- **Retention & Cleanup:**
  - Natee automatically prunes SQLite history older than the configured retention window (default: `14` days, configurable `1–90` days) on startup and settings save.
  - Users can click **Clear All Local History** in the *Alerts & History* tab (`DELETE` + `VACUUM` on SQLite) and **Delete Generated Reports** in the *Reports & Privacy* tab at any time.

---

## 5. Dependency Vulnerability Audit & Known Security Limitations

### 5.1 NuGet Direct & Transitive Dependency Audit
Audited via `dotnet list Natee.sln package --vulnerable --include-transitive` against `https://api.nuget.org/v3/index.json`:
- `Natee.Core`: **0 vulnerable packages** (direct or transitive)
- `Natee.Infrastructure`: **0 vulnerable packages** (direct or transitive)
- `Natee.App`: **0 vulnerable packages** (direct or transitive)
- `Natee.Tests`: **0 vulnerable packages** (direct or transitive)

Additionally, `Avalonia.BuildServices` build-time telemetry (`AvaloniaStatsTask`) is explicitly disabled across all projects via [Directory.Build.targets](Directory.Build.targets).

### 5.2 Known Security Limitations
- Local SQLite history (`natee_history.db`) and `settings.json` are stored as standard user-readable files protected by operating-system user account permissions. Users on shared workstations should use full-disk encryption (LUKS / BitLocker) and separate OS user accounts.
- If a user unchecks privacy redaction options before exporting a report, the resulting PDF and JSON files will contain the unredacted network/system identifiers selected by the user; users should review recipients before attaching reports to external tickets.

---

## 6. Responsible Vulnerability Reporting

If you discover a security or privacy issue in Natee:

1. **Do not** open a public GitHub issue containing exploit details or sensitive data.
2. Use GitHub's **Private Vulnerability Reporting** feature on the repository's *Security* tab, or contact the project maintainer directly via the email listed in the repository commit metadata.
3. Include the Natee version, operating system, reproduction steps, and impact assessment.
