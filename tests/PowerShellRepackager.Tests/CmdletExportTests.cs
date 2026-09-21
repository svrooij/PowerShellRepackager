using System.IO;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;

namespace PowerShellRepackager.Tests;

/// <summary>
/// Fixture that repackages MicrosoftTeams 7.9.0 once (using the built PowerShellRepackager module
/// in a real pwsh process) and exposes the resulting module for the tests in <see cref="CmdletExportTests"/>.
/// </summary>
public sealed class RepackagedTeamsFixture : IDisposable
{
    public const string OriginalModuleName = "MicrosoftTeams";
    public const string OriginalModuleVersion = "7.9.0";
    public const string NewModuleName = "Svrooij.MicrosoftTeams";

    public string PwshPath { get; }
    public string RepackagerManifestPath { get; }
    public string OutputPath { get; }
    public string RepackagedManifestPath { get; }
    public string RepackagedModuleDirectory { get; }
    public string? PackagePath { get; }
    public string RepackageOutput { get; }

    public RepackagedTeamsFixture()
    {
        PwshPath = ResolvePwsh();
        RepackagerManifestPath = ResolveRepackagerManifest();
        OutputPath = Path.Combine(Path.GetTempPath(), "PowerShellRepackager.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(OutputPath);

        var script = $$"""
            $ErrorActionPreference = 'Stop'
            Import-Module '{{RepackagerManifestPath}}' -Force
            $package = Get-ModulePackage -ModuleName '{{OriginalModuleName}}' -Version '{{OriginalModuleVersion}}'
            $extracted = Expand-ModulePackage -PackagePath $package
            $result = Publish-RepackagedModule -ModuleInfo $extracted -NewModuleName '{{NewModuleName}}' -OutputPath '{{OutputPath}}' -Pack
            Write-Output "MANIFEST_PATH=$($result.ManifestPath)"
            Write-Output "PACKAGE_PATH=$($result.PackagePath)"
            """;

        var (exitCode, stdout, stderr) = RunPwsh(PwshPath, script, TimeSpan.FromMinutes(10));
        RepackageOutput = stdout + Environment.NewLine + stderr;

        PackagePath = stdout
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault(l => l.StartsWith("PACKAGE_PATH=", StringComparison.Ordinal))?["PACKAGE_PATH=".Length..].Trim();

        if (exitCode != 0)
        {
            throw new InvalidOperationException(
                $"Repackaging {OriginalModuleName} {OriginalModuleVersion} failed (exit code {exitCode}).{Environment.NewLine}{RepackageOutput}");
        }

        var manifestLine = stdout
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault(l => l.StartsWith("MANIFEST_PATH=", StringComparison.Ordinal));

        RepackagedManifestPath = manifestLine?["MANIFEST_PATH=".Length..].Trim()
            ?? throw new InvalidOperationException($"Publish-RepackagedModule did not report a manifest path.{Environment.NewLine}{RepackageOutput}");

        RepackagedModuleDirectory = Path.GetDirectoryName(RepackagedManifestPath)
            ?? throw new InvalidOperationException($"Could not determine module directory from '{RepackagedManifestPath}'.");
    }

    public (int ExitCode, string StdOut, string StdErr) RunPwsh(string script, TimeSpan? timeout = null)
        => RunPwsh(PwshPath, script, timeout ?? TimeSpan.FromMinutes(5));

    private static (int ExitCode, string StdOut, string StdErr) RunPwsh(string pwshPath, string script, TimeSpan timeout)
    {
        var scriptFile = Path.Combine(Path.GetTempPath(), $"psrepack-test-{Guid.NewGuid():N}.ps1");
        File.WriteAllText(scriptFile, script);
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = pwshPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("-NoLogo");
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-NonInteractive");
            psi.ArgumentList.Add("-ExecutionPolicy");
            psi.ArgumentList.Add("Bypass");
            psi.ArgumentList.Add("-File");
            psi.ArgumentList.Add(scriptFile);

            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException($"Failed to start '{pwshPath}'.");

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit((int)timeout.TotalMilliseconds))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
                throw new TimeoutException($"pwsh did not finish within {timeout}.");
            }

            return (process.ExitCode, stdoutTask.Result, stderrTask.Result);
        }
        finally
        {
            try { File.Delete(scriptFile); } catch { /* best effort */ }
        }
    }

    private static string ResolvePwsh()
    {
        var fromEnv = Environment.GetEnvironmentVariable("PWSH_PATH");
        if (!string.IsNullOrWhiteSpace(fromEnv) && File.Exists(fromEnv))
        {
            return fromEnv;
        }

        var exeName = OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh";
        var pathDirs = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        foreach (var dir in pathDirs)
        {
            var candidate = Path.Combine(dir, exeName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        if (OperatingSystem.IsWindows())
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var candidate = Path.Combine(programFiles, "PowerShell", "7", "pwsh.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            "PowerShell 7 (pwsh) was not found. Install PowerShell 7.4+ or set the PWSH_PATH environment variable.");
    }

    private static string ResolveRepackagerManifest()
    {
        // The test project references PowerShellRepackager, so the module DLL is copied next to the test
        // assembly, but the module needs its own output folder layout (psd1/psm1 + Dependencies/).
        // Walk up from the test output directory to the repo root and locate the built module there.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PowerShellRepackager.slnx")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            throw new InvalidOperationException("Could not locate the repository root from " + AppContext.BaseDirectory);
        }

        var moduleBin = Path.Combine(dir.FullName, "src", "PowerShellRepackager", "bin");
        var candidates = new[] { "Debug", "Release" }
            .Select(c => Path.Combine(moduleBin, c, "net8.0", "PowerShellRepackager.psd1"))
            .Where(File.Exists)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToList();

        return candidates.FirstOrDefault()
            ?? throw new InvalidOperationException(
                $"PowerShellRepackager.psd1 was not found under '{moduleBin}'. Build the PowerShellRepackager project first.");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(OutputPath))
            {
                Directory.Delete(OutputPath, recursive: true);
            }
        }
        catch
        {
            // best effort cleanup
        }
    }
}

/// <summary>
/// Integration tests that repackage MicrosoftTeams 7.9.0 and validate the resulting module.
/// Requires PowerShell 7.4+ (pwsh) and network access to the PowerShell Gallery.
/// </summary>
[ClassDataSource<RepackagedTeamsFixture>(Shared = SharedType.PerClass)]
public class CmdletExportTests
{
    private readonly RepackagedTeamsFixture _fixture;

    public CmdletExportTests(RepackagedTeamsFixture fixture)
    {
        _fixture = fixture;
    }

    [Test]
    public async Task RepackagedModule_ShouldExportCmdlets()
    {
        // Arrange
        var script = $$"""
            $ErrorActionPreference = 'Stop'
            $module = Import-Module '{{_fixture.RepackagedManifestPath}}' -Force -PassThru
            $exported = @($module.ExportedCommands.Keys | Sort-Object)
            [pscustomobject]@{
                Name = $module.Name
                Version = $module.Version.ToString()
                Cmdlets = @($module.ExportedCmdlets.Keys | Sort-Object)
                Functions = @($module.ExportedFunctions.Keys | Sort-Object)
                Commands = $exported
            } | ConvertTo-Json -Depth 3 -Compress
            """;

        // Act
        var (exitCode, stdout, stderr) = _fixture.RunPwsh(script);

        // Assert
        await Assert.That(exitCode == 0).IsTrue().Because($"Importing repackaged module failed (exit code {exitCode}).{Environment.NewLine}STDOUT:{Environment.NewLine}{stdout}{Environment.NewLine}STDERR:{Environment.NewLine}{stderr}{Environment.NewLine}Repackage output:{Environment.NewLine}{_fixture.RepackageOutput}");

        var jsonLine = stdout
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault(l => l.TrimStart().StartsWith('{'));
        await Assert.That(string.IsNullOrWhiteSpace(jsonLine)).IsFalse().Because($"No JSON output from pwsh.{Environment.NewLine}{stdout}{Environment.NewLine}{stderr}");

        using var doc = JsonDocument.Parse(jsonLine!);
        var root = doc.RootElement;

        await Assert.That(root.GetProperty("Name").GetString()).IsEqualTo(RepackagedTeamsFixture.NewModuleName);
        await Assert.That(root.GetProperty("Version").GetString()).IsEqualTo(RepackagedTeamsFixture.OriginalModuleVersion);

        var commands = root.GetProperty("Commands").EnumerateArray().Select(e => e.GetString()).ToList();
        await Assert.That(commands.Count > 0).IsTrue().Because($"Repackaged module exported no commands.{Environment.NewLine}{stdout}{Environment.NewLine}{stderr}");

        await Assert.That(commands).Contains("Connect-MicrosoftTeams");
        await Assert.That(commands).Contains("Get-Team");
        await Assert.That(commands).Contains("Get-CsOnlineUser");
    }

    [Test]
    public async Task RepackagedModule_ManifestShouldBeValid()
    {
        // Arrange
        var script = $$"""
            $ErrorActionPreference = 'Stop'
            $manifest = Test-ModuleManifest -Path '{{_fixture.RepackagedManifestPath}}'
            Write-Output "NAME=$($manifest.Name)"
            Write-Output "GUID=$($manifest.Guid)"
            Write-Output "PSVERSION=$($manifest.PowerShellVersion)"
            Write-Output "EDITIONS=$($manifest.CompatiblePSEditions -join ',')"
            Write-Output "AUTHOR=$($manifest.Author)"
            Write-Output "PROJECTURI=$($manifest.ProjectUri)"
            Write-Output "LICENSEURI=$($manifest.LicenseUri)"
            Write-Output "HELPURI=$($manifest.HelpInfoUri)"
            Write-Output "TAGS=$($manifest.Tags -join ',')"
            Write-Output "DESCRIPTION=$($manifest.Description)"
            Write-Output "README=$(Test-Path (Join-Path '{{_fixture.RepackagedModuleDirectory}}' 'README.md'))"
            Write-Output "HEADER=$((Get-Content -Path '{{_fixture.RepackagedManifestPath}}' -TotalCount 2)[1])"
            """;

        // Act
        var (exitCode, stdout, stderr) = _fixture.RunPwsh(script);

        // Assert
        await Assert.That(exitCode == 0).IsTrue().Because($"Test-ModuleManifest failed (exit code {exitCode}).{Environment.NewLine}{stdout}{Environment.NewLine}{stderr}");
        await Assert.That(stdout).Contains($"NAME={RepackagedTeamsFixture.NewModuleName}");
        await Assert.That(stdout).Contains("GUID=2807");
        await Assert.That(stdout).Contains("PSVERSION=7.4");
        await Assert.That(stdout).Contains("EDITIONS=Core");
        await Assert.That(stdout).Contains("AUTHOR=Stephan van Rooij");
        await Assert.That(stdout).Contains("PROJECTURI=https://github.com/svrooij/PowerShellRepackager/blob/main/docs/modules/MicrosoftTeams.md");
        await Assert.That(stdout).Matches("LICENSEURI=https?://");
        await Assert.That(stdout.Replace("\n", "\r\n").Replace("\r\r", "\r")).Contains("HELPURI=\r\n");
        await Assert.That(stdout).Contains("Repackaged");
        await Assert.That(stdout).Contains("PSEdition_Core");
        await Assert.That(stdout).DoesNotContain("PSEdition_Desktop");
        await Assert.That(stdout).Contains("DESCRIPTION=Repackaged version of MicrosoftTeams 7.9.0 by Microsoft Corporation");
        await Assert.That(stdout).Contains("README=True");
        await Assert.That(stdout).Contains("HEADER=# Original Module: MicrosoftTeams v7.9.0");
    }

    [Test]
    public async Task RepackagedModule_ShouldContainRequiredFiles()
    {
        // Arrange
        var moduleDir = _fixture.RepackagedModuleDirectory;

        // Assert
        await Assert.That(File.Exists(_fixture.RepackagedManifestPath)).IsTrue().Because($"Manifest missing: {_fixture.RepackagedManifestPath}");
        await Assert.That(Path.GetFileName(_fixture.RepackagedManifestPath)).IsEqualTo($"{RepackagedTeamsFixture.NewModuleName}.psd1");

        var binDir = Path.Combine(moduleDir, "bin");
        await Assert.That(Directory.Exists(binDir)).IsTrue().Because($"bin directory missing: {binDir}");
        await Assert.That(File.Exists(Path.Combine(binDir, "Svrooij.PowerShellRepackager.GenericLoader.dll"))).IsTrue().Because("Generic loader DLL missing from bin/");
        await Assert.That(File.Exists(Path.Combine(binDir, "loader-config.json"))).IsTrue().Because("loader-config.json missing from bin/");

        var dlls = Directory.GetFiles(binDir, "*.dll", SearchOption.AllDirectories);
        await Assert.That(dlls.Length > 1).IsTrue().Because("Expected bundled assemblies in bin/ besides the loader");

        var frameworkFolders = new[] { "net472", "net48", "net461", "net462", "desktop" };
        var offendingDirs = Directory.GetDirectories(moduleDir, "*", SearchOption.AllDirectories)
            .Where(d => frameworkFolders.Contains(Path.GetFileName(d), StringComparer.OrdinalIgnoreCase))
            .ToList();
        await Assert.That(offendingDirs.Count == 0).IsTrue();
    }

    [Test]
    public async Task RepackagedModule_ShouldHaveGenericLoaderAsNestedModule()
    {
        // Arrange
        var manifestContent = File.ReadAllText(_fixture.RepackagedManifestPath);

        // Assert
        await Assert.That(manifestContent).Contains("Svrooij.PowerShellRepackager.GenericLoader.dll");
        await Assert.That(manifestContent).Matches(@"NestedModules\s*=\s*@\(");

        var loaderConfig = File.ReadAllText(Path.Combine(_fixture.RepackagedModuleDirectory, "bin", "loader-config.json"));
        using var doc = JsonDocument.Parse(loaderConfig);
        var preload = doc.RootElement.GetProperty("preloadAssemblies");
        await Assert.That(preload.GetArrayLength() > 0).IsTrue().Because("loader-config.json should list assemblies to preload");
    }

    [Test]
    public async Task RepackagedModule_Pack_ShouldCreateValidNupkg()
    {
        // Assert package exists
        await Assert.That(string.IsNullOrWhiteSpace(_fixture.PackagePath)).IsFalse().Because($"Publish-RepackagedModule -Pack did not report a PackagePath.{Environment.NewLine}{_fixture.RepackageOutput}");
        await Assert.That(File.Exists(_fixture.PackagePath)).IsTrue().Because($"Package missing: {_fixture.PackagePath}");
        await Assert.That(Path.GetFileName(_fixture.PackagePath)).IsEqualTo($"{RepackagedTeamsFixture.NewModuleName}.{RepackagedTeamsFixture.OriginalModuleVersion}.nupkg");

        // Assert package structure
        using var archive = System.IO.Compression.ZipFile.OpenRead(_fixture.PackagePath!);
        var names = archive.Entries.Select(e => e.FullName).ToList();

        await Assert.That(names).Contains($"{RepackagedTeamsFixture.NewModuleName}.nuspec");
        await Assert.That(names).Contains("_rels/.rels");
        await Assert.That(names).Contains("[Content_Types].xml");
        await Assert.That(names.Count(n => string.Equals(n, "[Content_Types].xml", StringComparison.OrdinalIgnoreCase))).IsEqualTo(1);
        await Assert.That(names.Count(n => string.Equals(n, "_rels/.rels", StringComparison.OrdinalIgnoreCase))).IsEqualTo(1);
        await Assert.That(names.Count(n => string.Equals(n, $"{RepackagedTeamsFixture.NewModuleName}.nuspec", StringComparison.OrdinalIgnoreCase))).IsEqualTo(1);
        await Assert.That(names).Contains($"{RepackagedTeamsFixture.NewModuleName}.psd1");
        await Assert.That(names).Contains("bin/Svrooij.PowerShellRepackager.GenericLoader.dll");
        await Assert.That(names).Contains("bin/loader-config.json");

        // Assert nuspec metadata
        using var nuspecStream = archive.GetEntry($"{RepackagedTeamsFixture.NewModuleName}.nuspec")!.Open();
        var nuspec = System.Xml.Linq.XDocument.Load(nuspecStream);
        var ns = nuspec.Root!.Name.Namespace;
        var metadata = nuspec.Root.Element(ns + "metadata")!;

        await Assert.That(metadata.Element(ns + "id")!.Value).IsEqualTo(RepackagedTeamsFixture.NewModuleName);
        await Assert.That(metadata.Element(ns + "version")!.Value).IsEqualTo(RepackagedTeamsFixture.OriginalModuleVersion);
        await Assert.That(string.IsNullOrWhiteSpace(metadata.Element(ns + "description")?.Value)).IsFalse();
        await Assert.That(metadata.Element(ns + "tags")!.Value.Split(' ')).Contains("PSModule");
    }

    [Test]
    public async Task RepackagedModule_Publish_WithoutApiKey_ShouldReportError()
    {
        // Arrange - re-run publish against the already extracted package with no API key available
        var script = $$"""
            $env:PSGALLERY_API_KEY = $null
            $env:NUGET_API_KEY = $null
            Import-Module '{{_fixture.RepackagerManifestPath}}' -Force
            $package = Get-ModulePackage -ModuleName '{{RepackagedTeamsFixture.OriginalModuleName}}' -Version '{{RepackagedTeamsFixture.OriginalModuleVersion}}'
            $extracted = Expand-ModulePackage -PackagePath $package
            $result = Publish-RepackagedModule -ModuleInfo $extracted -NewModuleName 'Svrooij.NoKey' -OutputPath '{{_fixture.OutputPath}}' -Publish -ErrorVariable publishError -ErrorAction SilentlyContinue
            Write-Output "ERROR_ID=$($publishError.FullyQualifiedErrorId)"
            Write-Output "PUBLISHED=$($result.PublishedToFeed)"
            """;

        // Act
        var (_, stdout, stderr) = _fixture.RunPwsh(script);

        // Assert
        await Assert.That(stdout).Contains("ERROR_ID=PublishApiKeyMissing");
        await Assert.That(stdout + Environment.NewLine).Contains("PUBLISHED=" + Environment.NewLine);
    }
}