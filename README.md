# PowerShell Repackager

Why would you need repacked PowerShell Modules? Mainly because someone decided that all modules are loaded in a shared Assembly Context, meaning the first module that imports a specific version of a dependency dictates which version **all** other modules can use. This is also know as *dependency hell*, and if super frustrating.

## Dependency issue

- `Module A` needs `Microsoft.Identity.Client 10.0.0`
- `Module B` needs `Microsoft.Identity.Client 8.0.0`

If module A is loaded and then module B, everything works, but module B will use the newer version, which might nog be compatible.

If they are loaded the other way around, module B is loaded as expected, but module A will refuse to load.

And if you want to use various Microsoft modules in the same session, this is where your issues start. Meet PowerShell Repackager.

## Notice

I do not like there is the need of such a repackager, nor did I fancy building something to fix it. Having deep knowledge about the issue and having build [CaPolice](https://github.com/svrooij/CaPolice) with a special Assembly Load Context gives me the ability to ask good questions to GitHub Copilot.

Expect the code here to be written with the help of AI, just by asking good questions.

For me it started with this prompt:

> PowerShell is notorious for dependency issues, where different modules try to load different versions of the same dll. And since they are all loaded in the same context this gives issues.
> Can you make a details plan for this in a markdown file, without writing any code just yet?
> 
> 1. Download the raw powershell module file for the gallery by using the module name and version (eg. `MicrosoftTeams` version `7.9.0`)
> 2. Extract the files
> 3. Generate an Assembly loader that pre-loads the dlls that are in the module in a seperate AssemblyLoadContext
> 4. Generate a new manifest from the original manifest, change the name to `Svrooij.{originalName}`, change the first four chars of the guid to 2807, and inject the loader
> 5. (optional) upgrade dependencies if requested
> 6. (optional) replace ApplicationInsights dll with a fake dll that does not transmit anything
> 7. package new module
> 8. (optional) publish to powershell gallery is requested

That resulted in [PLAN.md](./PLAN.md), which is the blueprint of all the code here.
