using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Svrooij.PowerShellRepackager.GenericLoader;

/// <summary>
/// Determines the .NET runtime identifiers (RIDs) that match the current process so the loader can pick
/// the correct <c>runtimes/{rid}/native</c> and <c>runtimes/{rid}/lib</c> folders from a NuGet-style layout.
/// </summary>
internal static class RuntimeIdentifiers
{
    /// <summary>
    /// The most specific RID for the current OS and process architecture (e.g. "win-x64", "linux-arm64", "osx-arm64").
    /// </summary>
    internal static string Current { get; } = $"{GetOsPart()}-{GetArchPart()}";

    /// <summary>
    /// Returns compatible RIDs ordered from most to least specific, e.g. ["win-x64", "win", "any"].
    /// </summary>
    internal static IEnumerable<string> GetCompatibleRids()
    {
        yield return Current;
        yield return GetOsPart();
        if (OperatingSystem.IsLinux())
            yield return "unix";
        if (OperatingSystem.IsMacOS())
            yield return "unix";
        yield return "any";
    }

    /// <summary>
    /// Returns the file names to try for a P/Invoke library name. .NET itself probes name variations
    /// (with/without extension, lib prefix); this mirrors that so files in the module folder are found.
    /// </summary>
    internal static IEnumerable<string> GetNativeFileNameCandidates(string unmanagedDllName)
    {
        yield return unmanagedDllName;

        var hasExtension = unmanagedDllName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            || unmanagedDllName.EndsWith(".so", StringComparison.OrdinalIgnoreCase)
            || unmanagedDllName.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase);

        if (OperatingSystem.IsWindows())
        {
            if (!hasExtension)
                yield return unmanagedDllName + ".dll";
        }
        else if (OperatingSystem.IsMacOS())
        {
            if (!hasExtension)
            {
                yield return unmanagedDllName + ".dylib";
                yield return "lib" + unmanagedDllName + ".dylib";
            }
            else if (!unmanagedDllName.StartsWith("lib", StringComparison.Ordinal))
            {
                yield return "lib" + unmanagedDllName;
            }
        }
        else
        {
            if (!hasExtension)
            {
                yield return unmanagedDllName + ".so";
                yield return "lib" + unmanagedDllName + ".so";
            }
            else if (!unmanagedDllName.StartsWith("lib", StringComparison.Ordinal))
            {
                yield return "lib" + unmanagedDllName;
            }
        }
    }

    private static string GetOsPart()
    {
        if (OperatingSystem.IsWindows()) return "win";
        if (OperatingSystem.IsMacOS()) return "osx";
        if (OperatingSystem.IsLinux()) return "linux";
        return "unknown";
    }

    private static string GetArchPart() => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X64 => "x64",
        Architecture.X86 => "x86",
        Architecture.Arm64 => "arm64",
        Architecture.Arm => "arm",
        _ => RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
    };
}
