namespace Trnscrbr.Models;

public sealed record RecordedAudio(
    string FilePath,
    TimeSpan Duration,
    int SampleRate,
    int Channels,
    long FileSizeBytes,
    string MicrophoneName);
