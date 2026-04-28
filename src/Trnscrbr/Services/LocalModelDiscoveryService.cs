using System.IO;
using Trnscrbr.Models;

namespace Trnscrbr.Services;

public sealed class LocalModelDiscoveryService
{
    private static readonly string[] ModelNameHints =
    [
        "whisper",
        "faster-whisper",
        "ggml",
        "gguf",
        "ctranslate2"
    ];

    public IReadOnlyList<LocalModelCandidate> Discover()
    {
        var candidates = new List<LocalModelCandidate>();

        foreach (var root in GetSearchRoots().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            ScanRoot(root, candidates);
        }

        return candidates
            .GroupBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IEnumerable<string> GetSearchRoots()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        yield return Path.Combine(appData, "Trnscrbr", "Models");
        yield return Path.Combine(localAppData, "Trnscrbr", "Models");
        yield return Path.Combine(userProfile, ".cache", "huggingface", "hub");
        yield return Path.Combine(userProfile, ".cache", "whisper");
    }

    private static void ScanRoot(string root, List<LocalModelCandidate> candidates)
    {
        AddIfModelCandidate(root, root, candidates);

        foreach (var path in EnumerateFileSystemEntries(root, maxDepth: 3))
        {
            AddIfModelCandidate(root, path, candidates);
        }
    }

    private static IEnumerable<string> EnumerateFileSystemEntries(string root, int maxDepth)
    {
        var pending = new Queue<(string Path, int Depth)>();
        pending.Enqueue((root, 0));

        while (pending.Count > 0)
        {
            var (path, depth) = pending.Dequeue();

            IEnumerable<string> entries;
            try
            {
                entries = Directory.EnumerateFileSystemEntries(path);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                continue;
            }

            foreach (var entry in entries)
            {
                yield return entry;

                if (depth >= maxDepth || !Directory.Exists(entry))
                {
                    continue;
                }

                pending.Enqueue((entry, depth + 1));
            }
        }
    }

    private static void AddIfModelCandidate(string root, string path, List<LocalModelCandidate> candidates)
    {
        var name = System.IO.Path.GetFileName(path.TrimEnd(
            System.IO.Path.DirectorySeparatorChar,
            System.IO.Path.AltDirectorySeparatorChar));

        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        if (!ModelNameHints.Any(hint => name.Contains(hint, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        candidates.Add(new LocalModelCandidate(
            name,
            path,
            root));
    }
}
