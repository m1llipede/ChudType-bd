using System.Diagnostics;
using ChudType.Speech.Sherpa;
using Xunit;
using Xunit.Abstractions;

namespace ChudType.Speech.Sherpa.Tests;

public sealed class StreamingRecognitionTests(ITestOutputHelper output)
{
    [Fact]
    public async Task CompactEnglishModelRecognizesOfficialFixtureInRealtime()
    {
        string? root = Environment.GetEnvironmentVariable("CHUDTYPE_SHERPA_TEST_MODEL");
        if (string.IsNullOrWhiteSpace(root)) return;

        var model = new SherpaStreamingModel(
            Path.Combine(root, "encoder-epoch-99-avg-1.int8.onnx"),
            Path.Combine(root, "decoder-epoch-99-avg-1.int8.onnx"),
            Path.Combine(root, "joiner-epoch-99-avg-1.int8.onnx"),
            Path.Combine(root, "tokens.txt"));
        short[] audio = ReadPcm16Mono(Path.Combine(root, "test_wavs", "0.wav"), out int sampleRate);
        Assert.Equal(16000, sampleRate);

        var timer = Stopwatch.StartNew();
        using var engine = new SherpaRealtimeSpeechEngine(model, threads: 2);
        TimeSpan loadTime = timer.Elapsed;
        await using var session = await engine.StartAsync();
        string partial = "";
        const int chunk = 1600;
        for (int offset = 0; offset < audio.Length; offset += chunk)
        {
            var next = await session.PushAsync(audio.AsMemory(offset, Math.Min(chunk, audio.Length - offset)));
            if (next.Text.Length > 0) partial = next.Text;
        }
        var final = await session.CompleteAsync();
        timer.Stop();

        output.WriteLine($"Model load: {loadTime.TotalMilliseconds:F0} ms");
        output.WriteLine($"Recognition: {(timer.Elapsed - loadTime).TotalMilliseconds:F0} ms for {audio.Length / 16000d:F2}s audio");
        output.WriteLine(final.Text);
        Assert.NotEmpty(partial);
        Assert.Contains("yellow lamps", final.Text, StringComparison.OrdinalIgnoreCase);
        Assert.True(timer.Elapsed.TotalSeconds < audio.Length / 16000d,
            $"Recognition took {timer.Elapsed.TotalSeconds:F2}s for {audio.Length / 16000d:F2}s of audio.");
    }

    private static short[] ReadPcm16Mono(string path, out int sampleRate)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        if (new string(reader.ReadChars(4)) != "RIFF") throw new InvalidDataException("Expected a RIFF wave file.");
        _ = reader.ReadInt32();
        if (new string(reader.ReadChars(4)) != "WAVE") throw new InvalidDataException("Expected a WAVE file.");
        sampleRate = 0;
        short channels = 0, bits = 0;
        byte[]? data = null;
        while (stream.Position + 8 <= stream.Length)
        {
            string id = new(reader.ReadChars(4));
            int length = reader.ReadInt32();
            long next = stream.Position + length + (length & 1);
            if (id == "fmt ")
            {
                if (reader.ReadInt16() != 1) throw new InvalidDataException("Expected PCM audio.");
                channels = reader.ReadInt16();
                sampleRate = reader.ReadInt32();
                stream.Position += 6;
                bits = reader.ReadInt16();
            }
            else if (id == "data") data = reader.ReadBytes(length);
            stream.Position = next;
        }
        if (channels != 1 || bits != 16 || sampleRate <= 0 || data is null)
            throw new InvalidDataException("Expected mono 16-bit PCM audio.");
        var samples = new short[data.Length / 2];
        Buffer.BlockCopy(data, 0, samples, 0, data.Length);
        return samples;
    }
}
