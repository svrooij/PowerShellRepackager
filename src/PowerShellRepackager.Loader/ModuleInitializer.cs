using System;
using System.Management.Automation;
using System.Reflection;
using System.Runtime.Loader;

namespace PowerShellRepackager.Loader;

/// <summary>Handles module load/unload lifecycle and wires up the custom <see cref="PowerShellRepackagerAssemblyLoadContext"/>.</summary>
public sealed class ModuleInitializer : IModuleAssemblyInitializer, IModuleAssemblyCleanup
{
    private static PowerShellRepackagerAssemblyLoadContext? s_alc;
    private static object s_lock = new();


    // Assemblies that PowerShell or other modules may load into the Default ALC before our
    // Resolving handler gets a chance to intercept. Pre-loading them into our private ALC
    // ensures our versions are used when the DI container or other internal code requests them.
    private static readonly string[] s_preloadAssemblies =
    [
        "Microsoft.Extensions.DependencyInjection.Abstractions",
        "Microsoft.Extensions.Logging.Abstractions",
        "System.Text.Json",
        "System.Diagnostics.DiagnosticSource",
        "System.IO.Pipelines",
        "Microsoft.Extensions.DependencyInjection",
        "Microsoft.Extensions.Logging",
        "Microsoft.Extensions.Options",
        "Microsoft.Extensions.Primitives",
        "Microsoft.ApplicationInsights"
    ];

    /// <summary>Called by PowerShell when this module is imported. Registers the dependency resolver.</summary>
    public void OnImport()
    {
        lock (s_lock)
        {
            if (s_alc != null)
                return;

            s_alc = new PowerShellRepackagerAssemblyLoadContext();
            s_alc.Preload(s_preloadAssemblies);
            AssemblyLoadContext.Default.Resolving += OnResolving;
        }
    }

    /// <summary>Called by PowerShell when this module is removed. Unregisters the dependency resolver.</summary>
    public void OnRemove(PSModuleInfo psModuleInfo)
    {
        lock (s_lock)
        {
            if (s_alc != null)
            {
                try
                {
                    AssemblyLoadContext.Default.Resolving -= OnResolving;
                }
                finally
                {
                    s_alc = null;
                }
            }
        }
    }

    private static Assembly? OnResolving(AssemblyLoadContext defaultAlc, AssemblyName assemblyName)
    {
        if (s_alc == null)
            return null;
        return s_alc.ResolveFromDependencies(assemblyName);
    }
}