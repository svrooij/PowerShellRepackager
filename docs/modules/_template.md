# Svrooij.{Module}

Repackaged version of the **{Module}** PowerShell module, published by {Original author}.

> This package is **not** produced, endorsed, or supported by {Original author}. For support of the underlying
> cmdlets, refer to the original module. For issues with the repackaging itself (loading, isolation,
> manifest), [open an issue](https://github.com/svrooij/PowerShellRepackager/issues) in this repository.

## Original module

| | |
|---|---|
| Name | `{Module}` |
| Author | {Original author} |
| PowerShell Gallery | https://www.powershellgallery.com/packages/{Module} |
| Project | {ProjectUri} |
| License | {LicenseUri} |

The original license applies to all original files contained in the repackaged module.

## Why repackaged?

{Describe the dependency conflict this solves.}

## Versions

| Repackaged version | Original version | Notes |
|---|---|---|
| {version} | [{version}](https://www.powershellgallery.com/packages/{Module}/{version}) | Initial repackage |

## Changes compared to the original

- Module name `Svrooij.{Module}`, new GUID (`2807…`)
- `CompatiblePSEditions = @('Core')`, `PowerShellVersion = '7.4'`
- `bin/Svrooij.PowerShellRepackager.GenericLoader.dll` added as nested module for assembly isolation
