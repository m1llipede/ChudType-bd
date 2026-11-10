using ChudType.Audio;
using ChudType.Core;
using ChudType.Speech;
using Xunit;

namespace ChudType.Core.Tests;

public sealed class CrossPlatformCoreTests
{
    private static readonly DictationModel LocalModel = new();
    private static readonly TextCleanup Cleaner = new(LocalModel);

    [Theory]
    [InlineData("Could you, um, check the calendar?", "Could you check the calendar?")]
    [InlineData("Uh, I'll look in the other folder.", "I'll look in the other folder.")]
    [InlineData("The clip says \"um wait for me\".", "The clip says \"um wait for me\".")]
    [InlineData("The width is 5 um long.", "The width is 5 um long.")]
    public void BundledModelEditsOnlyCandidateFillers(string input, string expected) =>
        Assert.Equal(expected, LocalModel.Apply(input));

    [Fact]
    public void CleanupSupportsUnicodeVocabularyAndSnippets()
    {
        Vocabulary[] vocabulary = [new("chudgpt", "ChudGPT", "es")];
        Assert.Equal("ChudGPT mañana 日本語", Cleaner.Apply("chudgpt mañana 日本語", "es", new(), vocabulary, []));
        Assert.Equal("Bonjour, André 👋", Cleaner.Apply("Insert snippet Bonjour.", "en", new(), [],
            [new("Bonjour", "Bonjour, André 👋")]));
    }

    [Fact]
    public void VoiceActivityRejectsSilenceAndKeepsSpeech()
    {
        var samples = new short[VoiceActivity.SampleRate * 2];
        Assert.Empty(VoiceActivity.Segment(samples, 0.012));
        Array.Fill(samples, (short)1000, 8000, 8000);
        Assert.Single(VoiceActivity.Segment(samples, 0.012));
    }

    [Fact]
    public async Task StreamingDictationSkipsEmptyAudioAndEmitsFinalMarker()
    {
        var engine = new FakeRealtimeEngine();
        var coordinator = new StreamingDictation(engine);
        var updates = new List<RealtimeTranscript>();

        await foreach (var update in coordinator.RunAsync(Audio())) updates.Add(update);

        Assert.Equal(2, engine.Session.PushCount);
        Assert.Collection(updates,
            partial =>
            {
                Assert.Equal("hello", partial.Text);
                Assert.False(partial.IsFinal);
            },
            final =>
            {
                Assert.Equal("hello world", final.Text);
                Assert.True(final.IsFinal);
            });
        Assert.True(engine.Session.Disposed);
    }

    private static async IAsyncEnumerable<ReadOnlyMemory<short>> Audio()
    {
        yield return ReadOnlyMemory<short>.Empty;
        yield return new short[] { 1, 2 };
        await Task.Yield();
        yield return new short[] { 3, 4 };
    }

    private sealed class FakeRealtimeEngine : IRealtimeSpeechEngine
    {
        public string Id => "fake";
        public string DisplayName => "Fake";
        public FakeRealtimeSession Session { get; } = new();
        public ValueTask<IRealtimeSpeechSession> StartAsync(CancellationToken cancellation = default) =>
            ValueTask.FromResult<IRealtimeSpeechSession>(Session);
    }

    private sealed class FakeRealtimeSession : IRealtimeSpeechSession
    {
        public int PushCount { get; private set; }
        public bool Disposed { get; private set; }

        public ValueTask<RealtimeTranscript> PushAsync(ReadOnlyMemory<short> samples, CancellationToken cancellation = default)
        {
            PushCount++;
            string text = PushCount == 1 ? "" : "hello";
            return ValueTask.FromResult(new RealtimeTranscript(text, false, TimeSpan.FromMilliseconds(PushCount * 20)));
        }

        public ValueTask<RealtimeTranscript> CompleteAsync(CancellationToken cancellation = default) =>
            ValueTask.FromResult(new RealtimeTranscript("hello world", true, TimeSpan.FromMilliseconds(40)));

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
