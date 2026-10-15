namespace FinalCheck.Core.Storage;

/// <summary>Platform shell operation; the caller supplies the active data location.</summary>
public interface IStorageFolderOpener
{
    void Open(string absoluteDirectory);
}
