using Windows.Storage;

namespace PresenterScript.Services;

/// <summary>Most-recently-used file list persisted in the app's local settings.</summary>
public sealed class RecentFilesService
{
    private const string Key = "RecentFiles";
    private const int MaxItems = 10;

    private static ApplicationDataContainer Settings => ApplicationData.Current.LocalSettings;

    public IReadOnlyList<string> Load()
    {
        var raw = Settings.Values[Key] as string;
        if (string.IsNullOrEmpty(raw))
        {
            return [];
        }

        var all = raw.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var existing = all.Where(File.Exists).ToList();
        if (existing.Count != all.Length)
        {
            Save(existing);
        }
        return existing;
    }

    public IReadOnlyList<string> Add(string path)
    {
        var list = Load().Where(p => !string.Equals(p, path, StringComparison.OrdinalIgnoreCase)).ToList();
        list.Insert(0, path);
        if (list.Count > MaxItems)
        {
            list.RemoveRange(MaxItems, list.Count - MaxItems);
        }
        Save(list);
        return list;
    }

    public IReadOnlyList<string> Remove(string path)
    {
        var list = Load().Where(p => !string.Equals(p, path, StringComparison.OrdinalIgnoreCase)).ToList();
        Save(list);
        return list;
    }

    private static void Save(IEnumerable<string> paths) => Settings.Values[Key] = string.Join('\n', paths);
}
