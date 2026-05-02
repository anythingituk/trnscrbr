namespace Trnscrbr.Models;

public sealed record LocalModelPreset(
    string Id,
    string DisplayName,
    string FileName,
    string DownloadUrl,
    string Sha1,
    string DiskSize,
    string RamRecommendation,
    string Description);
