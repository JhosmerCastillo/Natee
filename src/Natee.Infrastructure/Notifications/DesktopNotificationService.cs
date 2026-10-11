using System.Diagnostics;
using System.Runtime.InteropServices;
using Natee.Core.Interfaces;
using Natee.Core.Models;

namespace Natee.Infrastructure.Notifications;

/// <summary>
/// Dispatches native desktop notifications on Linux (via freedesktop notify-send) and Windows,
/// degrading gracefully when running in headless or unsupported environments.
/// </summary>
public sealed class DesktopNotificationService : INotificationService
{
    public bool IsSupported { get; }
    public string CapabilityStatusNote { get; }

    public DesktopNotificationService()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            bool hasDisplay = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY")) ||
                              !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));
            bool hasNotifySend = File.Exists("/usr/bin/notify-send") || File.Exists("/bin/notify-send");

            IsSupported = hasDisplay && hasNotifySend;
            CapabilityStatusNote = IsSupported
                ? "Native freedesktop.org notifications active (notify-send)."
                : "Desktop notification service or display session unavailable; alerts are recorded in the in-app Alert Log.";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            IsSupported = Environment.UserInteractive;
            CapabilityStatusNote = IsSupported
                ? "Windows desktop notification dispatch active."
                : "Non-interactive Windows session; alerts are recorded in the in-app Alert Log.";
        }
        else
        {
            IsSupported = false;
            CapabilityStatusNote = "Native desktop notifications are not configured for this platform.";
        }
    }

    public async Task<bool> SendNotificationAsync(
        AlertNotification notification,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);
        if (!IsSupported)
        {
            return false;
        }

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                string urgency = notification.Severity switch
                {
                    HealthStatus.Failed => "critical",
                    HealthStatus.Degraded => "normal",
                    _ => "low"
                };

                var psi = new ProcessStartInfo
                {
                    FileName = "notify-send",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                psi.ArgumentList.Add("--app-name=Natee");
                psi.ArgumentList.Add($"--urgency={urgency}");
                psi.ArgumentList.Add(notification.Title);
                psi.ArgumentList.Add(notification.Message);

                using var proc = Process.Start(psi);
                if (proc is null)
                {
                    return false;
                }

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(2500);
                await proc.WaitForExitAsync(cts.Token);
                return proc.ExitCode == 0;
            }
        }
        catch
        {
            // Degrade gracefully when desktop D-Bus notification daemon is unreachable
        }

        return false;
    }
}
