namespace Natee.Infrastructure.Platform;

/// <summary>
/// Prevents duplicate Natee monitoring processes from running concurrently for the same user profile.
/// Uses an exclusive OS file lock that is automatically released even if the process terminates unexpectedly.
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private FileStream? _lockStream;
    private readonly string _lockFilePath;

    public bool IsFirstInstance { get; private set; }

    public SingleInstanceGuard(string? customAppDataDirectory = null)
    {
        string dir = AppDataPaths.GetAppDataDirectory(customAppDataDirectory);
        _lockFilePath = Path.Combine(dir, "natee.instance.lock");
    }

    public bool TryAcquire()
    {
        if (IsFirstInstance && _lockStream is not null)
        {
            return true;
        }

        try
        {
            _lockStream = new FileStream(
                _lockFilePath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 64,
                FileOptions.DeleteOnClose);

            using var writer = new StreamWriter(_lockStream, leaveOpen: true);
            writer.WriteLine($"{Environment.ProcessId}:{DateTimeOffset.UtcNow:O}");
            writer.Flush();

            IsFirstInstance = true;
            return true;
        }
        catch (IOException)
        {
            IsFirstInstance = false;
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            IsFirstInstance = false;
            return false;
        }
    }

    public void Dispose()
    {
        IsFirstInstance = false;
        try
        {
            _lockStream?.Dispose();
            _lockStream = null;
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}
