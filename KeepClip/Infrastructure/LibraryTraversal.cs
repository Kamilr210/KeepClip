namespace KeepClip.Infrastructure;

internal static class LibraryTraversal
{
    internal const int MaximumEntries = 100_000;
    internal const int MaximumDepth = 64;

    internal static IEnumerable<string> Files(string root, CancellationToken cancellationToken, int maximumEntries = MaximumEntries)
    {
        var pending = new Stack<(DirectoryInfo Directory, int Depth)>();
        pending.Push((new DirectoryInfo(root), 0));
        int visited = 0;
        var options = new EnumerationOptions { RecurseSubdirectories = false, AttributesToSkip = 0, IgnoreInaccessible = false };
        while (pending.TryPop(out var current))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((current.Directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Folder skanowania jest dowiązaniem. Wybierz rzeczywisty folder biblioteki.");
            foreach (var entry in current.Directory.EnumerateFileSystemInfos("*", options))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++visited > maximumEntries) throw new IOException("Biblioteka przekracza limit liczby wpisów skanowania.");
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                if (entry is DirectoryInfo directory)
                {
                    if (current.Depth >= MaximumDepth) throw new IOException("Biblioteka przekracza limit głębokości folderów.");
                    pending.Push((directory, current.Depth + 1));
                }
                else yield return entry.FullName;
            }
        }
    }
}