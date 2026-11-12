using ChudType.Audio;
using SherpaOnnx;

namespace ChudType.Speech.Sherpa;

public sealed record SherpaStreamingModel(string Encoder, string Decoder, string Joiner, string Tokens)
{
    public void Validate()
    {
        foreach (string path in new[] { Encoder, Decoder, Joiner, Tokens })
            if (!File.Exists(path)) throw new FileNotFoundException("A streaming speech model file is missing.", path);
    }
}

/// <summary>
/// Cross-platform, offline, streaming recognition backed by sherpa-onnx.
/// A session returns a revised partial transcript after every audio chunk.
/// </summary>
public sealed class SherpaRealtimeSpeechEngine : IRealtimeSpeechEngine, IDisposable
{
    private readonly OnlineRecognizer recognizer;
    private bool disposed;

    public SherpaRealtimeSpeechEngine(SherpaStreamingModel model, int? threads = null)
    {
        model.Validate();
        var config = new OnlineRecognizerConfig();
        config.FeatConfig.SampleRate = VoiceActivity.SampleRate;
        config.FeatConfig.FeatureDim = 80;
        config.ModelConfig.Transducer.Encoder = model.Encoder;
        config.ModelConfig.Transducer.Decoder = model.Decoder;
        config.ModelConfig.Transducer.Joiner = model.Joiner;
        config.ModelConfig.Tokens = model.Tokens;
        config.ModelConfig.Provider = "cpu";
        config.ModelConfig.NumThreads = threads ?? Math.Clamp(Environment.ProcessorCount / 2, 2, 8);
        config.DecodingMethod = "greedy_search";
        config.EnableEndpoint = 1;
        config.Rule1MinTrailingSilence = 1.8f;
        config.Rule2MinTrailingSilence = 0.8f;
        config.Rule3MinUtteranceLength = 300;
        recognizer = new OnlineRecognizer(config);
    }

    public string Id => "sherpa-online-transducer";
    public string DisplayName => "Instant local transcription";

    public ValueTask<IRealtimeSpeechSession> StartAsync(CancellationToken cancellation = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        cancellation.ThrowIfCancellationRequested();
        return ValueTask.FromResult<IRealtimeSpeechSession>(new Session(recognizer));
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        recognizer.Dispose();
    }

    private sealed class Session(OnlineRecognizer recognizer) : IRealtimeSpeechSession
    {
        private readonly OnlineStream stream = recognizer.CreateStream();
        private readonly SemaphoreSlim gate = new(1, 1);
        private long sampleCount;
        private bool complete;

        public async ValueTask<RealtimeTranscript> PushAsync(ReadOnlyMemory<short> samples,
            CancellationToken cancellation = default)
        {
            if (complete) throw new InvalidOperationException("The recognition session is complete.");
            if (samples.IsEmpty) return Current(isFinal: false);
            await gate.WaitAsync(cancellation);
            try
            {
                cancellation.ThrowIfCancellationRequested();
                float[] audio = new float[samples.Length];
                var input = samples.Span;
                for (int i = 0; i < input.Length; i++) audio[i] = input[i] / 32768f;
                stream.AcceptWaveform(VoiceActivity.SampleRate, audio);
                sampleCount += samples.Length;
                DecodeReady(cancellation);
                return Current(isFinal: false);
            }
            finally { gate.Release(); }
        }

        public async ValueTask<RealtimeTranscript> CompleteAsync(CancellationToken cancellation = default)
        {
            await gate.WaitAsync(cancellation);
            try
            {
                if (!complete)
                {
                    complete = true;
                    stream.InputFinished();
                    DecodeReady(cancellation);
                }
                return Current(isFinal: true);
            }
            finally { gate.Release(); }
        }

        private void DecodeReady(CancellationToken cancellation)
        {
            while (recognizer.IsReady(stream))
            {
                cancellation.ThrowIfCancellationRequested();
                recognizer.Decode(stream);
            }
        }

        private RealtimeTranscript Current(bool isFinal) => new(recognizer.GetResult(stream).Text.Trim(), isFinal,
            TimeSpan.FromSeconds(sampleCount / (double)VoiceActivity.SampleRate));

        public ValueTask DisposeAsync()
        {
            stream.Dispose();
            gate.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
