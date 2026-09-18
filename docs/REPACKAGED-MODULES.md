# Repackaged modules

This page lists PowerShell modules that have been repackaged with
[PowerShellRepackager](https://github.com/svrooij/PowerShellRepackager) and published under the
`Svrooij.*` prefix on the [PowerShell Gallery](https://www.powershellgallery.com/profiles/svrooij).

> **Disclaimer** – These packages are **not** produced, endorsed, or supported by the original authors.
> The only changes are the module identity (name/GUID), a rewritten manifest, and a generic assembly loader
> that isolates the module's dependencies in a private `AssemblyLoadContext`. All original files remain
> subject to the original module's license. If you are the author of a listed module and object to the
> redistribution, please [open an issue](https://github.com/svrooij/PowerShellRepackager/issues).

## What is changed in a repackaged module?

| Manifest field | Original | Repackaged |
|---|---|---|
| Name / `RootModule` | `{Module}` | `Svrooij.{Module}` (+ generic loader as `NestedModules`) |
| `GUID` | original | new GUID starting with `2807` |
| `Author` / `CompanyName` | original author | Stephan van Rooij / Svrooij (original author kept in `Description` and `README.md`) |
| `Description` | original | attribution text + link to original + original description |
| `CompatiblePSEditions` | often `Desktop`, `Core` | `Core` only |
| `PowerShellVersion` | varies | `7.4` |
| `ProjectUri` | original | module page in this repository (see below) |
| `LicenseUri` | original | **unchanged** – the original license governs redistribution |
| `HelpInfoURI` / `IconUri` | original | removed |
| `Tags` | original | `Repackaged`, `PSEdition_Core` + original (minus `PSEdition_Desktop`) |

Each repackaged module folder also contains a `README.md` with attribution and, when the original package
bundles one, the original license as `LICENSE.original.txt`.

## Modules

| Repackaged module | Original module | Original author | License | Details |
|---|---|---|---|---|
| `Svrooij.MicrosoftTeams` | [MicrosoftTeams](https://www.powershellgallery.com/packages/MicrosoftTeams) | Microsoft Corporation | [Microsoft Teams PowerShell EULA](https://learn.microsoft.com/legal/microsoft-365/teams-powershell-eula) | [MicrosoftTeams.md](./modules/MicrosoftTeams.md) |

## Adding a module

1. Repackage and publish: `Publish-RepackagedModule -ModuleInfo $extracted -NewModuleName "Svrooij.{Module}" -Publish`
2. Copy `docs/modules/_template.md` to `docs/modules/{Module}.md` and fill it in (the generated manifest's
   `ProjectUri` already points there).
3. Add a row to the table above.
