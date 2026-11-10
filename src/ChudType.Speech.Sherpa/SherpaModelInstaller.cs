using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using SharpCompress.Archives;

namespace ChudType.Speech.Sherpa;

public sealed record SherpaModelPackage(string Id, Uri Download, long Bytes, string Sha256, string DirectoryName)
{
    public static SherpaModelPackage InstantEnglish { get; } = new(
        "zipformer-en-20m",
        new("https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/sherpa-onnx-streaming-zipformer-en-20M-2023-02-17.tar.bz2"),
        127_887_156,
        "9c559283e8498d3fe95913c79ca1cb454bb26281ac2b102b41306c7d752765d9",
        "sherpa-onnx-streaming-zipformer-en-20M-2023-02-17");
}

public sealed class SherpaModelInstaller(HttpClient http, string modelRoot)
{
    public bool IsInstalled(SherpaModelPackage package) => TryOpen(package) is not null;

    public SherpaStreamingModel? TryOpen(SherpaModelPackage package)
    {
        string root = Path.Combine(modelRoot, package.DirectoryName);
        var model = new SherpaStreamingModel(
            Path.Combine(root, "encoder-epoch-99-avg-1.int8.onnx"),
            Path.Combine(root, "decoder-epoch-99-avg-1.int8.onnx"),
            Path.Combine(root, "joiner-epoch-99-avg-1.int8.onnx"),
            Path.Combine(root, "tokens.txt"));
        try { model.Validate(); return model; }
        catch (FileNotFoundException) { return null; }
    }

    public async Task<SherpaStreamingModel> InstallAsync(SherpaModelPackage package, IProgress<double>? progress = null,
        CancellationToken cancellation = default)
    {
        Directory.CreateDirectory(modelRoot);
        string archivePath = Path.Combine(modelRoot, package.Id + ".tar.bz2.download");
        await DownloadVerifiedAsync(package, archivePath, progress, cancellation);

        string stage = Path.Combine(modelRoot, ".install-" + package.Id + "-" + Guid.NewGuid().ToString("N"));
        string destination = Path.Combine(modelRoot, package.DirectoryName);
        try
        {
            Directory.CreateDirectory(stage);
            await Task.Run(() => ExtractSafely(archivePath, stage, cancellation), cancellation);
            string extracted = Path.Combine(stage, package.DirectoryName);
            if (!Directory.Exists(extracted)) throw new InvalidDataException("The speech model archive has an unexpected layout.");
            var staged = new SherpaStreamingModel(
                Path.Combine(extracted, "encoder-epoch-99-avg-1.int8.onnx"),
                Path.Combine(extracted, "decoder-epoch-99-avg-1.int8.onnx"),
                Path.Combine(extracted, "joiner-epoch-99-avg-1.int8.onnx"),
                Path.Combine(extracted, "tokens.txt"));
            staged.Validate();
            if (Directory.Exists(destination)) Directory.Move(destination, destination + ".replaced-" + DateTime.UtcNow.Ticks);
            Directory.Move(extracted, destination);
            File.Delete(archivePath);
            return TryOpen(package) ?? throw new InvalidDataException("The installed speech model could not be opened.");
        }
        finally
        {
            if (Directory.Exists(stage)) Directory.Delete(stage, recursive: true);
        }
    }

    private async Task DownloadVerifiedAsync(SherpaModelPackage package, string path, IProgress<double>? progress,
        CancellationToken cancellation)
    {
        long existing = File.Exists(path) ? new FileInfo(path).Length : 0;
        if (existing > package.Bytes) { File.Delete(path); existing = 0; }

        using var request = new HttpRequestMessage(HttpMethod.Get, package.Download);
        if (existing > 0) request.Headers.Range = new RangeHeaderValue(existing, null);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
        if (existing > 0 && response.StatusCode != HttpStatusCode.PartialContent)
        {
            File.Delete(path);
            existing = 0;
        }
        response.EnsureSuccessStatusCode();

        await using (var source = await response.Content.ReadAsStreamAsync(cancellation))
        await using (var target = new FileStream(path, existing > 0 ? FileMode.Append : FileMode.Create,
            FileAccess.Write, FileShare.None, 128 * 1024, useAsync: true))
        {
            var buffer = new byte[128 * 1024];
            long total = existing;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellation)) > 0)
            {
                total += read;
                if (total > package.Bytes) throw new InvalidDataException("The speech model download exceeded its expected size.");
                await target.WriteAsync(buffer.AsMemory(0, read), cancellation);
                progress?.Report(total / (double)package.Bytes);
            }
        }

        var info = new FileInfo(path);
        if (info.Length != package.Bytes) throw new InvalidDataException("The speech model download is incomplete.");
        await using var verify = File.OpenRead(path);
        string digest = Convert.ToHexString(await SHA256.HashDataAsync(verify, cancellation));
        if (!digest.Equals(package.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The speech model failed SHA-256 verification.");
    }

    private static void ExtractSafely(string archivePath, string stage, CancellationToken cancellation)
    {
        string prefix = Path.GetFullPath(stage) + Path.DirectorySeparatorChar;
        using var archive = ArchiveFactory.OpenArchive(archivePath);
        foreach (var entry in archive.Entries.Where(e => !e.IsDirectory))
        {
            cancellation.ThrowIfCancellationRequested();
            string relative = (entry.Key ?? throw new InvalidDataException("The speech model archive contains an unnamed entry."))
                .Replace('\\', '/');
            if (relative.StartsWith('/') || relative.Split('/').Any(part => part is "" or "." or ".."))
                throw new InvalidDataException("The speech model archive contains an unsafe path.");
            string targetPath = Path.GetFullPath(Path.Combine(stage, relative));
            if (!targetPath.StartsWith(prefix, StringComparison.Ordinal))
                throw new InvalidDataException("The speech model archive contains an unsafe path.");
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            using var input = entry.OpenEntryStream();
            using var output = new FileStream(targetPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            input.CopyTo(output);
        }
    }
}
