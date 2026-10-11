using Avalonia;
using Natee.Infrastructure.Platform;

namespace Natee.App;

public static class Program
{
    private static SingleInstanceGuard? _instanceGuard;

    [STAThread]
    public static int Main(string[] args)
    {
        _instanceGuard = new SingleInstanceGuard();
        if (!_instanceGuard.TryAcquire())
        {
            Console.Error.WriteLine("Natee is already running in this user session. Exiting duplicate instance.");
            return 1;
        }

        try
        {
            return BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Natee encountered a fatal startup error: {ex.Message}");
            return 2;
        }
        finally
        {
            ReleaseInstanceGuard();
        }
    }

    public static void ReleaseInstanceGuard()
    {
        try
        {
            _instanceGuard?.Dispose();
            _instanceGuard = null;
        }
        catch
        {
            // Ignore cleanup exceptions
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
