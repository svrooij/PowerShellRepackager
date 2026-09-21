using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PowerShellRepackager.Tests;

/// <summary>
/// Unit tests for manifest field manipulation logic.
/// Tests the regex patterns used to update PowerShell manifest files.
/// </summary>
public class ManifestFieldUpdateTests
{
    /// <summary>
    /// Test that array fields are not wrapped in extra quotes.
    /// </summary>
    [Test]
    public async Task UpdateField_WithArrayValue_ShouldNotAddExtraQuotes()
    {
        // Arrange
        var manifest = @"@{
    VariablesToExport = @()
    RootModule = 'old.psm1'
}";
        var fieldName = "NestedModules";
        var value = "@('bin/Loader.dll')";

        // Act
        var result = UpdateManifestField(manifest, fieldName, value);

        // Assert
        await Assert.That(result).Contains("NestedModules = @('bin/Loader.dll')");
        await Assert.That(result).DoesNotContain("NestedModules = '@('bin/Loader.dll')'");
    }

    /// <summary>
    /// Test that scalar fields are wrapped in quotes.
    /// </summary>
    [Test]
    public async Task UpdateField_WithScalarValue_ShouldAddQuotes()
    {
        // Arrange
        var manifest = @"@{
    RootModule = 'old.psm1'
}";
        var fieldName = "RootModule";
        var value = "new.psm1";

        // Act
        var result = UpdateManifestField(manifest, fieldName, value);

        // Assert
        await Assert.That(result).Contains("RootModule = 'new.psm1'");
    }

    /// <summary>
    /// Test that field names are matched exactly, not partially.
    /// </summary>
    [Test]
    public async Task UpdateField_ShouldNotMatchPartialFieldNames()
    {
        // Arrange
        var manifest = @"@{
    VariablesToExport = @()
    Module = 'something'
}";
        var fieldName = "NestedModules"; // Should NOT match "Module"
        var value = "@('bin/Loader.dll')";

        // Act
        var result = UpdateManifestField(manifest, fieldName, value);

        // Assert - NestedModules should be added, Module should remain unchanged
        await Assert.That(result).Contains("NestedModules = @('bin/Loader.dll')");
        await Assert.That(result).Contains("Module = 'something'");
    }

    /// <summary>
    /// Test that existing field values are replaced correctly.
    /// </summary>
    [Test]
    public async Task UpdateField_WhenFieldExists_ShouldReplaceValue()
    {
        // Arrange
        var manifest = @"@{
    NestedModules = @('old.dll')
    RootModule = 'module.psm1'
}";
        var fieldName = "NestedModules";
        var value = "@('new.dll')";

        // Act
        var result = UpdateManifestField(manifest, fieldName, value);

        // Assert
        await Assert.That(result).Contains("NestedModules = @('new.dll')");
        await Assert.That(result).DoesNotContain("NestedModules = @('old.dll')");
    }

    /// <summary>
    /// Test that field removal works correctly and doesn't leave orphaned lines.
    /// </summary>
    [Test]
    public async Task RemoveField_ShouldRemoveEntireFieldLine()
    {
        // Arrange
        var manifest = @"@{
    ModuleName = 'TeamsPowerShell'
    RootModule = 'module.psm1'
    GUID = '12345678'
}";
        var fieldName = "ModuleName";

        // Act
        var result = RemoveManifestField(manifest, fieldName);

        // Assert
        await Assert.That(result).DoesNotContain("ModuleName");
        await Assert.That(result).Contains("RootModule = 'module.psm1'");
        await Assert.That(result).Contains("GUID = '12345678'");
    }

    /// <summary>
    /// Test that nested PrivateData fields are not accidentally matched.
    /// </summary>
    [Test]
    public async Task UpdateField_ShouldNotMatchNestedFields()
    {
        // Arrange
        var manifest = @"@{
    NestedModules = @()
    PrivateData = @{
        NestedModules = 'nested-value'
    }
}";
        var fieldName = "NestedModules";
        var value = "@('loader.dll')";

        // Act
        var result = UpdateManifestField(manifest, fieldName, value);

        // Assert - Only root level NestedModules should be updated
        var lines = result.Split('\n');
        var rootNestedModulesLine = lines.FirstOrDefault(l => l.TrimStart().StartsWith("NestedModules"));
        await Assert.That(rootNestedModulesLine).IsNotNull();
        await Assert.That(rootNestedModulesLine).Contains("@('loader.dll')");
    }

    /// <summary>
    /// Test realistic MicrosoftTeams manifest scenario.
    /// </summary>
    [Test]
    public async Task RealisticTeamsManifest_ShouldUpdateCorrectly()
    {
        // Arrange - Simulated Teams manifest
        var manifest = @"@{
    RootModule = 'MicrosoftTeams.psm1'
    ModuleVersion = '7.9.0'
    GUID = '82b0bf19-c5cd-4c30-8db4-b458a4b84495'
    Author = 'Microsoft Corporation'
    VariablesToExport = @()
    CmdletsToExport = '*'
    FunctionsToExport = '*'
    PrivateData = @{
        PSData = @{
            Prerelease = 'preview'
        }
    }
}";

        // Act - Update multiple fields
        var result = manifest;
        result = UpdateManifestField(result, "ModuleName", "Teams");
        result = UpdateManifestField(result, "GUID", "new-guid-12345");
        result = UpdateManifestField(result, "RootModule", "RepackagedModule.psm1");
        result = UpdateManifestField(result, "NestedModules", "@('bin/Loader.dll')");
        result = RemoveManifestField(result, "ModuleName"); // ModuleName should have been removed

        // Assert
        await Assert.That(result).Contains("RootModule = 'RepackagedModule.psm1'");
        await Assert.That(result).Contains("GUID = 'new-guid-12345'");
        await Assert.That(result).Contains("NestedModules = @('bin/Loader.dll')");
        await Assert.That(result).DoesNotContain("VariablesToExport = 'Teams'"); // ModuleName was removed, not VariablesToExport
        await Assert.That(result).Contains("CmdletsToExport = '*'"); // Other fields unchanged
    }

    // ===== Helper Methods (mirroring the actual implementation) =====

    /// <summary>
    /// Test that commented-out fields are not matched, so the field is added exactly once.
    /// </summary>
    [Test]
    public async Task UpdateField_ShouldIgnoreCommentedLines_AndAddFieldOnce()
    {
        // Arrange
        var manifest = @"@{
    RootModule = 'old.psm1'
    # NestedModules = @()
    #NestedModules = @()
}";

        // Act
        var result = UpdateManifestField(manifest, "NestedModules", "@('bin/Loader.dll')");

        // Assert
        var uncommentedMatches = Regex.Matches(result, @"^\s*NestedModules\s*=", RegexOptions.Multiline);
        await Assert.That(uncommentedMatches).HasSingleItem();
        await Assert.That(result).Contains("# NestedModules = @()");
        await Assert.That(result).Contains("#NestedModules = @()");
    }

    private static string UpdateManifestField(string manifestContent, string fieldName, string value)
    {
        var isArrayLiteral = value.StartsWith("@");
        var quotedValue = isArrayLiteral ? value : $"'{value}'";

        var singleLinePattern = $"(?m)^(?!\\s*#)\\s*{Regex.Escape(fieldName)}\\s*=\\s*[^\r\n]*";
        var singleLineReplacement = $"    {fieldName} = {quotedValue}";

        var regex = new Regex(singleLinePattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
        var result = regex.Replace(manifestContent, singleLineReplacement, 1);

        if (result == manifestContent)
        {
            var index = manifestContent.IndexOf("@{", StringComparison.Ordinal);
            result = index == -1
                ? manifestContent
                : manifestContent.Insert(index + 2, $"\r\n    {fieldName} = {quotedValue}");
        }

        return result;
    }

    private static string RemoveManifestField(string manifestContent, string fieldName)
    {
        var pattern = $"(?m)^(?!\\s*#)\\s*{Regex.Escape(fieldName)}\\s*=\\s*[^\r\n]*\\r?\\n?";

        var result = Regex.Replace(
            manifestContent, pattern, "",
            RegexOptions.IgnoreCase | RegexOptions.Multiline);

        return result;
    }
}