using System.IO;
using System.Text.Json;

namespace Svrooij.PowerShellRepackager.GenericLoader;

/// <summary>
/// Loads and parses the JSON configuration file for the generic loader.
/// The configuration file is placed alongside the loader DLL in the module's bin folder.
/// </summary>
internal static class ConfigurationLoader
{
    private const string ConfigFileName = "loader-config.json";

    /// <summary>
    /// Loads the configuration from a JSON file located alongside the loader assembly.
    /// </summary>
    /// <param name="loaderAssemblyLocation">
    /// The file path to the generic loader assembly.
    /// The configuration file is expected to be in the same directory.
    /// </param>
    /// <returns>The parsed <see cref="LoaderConfiguration"/>.</returns>
    /// <exception cref="FileNotFoundException">Thrown if the configuration file is not found.</exception>
    /// <exception cref="JsonException">Thrown if the configuration file is invalid JSON.</exception>
    /// <exception cref="InvalidOperationException">Thrown if the configuration is invalid or missing required fields.</exception>
    internal static LoaderConfiguration LoadConfiguration(string loaderAssemblyLocation)
    {
        string? loaderDir = Path.GetDirectoryName(loaderAssemblyLocation);
        if (loaderDir == null)
            throw new InvalidOperationException($"Cannot determine directory for loader assembly at '{loaderAssemblyLocation}'.");

        string configPath = Path.Combine(loaderDir, ConfigFileName);

        if (!File.Exists(configPath))
            throw new FileNotFoundException($"Loader configuration file not found at '{configPath}'.", configPath);

        try
        {
            string jsonContent = File.ReadAllText(configPath);
            var config = JsonSerializer.Deserialize<LoaderConfiguration>(jsonContent)
                ?? throw new InvalidOperationException("Loader configuration deserialized to null.");

            ValidateConfiguration(config);
            return config;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Failed to parse loader configuration at '{configPath}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Validates that the configuration contains all required fields and valid values.
    /// </summary>
    /// <param name="config">The configuration to validate.</param>
    /// <exception cref="InvalidOperationException">Thrown if validation fails.</exception>
    private static void ValidateConfiguration(LoaderConfiguration config)
    {
        if (string.IsNullOrWhiteSpace(config.ModuleName))
            throw new InvalidOperationException("LoaderConfiguration.ModuleName is required and cannot be null or whitespace.");

        if (config.PreloadAssemblies == null || config.PreloadAssemblies.Length == 0)
            throw new InvalidOperationException("LoaderConfiguration.PreloadAssemblies must be a non-empty array.");
    }
}
