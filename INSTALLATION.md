# Natee — Installation and Usage Guide (`INSTALLATION.md`)

This guide provides step-by-step instructions for installing, running, configuring, troubleshooting, and uninstalling **Natee** on **Linux (x64)** and **Windows (x64)**, both from prebuilt release archives and from source.

---

## 1. Linux Installation (Priority Target: Fedora KDE Plasma x64)

### 1.1 Prerequisites
- **Operating System:** 64-bit Linux distribution (`x86_64` / `amd64`), prioritized and verified on **Fedora Linux (KDE Plasma Desktop Edition)**.
- **Runtime:** **None required** when using the self-contained release archive (`natee-v1.0.0-linux-x64.tar.gz`), which bundles the .NET 10 LTS runtime.
- **Desktop Dependencies:**
  - Standard X11 or Wayland desktop session (`libX11`, `libICE`, `libSM`, `fontconfig`, `freetype`, installed by default on Fedora KDE Plasma and standard Linux desktops).
  - Optional: `libnotify` (`notify-send`, pre-installed on Fedora KDE Plasma) for native desktop alert popups.
  - **GNOME Note:** On **Fedora KDE Plasma**, system tray icons (`StatusNotifierItem`) work out of the box. On **GNOME**, install the AppIndicator extension (`sudo dnf install gnome-shell-extension-appindicator` and enable it in GNOME Extensions) if you want Natee to appear in the top bar; otherwise, Natee remains fully usable through its main dashboard window.

### 1.2 Installing from the Release Archive (`tar.gz`)
1. Download `natee-v1.0.0-linux-x64.tar.gz` from the GitHub Releases page (or `artifacts/releases/`).
2. Extract the archive into an application directory in your home folder:
   ```bash
   mkdir -p ~/.local/opt/natee
   tar -xzf natee-v1.0.0-linux-x64.tar.gz -C ~/.local/opt/natee
   chmod +x ~/.local/opt/natee/Natee
   ```
3. Launch Natee:
   ```bash
   ~/.local/opt/natee/Natee
   ```
4. *(Optional)* Create a desktop application launcher entry so Natee appears in your KDE Plasma Application Launcher:
   ```bash
   mkdir -p ~/.local/share/applications
   cat > ~/.local/share/applications/natee.desktop <<EOF
   [Desktop Entry]
   Type=Application
   Name=Natee
   Comment=Calm monitoring. Clear diagnostics.
   Exec=$HOME/.local/opt/natee/Natee
   Icon=$HOME/.local/opt/natee/Assets/natee-icon-256.png
   Terminal=false
   Categories=System;Monitor;Network;
   EOF
   ```

### 1.3 Linux Uninstallation
1. Exit Natee from the system tray menu (**Exit**) or the top-right **Exit** button.
2. Remove the application directory, optional desktop launcher, and local data directory:
   ```bash
   rm -rf ~/.local/opt/natee
   rm -f ~/.local/share/applications/natee.desktop
   rm -f ~/.config/autostart/natee.desktop
   rm -rf ~/.local/share/Natee
   ```

---

## 2. Windows Installation (Windows 10 / 11 x64)

### 2.1 Prerequisites
- **Operating System:** Windows 10 (version 1809+) or Windows 11 (64-bit `x64`).
- **Runtime:** **None required** when using the self-contained release archive (`natee-v1.0.0-win-x64.zip`).
- **Privileges:** Standard user account (Administrator privileges are **not** required).

### 2.2 Installing from the Release Archive (`zip`)
1. Download `natee-v1.0.0-win-x64.zip` from the GitHub Releases page (or `artifacts/releases/`).
2. Right-click `natee-v1.0.0-win-x64.zip` and choose **Extract All...** (for example, to `C:\Users\<YourUsername>\AppData\Local\Programs\Natee\`).
3. Double-click **`Natee.exe`** to launch the application.
   - *Note:* Because MVP portfolio builds are not signed with a commercial Authenticode EV certificate, Windows SmartScreen may display an *"Unrecognized app"* prompt on first launch. Click **More info** -> **Run anyway** after verifying the download source.
   - *Platform Verification Note:* The `win-x64` package is cross-compiled from the Linux build environment and unit-tested; interactive execution on a Windows 10/11 desktop remains unverified until tested on a Windows host.

### 2.3 Windows Uninstallation
1. Right-click the Natee manatee icon in the Windows notification area (system tray) and click **Exit**.
2. Delete the folder where you extracted `Natee.exe`.
3. To remove all saved settings, SQLite history, and generated reports, delete:
   - `%LOCALAPPDATA%\Natee`
   - `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\Natee-Autostart.cmd` *(if session autostart was enabled)*

---

## 3. First-Launch Behavior & Tray Integration

- **Main Dashboard & System Tray:** On normal launch (`./Natee` or `Natee.exe`), Natee opens its dashboard window and places the **Natee manatee icon** in your system tray.
- **Background Monitoring on Close:** Closing the dashboard window (`X`) hides the window to the system tray while background monitoring continues quietly. To terminate the process completely, click **Exit** in the tray context menu or the **Exit** button in the dashboard header.
- **Starting Minimized / Session Startup:**
  - Launch with `--minimized` (`./Natee --minimized`) to start directly in the system tray without opening the dashboard window.
  - Enable **Start Natee automatically when signing into the desktop session** in the **Settings** tab (disabled by default) to register user-level autostart.

---

## 4. Building and Running from Source

### 4.1 Prerequisites for Building from Source
- **.NET SDK 10.0 LTS** (verified with `.NET SDK 10.0.401`; `global.json` specifies `10.0.100` with `latestFeature` roll-forward to keep SDK selection strictly within the `.NET 10.0` release line)
- **Git**
- **IDE (Optional):** JetBrains Rider (recommended) or Visual Studio 2022+ / VS Code

### 4.2 Build, Test, Run, and Package Commands
```bash
# 1. Clone the repository
git clone https://github.com/<YOUR_GITHUB_USERNAME>/Natee.git
cd Natee

# 2. Restore NuGet packages
dotnet restore Natee.sln

# 3. Build the solution in Release mode
dotnet build Natee.sln --configuration Release --no-restore

# 4. Run the automated test suite
dotnet test Natee.sln --configuration Release --no-build --verbosity normal

# 5. Launch Natee directly from source
dotnet run --project src/Natee.App/Natee.App.csproj --configuration Release

# 6. Produce self-contained Linux x64 and Windows x64 release archives
bash scripts/package-releases.sh 1.0.0
```

---

## 5. Troubleshooting

- **"Natee is already running in this user session. Exiting duplicate instance."**
  - Natee enforces a single-instance lock (`~/.local/share/Natee/natee.instance.lock` or `%LOCALAPPDATA%\Natee\natee.instance.lock`) to prevent duplicate background monitors. Click the existing Natee manatee icon in your system tray to open the dashboard.
- **CPU Temperature shows `Unavailable (Unsupported)`:**
  - On Linux virtual machines or on Windows without vendor thermal zones, hardware temperature sensors are not exposed to unprivileged user-space processes. Natee intentionally avoids installing invasive kernel drivers and clearly marks the sensor as `Unsupported`.
- **Speed Test fails in restricted corporate networks:**
  - If your firewall blocks `speed.cloudflare.com`, open **Settings** -> **Diagnostic Targets & Speed Test Endpoints** and configure an internal HTTP/HTTPS test URL.
