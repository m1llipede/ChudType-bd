using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ChudType.Mac;

/// <summary>Streams 16 kHz mono PCM from the bundled native AVFoundation capture helper.</summary>
internal sealed class MacMicrophone : IAsyncDisposable
{
    private Process? process;
    private FileStream? audio;
    private string? pipePath;

    public async IAsyncEnumerable<ReadOnlyMemory<short>> CaptureAsync(
        [EnumeratorCancellation] CancellationToken cancellation)
    {
        string helper = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Helpers", "ChudType.AudioCapture.app"));
        if (!Directory.Exists(helper)) throw new FileNotFoundException("The native Mac microphone helper is missing.", helper);
        pipePath = Path.Combine(Path.GetTempPath(), "chudtype-audio-" + Guid.NewGuid().ToString("N") + ".pipe");
        if (mkfifo(pipePath, Convert.ToUInt32("600", 8)) != 0)
            throw new InvalidOperationException("ChudType could not create its private microphone stream.");
        var start = new ProcessStartInfo
        {
            FileName = "/usr/bin/open",
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (string argument in new[] { "-W", "-n", helper, "--args", pipePath }) start.ArgumentList.Add(argument);
        process = Process.Start(start) ?? throw new InvalidOperationException("The Mac microphone helper could not start.");
        audio = await Task.Run(() => new FileStream(pipePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            3_200, FileOptions.Asynchronous), cancellation).ConfigureAwait(false);

        var bytes = new byte[3_200];
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                int read;
                try { read = await audio.ReadAsync(bytes, cancellation).ConfigureAwait(false); }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { break; }
                if (read == 0)
                {
                    await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                    throw new InvalidOperationException("macOS stopped microphone capture. Check ChudType Audio Capture in Microphone privacy settings.");
                }
                read &= ~1;
                if (read == 0) continue;
                yield return MemoryMarshal.Cast<byte, short>(bytes.AsSpan(0, read)).ToArray();
            }
        }
        finally { Stop(); }
    }

    private void Stop()
    {
        var active = Interlocked.Exchange(ref process, null);
        audio?.Dispose();
        audio = null;
        if (active is not null)
        {
            try { if (!active.HasExited) active.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            active.Dispose();
        }
        foreach (var helper in Process.GetProcessesByName("ChudType.AudioCapture"))
        {
            try { helper.Kill(); }
            catch (InvalidOperationException) { }
            finally { helper.Dispose(); }
        }
        if (pipePath is { } pipe)
        {
            try { File.Delete(pipe); }
            catch (IOException) { }
            pipePath = null;
        }
    }

    public ValueTask DisposeAsync()
    {
        Stop();
        return ValueTask.CompletedTask;
    }

    [DllImport("/usr/lib/libSystem.B.dylib", SetLastError = true)]
    private static extern int mkfifo(string path, uint mode);
}
