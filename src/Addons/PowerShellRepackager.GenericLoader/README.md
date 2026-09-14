# Generic Loader for PowerShellRepackager

## Overview

The `Svrooij.PowerShellRepackager.GenericLoader` is a reusable, configuration-driven AssemblyLoadContext (ALC) loader that isolates a repackaged module's bundled assemblies to prevent DLL version collisions with other modules.

Unlike the module-specific `PowerShellRepackager.Loader` (which is hardcoded for this tool itself), the generic loader accepts runtime configuration so it can be deployed with any repackaged module.

## Architecture

### Components

1. **`GenericAssemblyLoadContext`** — The custom ALC that:
   - Discovers and loads all `.dll` files in the module's `bin/` folder
   - Returns `null` for assemblies not found (falls through to Default ALC)
   - Allows framework and PowerShell SDK assemblies to remain compatible

2. **`GenericModuleInitializer`** — Implements PowerShell's `IModuleAssemblyInitializer` and `IModuleAssemblyCleanup`:
   - Called by PowerShell when the module is imported (`OnImport()`)
   - Loads the module-specific configuration
   - Creates and configures the private ALC
   - Preloads critical dependencies
   - Registers with Default ALC's `Resolving` event
   - Cleans up on module removal (`OnRemove()`)

3. **`LoaderConfiguration`** — A data class defining:
   - `ModuleName` — The repackaged module name (e.g., "Svrooij.MicrosoftTeams")
   - `PreloadAssemblies` — Array of assemblies to eagerly preload at import time
   - Optional metadata: original module name/version, repackage date, tool version

4. **`ConfigurationLoader`** — Reads and validates the JSON configuration file:
   - Expects `loader-config.json` in the same directory as the loader DLL
   - Parses JSON and validates all required fields

## Usage Flow

### 1. Repackager Tool Side (Build Time)

When repackaging a module (e.g., MicrosoftTeams):

```
Repackage MicrosoftTeams
  ↓
Extract .nupkg and normalize:
  - Collect all assemblies from flat /bin and selected TFM folder
  - Total: 74 assemblies (7 flat + 67 TFM-specific)
  ↓
Consolidate into single flat /bin folder:
  - Copy all 74 DLLs to: output_module/bin/
  - No TFM-specific subfolders in output
  ↓
Generate loader-config.json with:
  - moduleName: "Svrooij.MicrosoftTeams"
  - preloadAssemblies: [subset of DLLs, prioritizing critical deps]
  - Optional: originalModuleName, originalModuleVersion (traceability)
  ↓
Copy prebuilt Svrooij.PowerShellRepackager.GenericLoader.dll to:
  output_module/bin/
  ↓
Rewrite module manifest (Svrooij.MicrosoftTeams.psd1):
  - RootModule points to loader DLL (in same bin/ folder)
  - ModuleInitializer set to GenericModuleInitializer
```

### 2. PowerShell Import Side (Runtime)

When user imports the repackaged module:

```powershell
Import-Module Svrooij.MicrosoftTeams
  ↓
PowerShell loads Svrooij.PowerShellRepackager.GenericLoader.dll from bin/
  ↓
Invokes GenericModuleInitializer.OnImport()
  ↓
  [In OnImport]
  1. Reads loader-config.json from same directory as loader DLL (bin/)
  2. Creates GenericAssemblyLoadContext with module-specific config
  3. Calls Preload() for critical assemblies (from bin/)
  4. Registers OnResolving() callback with Default ALC
  5. Returns control to PowerShell
  ↓
PowerShell imports the actual module's root module/cmdlets/functions
  ↓
When module code requests assemblies:
  - Default ALC.Resolving event fires
  - OnResolving() tries to load from the private ALC first
  - Private ALC checks bin/ folder for the assembly
  - If found: loads from private ALC (isolated from other modules)
  - If not found: returns null (framework/SDK assemblies resolve from Default ALC)
  ↓
Module import completes successfully with all 74 DLLs isolated in private ALC
```

## Configuration File Format

**File name:** `loader-config.json`  
**Location:** Alongside the loader DLL in the same directory (e.g., `output_module/bin/`)

**Example:**

```json
{
  "moduleName": "Svrooij.MicrosoftTeams",
  "preloadAssemblies": [
	"Microsoft.Identity.Client",
	"Newtonsoft.Json",
	"Microsoft.Extensions.Logging"
  ],
  "originalModuleName": "MicrosoftTeams",
  "originalModuleVersion": "7.9.0",
  "repackageDate": "2024-01-15T10:30:00Z",
  "toolVersion": "1.0.0"
}
```

### Field Descriptions

- **`moduleName`** *(required)* — The repackaged module name. Used to create a recognizable ALC name for diagnostics (e.g., `Svrooij_Svrooij.MicrosoftTeams_ALC`).
- **`preloadAssemblies`** *(required)* — Array of assembly simple names (without .dll extension) to load eagerly during initialization. These are loaded immediately in `OnImport()` to ensure they're available before any static constructors run. All other DLLs in `bin/` are loaded lazily on first request.
- **`originalModuleName`** *(optional)* — The original module name before repackaging (for traceability and audit logs).
- **`originalModuleVersion`** *(optional)* — The original module version before repackaging (for traceability and audit logs).
- **`repackageDate`** *(optional)* — ISO 8601 timestamp of when this module was repackaged.
- **`toolVersion`** *(optional)* — Version of PowerShellRepackager that created this configuration.

**Note:** There is no `targetFramework` field because all assemblies in the repackaged module are consolidated into a single flat `/bin/` folder, regardless of their original TFM origin.

## Key Design Decisions

### 1. **Folder-Based Discovery, Not Allow-List**

All `.dll` files in the module's `bin/` folder are automatically discovered and available for loading from the private ALC. There is no explicit allow-list of assembly names.

**Benefits:**
- Automatically captures all dependencies (including transitive ones)
- No risk of forgetting to list a dependency
- Simpler configuration (no need to enumerate DLLs)
- The folder boundary itself is the security perimeter (only bundled DLLs are there)

**Trade-off:**
- We lose explicit documentation of "which DLLs are isolated" in the config, but this can be recovered by scanning the actual folder at runtime if needed for diagnostics.

### 2. **Preload vs. Lazy Load**

`PreloadAssemblies` are loaded eagerly during `OnImport()` to ensure they are available *before* any module initialization code runs that might expect them via reflection or static constructors.

Non-preloaded assemblies are loaded lazily when first requested via the ALC's `Load()` mechanism.

### 3. **Idempotent Initialization**

`OnImport()` uses a static lock to prevent double-initialization if the module is imported multiple times in the same process.

### 4. **Graceful Error Handling**

If configuration loading or ALC initialization fails:
- The error is logged/debugged
- The exception is re-thrown so PowerShell sees the problem
- Module import fails with a clear error instead of silently breaking later

### 5. **Configuration-Driven, Not Code-Generated**

The loader is prebuilt once and reused for all modules. Configuration is a simple JSON file, eliminating the need for:
- Per-module code generation
- Per-module compilation
- Build toolchain dependencies at repackage time

This keeps the repackager simple and fast.

## Deployment

### For the Repackager Tool

The compiled `Svrooij.PowerShellRepackager.GenericLoader.dll` is built once and then bundled/embedded with the PowerShellRepackager tool as a prebuilt resource. It is reused for all repackaged modules (no per-module compilation).

### For Each Repackaged Module

When repackaging a module (e.g., MicrosoftTeams → Svrooij.MicrosoftTeams):

**Output Structure:**
```
Svrooij.MicrosoftTeams/
  bin/
    (all 74 DLLs consolidated here)
    Svrooij.PowerShellRepackager.GenericLoader.dll
    loader-config.json
  Svrooij.MicrosoftTeams.psd1    (rewritten manifest)
  Svrooij.MicrosoftTeams.psm1
  ... other module files ...
```

**Steps:**

1. Extract original `.nupkg` and discover available TFMs
2. Select the highest-priority compatible TFM (e.g., `netcore3.1` from `netcoreapp3.1`)
3. Collect all assemblies from:
   - Flat `/bin` folder (shared DLLs)
   - Selected TFM folder (version-specific DLLs)
4. Consolidate all DLLs into output `bin/` folder
5. Copy prebuilt `Svrooij.PowerShellRepackager.GenericLoader.dll` to `bin/`
6. Generate `loader-config.json` in `bin/`
7. Rewrite module manifest to:
   - Point RootModule to the loader DLL
   - Set ModuleInitializer to `Svrooij.PowerShellRepackager.GenericLoader.GenericModuleInitializer`
8. Package the module as usual

## Testing

To test the generic loader with a repackaged module:

1. **Build the loader:** `dotnet build src/Addons/PowerShellRepackager.GenericLoader/`
2. **Create a test module:** Structure it as:
   ```
   Svrooij.TestModule/
   ├── bin/
   │   ├── Svrooij.PowerShellRepackager.GenericLoader.dll
   │   ├── loader-config.json
   │   ├── TestModule.dll (or .psm1 for root module)
   │   └── [all other module DLLs consolidated here]
   ├── Svrooij.TestModule.psd1 (manifest rewritten to use loader)
   ├── Svrooij.TestModule.psm1
   └── ... other module files ...
   ```
3. **Generate loader-config.json** with:
   ```json
   {
     "moduleName": "Svrooij.TestModule",
     "preloadAssemblies": ["critical-dep-1", "critical-dep-2"],
     "originalModuleName": "TestModule",
     "originalModuleVersion": "1.0.0"
   }
   ```
4. **Import and test:**
   ```powershell
   Import-Module ./Svrooij.TestModule
   Get-Module | Format-Table
   # Verify no DLL conflicts, commands are available
   # Check that ALC is created: $alc = [System.Runtime.Loader.AssemblyLoadContext]::CurrentContextualReflectionContext
   ```

## Future Enhancements

- **Logging:** Integrate with a logging framework to record ALC lifecycle events
- **Diagnostics:** Expose ALC statistics (loaded assemblies, performance metrics) for troubleshooting
- **Dynamic Config Updates:** Support reloading configuration without reimporting the module
- **Multi-TFM Support:** Handle modules with multiple TFM folders (currently we select the highest compatible version at repackage time)
