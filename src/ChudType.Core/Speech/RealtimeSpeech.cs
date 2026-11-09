namespace ChudType.Speech;

/// <summary>Incremental transcript emitted while the user is still speaking.</summary>
public sealed record RealtimeTranscript(string Text, bool IsFinal, TimeSpan AudioProcessed);

/// <summary>A local engine capable of accepting microphone audio as it arrives.</summary>
public interface IRealtimeSpeechEngine
{
    string Id { get; }
    string DisplayName { get; }
    ValueTask<IRealtimeSpeechSession> StartAsync(CancellationToken cancellation = default);
}

public interface IRealtimeSpeechSession : IAsyncDisposable
{
    ValueTask<RealtimeTranscript> PushAsync(ReadOnlyMemory<short> samples, CancellationToken cancellation = default);
    ValueTask<RealtimeTranscript> CompleteAsync(CancellationToken cancellation = default);
}
