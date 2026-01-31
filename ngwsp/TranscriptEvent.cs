namespace ngwsp;

public sealed record TranscriptEvent(
    string Track,
    IReadOnlyList<TranscriptToken> Tokens,
    double FinalAudioProcMs,
    double TotalAudioProcMs);

public sealed record TranscriptToken(string Text, double StartMs, double EndMs, bool IsFinal, bool NonSpeech);
