using System.Runtime.CompilerServices;

namespace ChudType.Speech;

/// <summary>
/// Connects any platform microphone source to an incremental local speech engine.
/// UI projects only need to provide 16 kHz mono PCM chunks and render the
/// transcripts yielded here.
/// </summary>
public sealed class StreamingDictation(IRealtimeSpeechEngine engine)
{
    public async IAsyncEnumerable<RealtimeTranscript> RunAsync(
        IAsyncEnumerable<ReadOnlyMemory<short>> audio,
        [EnumeratorCancellation] CancellationToken cancellation = default)
    {
        await using var session = await engine.StartAsync(cancellation);
        string? lastText = null;

        await foreach (var samples in audio.WithCancellation(cancellation).ConfigureAwait(false))
        {
            if (samples.IsEmpty) continue;

            var update = await session.PushAsync(samples, cancellation).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(update.Text)) continue;

            lastText = update.Text;
            yield return update;
        }

        var final = await session.CompleteAsync(cancellation).ConfigureAwait(false);
        if (final.IsFinal && (!string.IsNullOrWhiteSpace(final.Text) || lastText is not null))
        {
            // Always emit the final marker. Consumers use it to replace the last
            // partial and to enable insertion, even when the text is unchanged.
            yield return final;
        }
    }
}
