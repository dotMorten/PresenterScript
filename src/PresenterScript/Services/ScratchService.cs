using System.Security.Cryptography;
using System.Text;
using Windows.Storage;

namespace PresenterScript.Services;

/// <summary>
/// Keeps unsaved edits per document in LocalState\Scratch so they survive closing the app, and remembers which
/// document was open last. Failures are ignored: scratch space is a convenience and must never break editing.
/// </summary>
public sealed class ScratchService
{
    private const string LastDocumentKey = "LastDocument";
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    private static string Folder => Path.Combine(ApplicationData.Current.LocalFolder.Path, "Scratch");

    /// <summary>Path of the document that was open last, <c>""</c> for an untitled document, or null if unknown.</summary>
    public string? LastDocument
    {
        get => ApplicationData.Current.LocalSettings.Values[LastDocumentKey] as string;
        set => ApplicationData.Current.LocalSettings.Values[LastDocumentKey] = value;
    }

    public string? Load(string? documentPath)
    {
        try
        {
            var file = FileFor(documentPath);
            return File.Exists(file) ? File.ReadAllText(file) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Save(string? documentPath, string text)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var file = FileFor(documentPath);
            var temp = file + ".tmp";
            File.WriteAllText(temp, text, Utf8NoBom);
            File.Move(temp, file, overwrite: true);
        }
        catch (Exception)
        {
        }
    }

    public void Clear(string? documentPath)
    {
        try
        {
            File.Delete(FileFor(documentPath));
        }
        catch (Exception)
        {
        }
    }

    private static string FileFor(string? documentPath)
    {
        if (documentPath is null)
        {
            return Path.Combine(Folder, "untitled.md");
        }
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(documentPath).ToUpperInvariant()));
        return Path.Combine(Folder, Convert.ToHexString(hash, 0, 16) + ".md");
    }
}
