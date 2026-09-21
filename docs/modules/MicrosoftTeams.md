# Svrooij.MicrosoftTeams

Repackaged version of the **MicrosoftTeams** PowerShell module, published by Microsoft Corporation.

> This package is **not** produced, endorsed, or supported by Microsoft. For support of the underlying
> cmdlets, refer to the original module. For issues with the repackaging itself (loading, isolation,
> manifest), [open an issue](https://github.com/svrooij/PowerShellRepackager/issues) in this repository.

## Original module

| | |
|---|---|
| Name | `MicrosoftTeams` |
| Author | Microsoft Corporation |
| PowerShell Gallery | https://www.powershellgallery.com/packages/MicrosoftTeams |
| Documentation | https://learn.microsoft.com/powershell/teams/ |
| License | https://learn.microsoft.com/legal/microsoft-365/teams-powershell-eula |

The original license applies to all original files (binaries, scripts, help and format files) contained in
the repackaged module. A copy is included in the module folder as `LICENSE.original.txt` when it is bundled
in the original package.

## Why repackaged?

The MicrosoftTeams module ships a large set of dependencies (e.g. `Microsoft.Identity.Client`,
`Newtonsoft.Json`, `Azure.Core`) that frequently conflict with other modules such as `Az.*` or
`Microsoft.Graph.*` when loaded into the same PowerShell session. The repackaged module loads these
dependencies in a private `AssemblyLoadContext` so they no longer collide.

## Versions

| Repackaged version | Original version | Notes |
|---|---|---|
| 7.9.0 | [7.9.0](https://www.powershellgallery.com/packages/MicrosoftTeams/7.9.0) | Initial repackage |

## Changes compared to the original

- Module name `Svrooij.MicrosoftTeams`, new GUID (`2807…`)
- `CompatiblePSEditions = @('Core')`, `PowerShellVersion = '7.4'`; legacy `net472` content removed
- Original root `.psm1` copied to `MicrosoftTeams.original.ps1` and dot-sourced from a generated wrapper
- `bin/Svrooij.PowerShellRepackager.GenericLoader.dll` added as nested module for assembly isolation
- Authenticode signatures stripped (the files were modified, so the original signatures would be invalid)
