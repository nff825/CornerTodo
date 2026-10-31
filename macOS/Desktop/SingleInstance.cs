#nullable enable
using System;
using System.IO;
using System.Text;

namespace CornerTodo.MacDesktop;

/// <summary>A same-user file lock plus a data-only restore request; no named Windows events.</summary>
internal sealed class SingleInstance : IDisposable
{
    private readonly FileStream? instanceLock;
    private readonly string requestPath;
    private readonly Func<string, string?> readRequest;
    private string? handledRequest;
    private bool disposed;
    internal bool IsPrimary { get; }

    internal SingleInstance(string directory, bool preview)
        : this(directory, preview, OpenLock, ReadRequest) { }

    // Inject only the file boundary for deterministic I/O failure checks.
    internal SingleInstance(string directory, bool preview, Func<string, FileStream> openLock,
        Func<string, string?> readRequest)
    {
        if (String.IsNullOrWhiteSpace(directory)) throw new ArgumentException("A data directory is required.", nameof(directory));
        this.readRequest = readRequest ?? throw new ArgumentNullException(nameof(readRequest));
        if (openLock == null) throw new ArgumentNullException(nameof(openLock));
        requestPath = Path.Combine(directory, "restore.request");
        if (preview) { IsPrimary = true; return; }
        Directory.CreateDirectory(directory);

        FileStream acquired;
        try { acquired = openLock(Path.Combine(directory, "instance.lock")); }
        catch (IOException error) when (IsLockContention(error, OperatingSystem.IsWindows(), OperatingSystem.IsMacOS()))
        {
            // Only a proven sharing/lock conflict means another instance owns the
            // store. Disk, path and permission failures must remain visible errors.
            SendRestoreRequest(directory, requestPath);
            return;
        }

        try
        {
            // A leftover token from a previous run is a baseline, not a new
            // restore command. Read failures are not mistaken for duplicates.
            handledRequest = this.readRequest(requestPath);
            instanceLock = acquired;
            IsPrimary = true;
        }
        catch
        {
            acquired.Dispose();
            throw;
        }
    }

    internal bool TryTakeRestoreRequest(bool busy)
    {
        if (disposed || !IsPrimary || instanceLock == null || busy) return false;
        try
        {
            string? request = readRequest(requestPath);
            if (request == null || request == handledRequest) return false;
            handledRequest = request;
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    internal static bool IsLockContention(IOException error, bool windows, bool macOS)
    {
        // .NET's Windows FileStream uses HRESULT_FROM_WIN32; on Unix its flock
        // EWOULDBLOCK IOException carries the raw errno (Darwin 35, Linux 11).
        if (windows) return (error.HResult & 0xffff) == 32 || (error.HResult & 0xffff) == 33;
        return error.HResult == (macOS ? 35 : 11);
    }

    private static FileStream OpenLock(string path)
        => new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

    private static void SendRestoreRequest(string directory, string destination)
    {
        // The payload is exactly one random GUID, never executable code, a path,
        // user task content or command-line arguments.
        string temporary = Path.Combine(directory, ".restore-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            byte[] token = Encoding.ASCII.GetBytes(Guid.NewGuid().ToString("N"));
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                output.Write(token, 0, token.Length);
                output.Flush(true);
            }
            File.Move(temporary, destination, true);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static string? ReadRequest(string path)
    {
        FileStream input;
        try { input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        using (input)
        {
            if (input.Length != 32) return null;
            byte[] bytes = new byte[33];
            int count = 0;
            while (count < bytes.Length)
            {
                int read = input.Read(bytes, count, bytes.Length - count);
                if (read == 0) break;
                count += read;
            }
            if (count != 32) return null;
            foreach (byte value in bytes.AsSpan(0, count))
                if (!(value >= '0' && value <= '9') && !(value >= 'a' && value <= 'f') && !(value >= 'A' && value <= 'F')) return null;
            return Encoding.ASCII.GetString(bytes, 0, count).ToLowerInvariant();
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        instanceLock?.Dispose();
    }
}
