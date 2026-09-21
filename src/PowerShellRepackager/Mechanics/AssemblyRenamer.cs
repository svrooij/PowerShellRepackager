using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;

namespace PowerShellRepackager.Mechanics;

/// <summary>
/// Renames a .NET assembly in place using System.Reflection.Metadata.
/// Used to give each repackaged module's copy of the generic loader a unique assembly identity,
/// so multiple repackaged modules can be imported into the same PowerShell session without the
/// runtime reusing the first-loaded loader (which would skip initialization of subsequent modules).
/// </summary>
/// <remarks>
/// System.Reflection.Metadata has no rewriting API, so the rename is performed as an in-place
/// binary patch: the AssemblyDef and ModuleDef name entries in the #Strings heap are overwritten
/// (the new name must fit in the existing entry's byte span; the remainder is zero-padded), and the
/// MVID in the #GUID heap is replaced so each renamed copy has a distinct module version id.
/// This is only safe for assemblies that are not strong-name signed, which holds for the loader.
/// </remarks>
internal static class AssemblyRenamer
{
    /// <summary>UTF-8 byte length of the loader's original assembly name ("Svrooij.PowerShellRepackager.GenericLoader"), which bounds the new name.</summary>
    internal const int MaxLoaderAssemblyNameLength = 42;

    /// <summary>
    /// Builds the per-module loader assembly name <c>Svrooij.{originalModuleName}.Loader</c>.
    /// If the result does not fit in the original name's heap slot, the module name part is
    /// deterministically truncated and suffixed with a short hash to keep it unique.
    /// </summary>
    internal static string BuildLoaderAssemblyName(string originalModuleName)
    {
        var name = $"Svrooij.{originalModuleName}.Loader";
        if (Encoding.UTF8.GetByteCount(name) <= MaxLoaderAssemblyNameLength)
            return name;

        // Truncate the module part and append a 4-char hash of the full name for uniqueness.
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(originalModuleName)))[..4];
        const string prefix = "Svrooij.";
        const string suffix = ".Loader";
        var budget = MaxLoaderAssemblyNameLength - prefix.Length - suffix.Length - hash.Length;

        var truncated = originalModuleName;
        while (Encoding.UTF8.GetByteCount(truncated) > budget)
            truncated = truncated[..^1];

        return $"{prefix}{truncated}{hash}{suffix}";
    }

    /// <summary>
    /// Renames the assembly at <paramref name="assemblyPath"/> to <paramref name="newAssemblyName"/>
    /// (module name becomes <c>{newAssemblyName}.dll</c>) and assigns a fresh MVID.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the new name does not fit in the existing metadata string slots or the
    /// metadata layout cannot be patched safely.
    /// </exception>
    internal static void RenameAssembly(string assemblyPath, string newAssemblyName)
    {
        var image = File.ReadAllBytes(assemblyPath);

        using var peReader = new PEReader(ImmutableArray.Create(image));
        var reader = peReader.GetMetadataReader();
        int metadataOffset = peReader.PEHeaders.MetadataStartOffset;
        if (metadataOffset < 0)
            throw new InvalidOperationException($"'{assemblyPath}' does not contain CLI metadata.");

        int stringsHeapOffset = metadataOffset + reader.GetHeapMetadataOffset(HeapIndex.String);

        // Patch the assembly definition name.
        var assemblyDef = reader.GetAssemblyDefinition();
        PatchStringsHeapEntry(image, reader, stringsHeapOffset, assemblyDef.Name, newAssemblyName, assemblyPath);

        // Patch the module definition name (conventionally "{assemblyName}.dll").
        var moduleDef = reader.GetModuleDefinition();
        PatchStringsHeapEntry(image, reader, stringsHeapOffset, moduleDef.Name, $"{newAssemblyName}.dll", assemblyPath);

        // Replace the MVID so each renamed copy has a distinct module identity.
        PatchMvid(image, reader, metadataOffset, moduleDef.Mvid);

        File.WriteAllBytes(assemblyPath, image);
    }

    /// <summary>
    /// Overwrites a null-terminated UTF-8 entry in the #Strings heap in place.
    /// The new value must not be longer than the existing value; remaining bytes are zeroed
    /// so heap offsets referenced by other handles stay valid.
    /// </summary>
    private static void PatchStringsHeapEntry(
        byte[] image,
        MetadataReader reader,
        int stringsHeapFileOffset,
        StringHandle handle,
        string newValue,
        string assemblyPath)
    {
        var currentValue = reader.GetString(handle);
        var currentBytes = Encoding.UTF8.GetByteCount(currentValue);
        var newBytes = Encoding.UTF8.GetBytes(newValue);

        if (newBytes.Length > currentBytes)
        {
            throw new InvalidOperationException(
                $"Cannot rename '{currentValue}' to '{newValue}' in '{assemblyPath}': " +
                $"the new name ({newBytes.Length} bytes) exceeds the existing metadata slot ({currentBytes} bytes).");
        }

        int entryOffset = stringsHeapFileOffset + reader.GetHeapOffset(handle);

        // Sanity check: the bytes at the computed offset must match the current string.
        var existing = Encoding.UTF8.GetString(image, entryOffset, currentBytes);
        if (!string.Equals(existing, currentValue, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Metadata layout mismatch while renaming '{assemblyPath}': expected '{currentValue}' at strings heap offset {reader.GetHeapOffset(handle)}.");
        }

        Array.Copy(newBytes, 0, image, entryOffset, newBytes.Length);
        // Zero the remainder of the slot (including the original null terminator position).
        Array.Clear(image, entryOffset + newBytes.Length, currentBytes - newBytes.Length);
    }

    /// <summary>
    /// Replaces the module's MVID (a fixed 16-byte entry in the #GUID heap) with a new GUID.
    /// </summary>
    private static void PatchMvid(byte[] image, MetadataReader reader, int metadataOffset, GuidHandle mvidHandle)
    {
        var currentMvid = reader.GetGuid(mvidHandle);
        int guidHeapOffset = metadataOffset + reader.GetHeapMetadataOffset(HeapIndex.Guid);

        // GUID handles are 1-based indexes of 16-byte entries.
        int index = MetadataTokens.GetHeapOffset(mvidHandle);
        int entryOffset = guidHeapOffset + (index - 1) * 16;

        // Sanity check: the bytes at the computed offset must match the current MVID.
        var existing = new Guid(image.AsSpan(entryOffset, 16));
        if (existing != currentMvid)
        {
            // Layout differs from expectation; skip the MVID patch rather than corrupting the file.
            // Distinct assembly names are sufficient for distinct identities.
            return;
        }

        Guid.NewGuid().TryWriteBytes(image.AsSpan(entryOffset, 16));
    }
}
