using System;
using System.Management.Automation;
using System.Reflection;
using System.Runtime.Loader;

namespace Svrooij.PowerShellRepackager.GenericLoader;

/// <summary>
/// Handles module load/unload lifecycle for repackaged modules.
/// Implements <see cref="IModuleAssemblyInitializer"/> and <see cref="IModuleAssemblyCleanup"/>
/// to wire up the custom <see cref="GenericAssemblyLoadContext"/> when the module is imported.
/// </summary>
/// <remarks>
/// This initializer is generic and works for any repackaged module.
/// The module-specific configuration (which DLLs to isolate, target framework, etc.) is read
/// from a JSON file (loader-config.json) placed alongside this loader DLL.
/// </remarks>
public sealed class GenericModuleInitializer : IModuleAssemblyInitializer, IModuleAssemblyCleanup
{
    private static GenericAssemblyLoadContext? s_alc;
    private static object s_lock = new();

    /// <summary>
    /// Called by PowerShell when a module using this loader is imported.
    /// Initializes the private AssemblyLoadContext and registers with Default ALC's Resolving event.
    /// </summary>
    public void OnImport()
    {
        lock (s_lock)
        {
            // Guard against double-initialization if the module is imported multiple times.
            if (s_alc != null)
                return;

            try
            {
                // Get the location of this loader assembly.
                string loaderAssemblyPath = typeof(GenericModuleInitializer).Assembly.Location;

                // Load the configuration for this specific repackaged module.
                LoaderConfiguration config = ConfigurationLoader.LoadConfiguration(loaderAssemblyPath);

                // Create the private AssemblyLoadContext for this module.
                s_alc = new GenericAssemblyLoadContext(config, loaderAssemblyPath);

                // Preload the critical dependencies to ensure they are available immediately.
                s_alc.Preload(config.PreloadAssemblies);

                // Register the resolver with the Default ALC so requests for isolated assemblies
                // are intercepted and resolved from our private ALC.
                AssemblyLoadContext.Default.Resolving += OnResolving;
            }
            catch (Exception ex)
            {
                // If initialization fails, write to the PowerShell host so the user sees the error.
                // In a production system, this might also log to a file or event log.
                string message = $"GenericModuleInitializer.OnImport() failed: {ex.GetType().Name}: {ex.Message}";
                if (ex.InnerException != null)
                    message += $" InnerException: {ex.InnerException.Message}";

                // Attempt to write to PowerShell's error stream if available.
                try
                {
                    System.Diagnostics.Debug.WriteLine(message);
                }
                catch
                {
                    // If all else fails, silently continue. This prevents module import from failing entirely.
                }

                // Re-throw so PowerShell sees the error.
                throw;
            }
        }
    }

    /// <summary>
    /// Called by PowerShell when the module is removed.
    /// Unregisters the dependency resolver to allow proper cleanup.
    /// </summary>
    public void OnRemove(PSModuleInfo psModuleInfo)
    {
        lock (s_lock)
        {
            if (s_alc != null)
            {
                try
                {
                    // Unregister the resolver.
                    AssemblyLoadContext.Default.Resolving -= OnResolving;
                }
                finally
                {
                    s_alc = null;
                }
            }
        }
    }

    /// <summary>
    /// Callback registered with Default ALC's Resolving event.
    /// Attempts to resolve assembly requests from the module's private ALC.
    /// </summary>
    private static Assembly? OnResolving(AssemblyLoadContext defaultAlc, AssemblyName assemblyName)
    {
        if (s_alc == null)
            return null;

        return s_alc.ResolveFromBin(assemblyName);
    }
}
