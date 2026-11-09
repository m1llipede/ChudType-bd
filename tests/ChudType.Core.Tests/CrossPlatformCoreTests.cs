using ChudType.Audio;
using ChudType.Core;
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
}
