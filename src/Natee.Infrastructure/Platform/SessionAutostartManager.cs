using System.Runtime.InteropServices;

namespace Natee.Infrastructure.Platform;

/// <summary>
/// Manages user-level session autostart registration (disabled by default) without requiring administrator privileges.
/// Uses XDG ~/.config/autostart/natee.desktop on Linux and the user Startup folder on Windows.
/// </summary>
public static class SessionAutostartManager
{
    public static bool TryConfigureAutostart(bool enable, out string statusMessage)
    {
        try
        {
            string? processPath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(processPath))
            {
                statusMessage = "Could not determine application executable path for session startup.";
                return false;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                string configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")
                    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
                string autostartDir = Path.Combine(configHome, "autostart");
                string desktopFile = Path.Combine(autostartDir, "natee.desktop");

                if (!enable)
                {
                    if (File.Exists(desktopFile))
                    {
                        File.Delete(desktopFile);
                    }

                    statusMessage = "Session autostart disabled (removed XDG autostart entry).";
                    return true;
                }

                Directory.CreateDirectory(autostartDir);
                string content = $"""
                    [Desktop Entry]
                    Type=Application
                    Name=Natee
                    Comment=Calm monitoring. Clear diagnostics.
                    Exec="{processPath}" --minimized
                    Terminal=false
                    Categories=System;Monitor;Network;
                    X-GNOME-Autostart-enabled=true
                    """;
                File.WriteAllText(desktopFile, content);
                statusMessage = $"Session autostart enabled at '{desktopFile}'.";
                return true;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                string startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                if (string.IsNullOrWhiteSpace(startupFolder))
                {
                    statusMessage = "Windows user Startup folder could not be resolved.";
                    return false;
                }

                string launcherCmd = Path.Combine(startupFolder, "Natee-Autostart.cmd");
                if (!enable)
                {
                    if (File.Exists(launcherCmd))
                    {
                        File.Delete(launcherCmd);
                    }

                    statusMessage = "Windows session autostart disabled.";
                    return true;
                }

                Directory.CreateDirectory(startupFolder);
                string cmdContent = $"@echo off\r\nstart \"\" \"{processPath}\" --minimized\r\n";
                File.WriteAllText(launcherCmd, cmdContent);
                statusMessage = $"Windows session autostart enabled at '{launcherCmd}'.";
                return true;
            }

            statusMessage = "Session autostart is not supported on this operating system.";
            return false;
        }
        catch (Exception ex)
        {
            statusMessage = $"Failed to update session autostart: {ex.Message}";
            return false;
        }
    }
}
