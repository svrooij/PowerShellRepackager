namespace PowerShellRepackager.Models;

/// <summary>
/// Supported .NET target frameworks for module assembly isolation.
/// These are the only TFMs that work with PowerShell 7.4+.
/// </summary>
/// <remarks>
/// Folder naming conventions:
/// - .NET Core 3.1 can appear as either "netcore3.1" or "netcoreapp3.1"
/// - .NET 5.0+ use the format "net5.0", "net6.0", "net7.0", etc.
/// </remarks>
public enum TargetFramework
{
    /// <summary>
    /// .NET Core 3.1 (legacy but still supported by PS 7.4+)
    /// </summary>
    NetCore31,

    /// <summary>
    /// .NET 5.0
    /// </summary>
    Net50,

    /// <summary>
    /// .NET 6.0
    /// </summary>
    Net60,

    /// <summary>
    /// .NET 7.0
    /// </summary>
    Net70,

    /// <summary>
    /// .NET 8.0
    /// </summary>
    Net80,

    /// <summary>
    /// .NET 9.0
    /// </summary>
    Net90,
}

/// <summary>
/// Extension methods for <see cref="TargetFramework"/>.
/// </summary>
public static class TargetFrameworkExtensions
{
    /// <summary>
    /// Maps a <see cref="TargetFramework"/> enum value to its primary folder name (e.g., "net8.0").
    /// </summary>
    public static string ToFolderName(this TargetFramework tf) => tf switch
    {
        TargetFramework.NetCore31 => "netcore3.1",
        TargetFramework.Net50 => "net5.0",
        TargetFramework.Net60 => "net6.0",
        TargetFramework.Net70 => "net7.0",
        TargetFramework.Net80 => "net8.0",
        TargetFramework.Net90 => "net9.0",
        _ => throw new ArgumentOutOfRangeException(nameof(tf), tf, "Unknown target framework"),
    };

    /// <summary>
    /// Attempts to parse a folder name (e.g., "net8.0" or "netcoreapp3.1") to a <see cref="TargetFramework"/>.
    /// Handles both naming conventions: netcore3.1, netcoreapp3.1, net5.0, net6.0, etc.
    /// Case-insensitive matching.
    /// </summary>
    public static bool TryParseFolderName(string folderName, out TargetFramework result)
    {
        if (string.IsNullOrWhiteSpace(folderName))
        {
            result = default;
            return false;
        }

        // Normalize: trim and lowercase
        var normalized = folderName.Trim().ToLowerInvariant();

        result = normalized switch
        {
            "netcore3.1" or "netcoreapp3.1" => TargetFramework.NetCore31,
            "net5.0" => TargetFramework.Net50,
            "net6.0" => TargetFramework.Net60,
            "net7.0" => TargetFramework.Net70,
            "net8.0" => TargetFramework.Net80,
            "net9.0" => TargetFramework.Net90,
            _ => default,
        };

        return result != default || normalized == "netcore3.1" || normalized == "netcoreapp3.1";
    }

    /// <summary>
    /// Returns the version number for sorting (higher = newer).
    /// Used to select the highest compatible TFM when multiple are present.
    /// </summary>
    public static int GetVersionPriority(this TargetFramework tf) => tf switch
    {
        TargetFramework.NetCore31 => 1,
        TargetFramework.Net50 => 2,
        TargetFramework.Net60 => 3,
        TargetFramework.Net70 => 4,
        TargetFramework.Net80 => 5,
        TargetFramework.Net90 => 6,
        _ => 0,
    };
}
