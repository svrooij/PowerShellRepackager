# PowerShellRepackager Tests

This directory contains comprehensive tests for the PowerShellRepackager project, validating that repackaged modules correctly export cmdlets and maintain module integrity.

## Test Projects

### Unit Tests: `ManifestFieldUpdateTests`

Fast tests that validate the manifest field update logic without requiring full module repackaging.

**Tests:**
- `UpdateField_WithArrayValue_ShouldNotAddExtraQuotes` - Validates that array literals like `@('...')` aren't wrapped in extra quotes
- `UpdateField_WithScalarValue_ShouldAddQuotes` - Ensures scalar values are properly quoted
- `UpdateField_ShouldNotMatchPartialFieldNames` - Validates exact field name matching (e.g., "Module" shouldn't match "NestedModules")
- `UpdateField_WhenFieldExists_ShouldReplaceValue` - Tests field value replacement
- `RemoveField_ShouldRemoveEntireFieldLine` - Validates field removal without leaving orphaned lines
- `UpdateField_ShouldNotMatchNestedFields` - Ensures nested PrivateData fields aren't accidentally updated
- `RealisticTeamsManifest_ShouldUpdateCorrectly` - End-to-end realistic scenario with Teams manifest

**Run:** 
```bash
dotnet test --filter "TypeName=PowerShellRepackager.Tests.ManifestFieldUpdateTests"
```

### Integration Tests: `CmdletExportTests`

Full integration tests that exercise the complete repackaging workflow. **Note:** These tests require:
- PowerShell 7.4+ installed and in PATH (or set `PWSH_PATH` environment variable)
- The repackaged tool build artifacts available
- MicrosoftTeams 7.9.0 available from PowerShell Gallery (downloaded automatically if needed)

**Tests:**
- `RepackagedModule_ShouldExportCmdlets` - Verifies that repackaged modules export cmdlets
- `RepackagedModule_ManifestShouldBeValid` - Validates manifest syntax is valid PowerShell
- `RepackagedModule_ShouldContainRequiredFiles` - Checks for required module files and structure
- `RepackagedModule_ShouldHaveGenericLoaderAsNestedModule` - Validates generic loader configuration

**Run:**
```bash
# Run all integration tests
dotnet test --filter "TypeName=PowerShellRepackager.Tests.CmdletExportTests"

# Run specific test
dotnet test --filter "Name=RepackagedModule_ShouldExportCmdlets"
```

## Running Tests

### Fast Unit Tests Only
```bash
cd tests/PowerShellRepackager.Tests
dotnet test --filter "TypeName~ManifestFieldUpdateTests"
```

### All Tests
```bash
cd tests/PowerShellRepackager.Tests
dotnet test
```

### Verbose Output
```bash
dotnet test --verbosity=detailed
```

## Environment Variables

For integration tests:
- `PWSH_PATH` - Override PowerShell 7 executable path (e.g., `/usr/bin/pwsh` or `C:\Program Files\PowerShell\7\pwsh.exe`)

Example:
```bash
export PWSH_PATH=/usr/bin/pwsh
dotnet test
```

## What's Being Tested

### Manifest Field Updates (Unit Tests)
- **Array vs. Scalar Values**: Ensures `@('...')` array literals aren't wrapped in extra quotes while scalar values like `'guid'` are
- **Field Name Precision**: Validates exact field matching using word boundaries (e.g., "NestedModules" ≠ "Module")
- **Field Replacement**: Confirms existing field values are updated correctly
- **Field Removal**: Tests that fields are cleanly removed without orphaned lines
- **Nested Structure**: Ensures root-level fields aren't confused with nested PrivateData fields

### Module Export (Integration Tests)
- **Cmdlet Discovery**: Validates that repackaged modules successfully export cmdlets
- **Manifest Validity**: Tests that generated manifests are syntactically valid PowerShell
- **File Structure**: Confirms all required files (manifest, .psm1 wrapper, bin/, loader DLL) are present
- **Generic Loader Integration**: Verifies generic loader is correctly configured as NestedModule

## Troubleshooting

### Tests Fail with "PowerShell not found"
Set the `PWSH_PATH` environment variable:
```bash
export PWSH_PATH=$(which pwsh)  # Linux/macOS
set PWSH_PATH=C:\Program Files\PowerShell\7\pwsh.exe  # Windows
```

### Tests Fail with "ModuleRepackager.dll not found"
Ensure the project is built:
```bash
dotnet build -c Release
```

### Integration Tests Timeout
The first run downloads MicrosoftTeams from NuGet. Increase timeout in `CmdletExportTests` if needed.

## CI/CD Integration

For continuous integration, run unit tests (fast, no external dependencies):
```yaml
- name: Run Unit Tests
  run: dotnet test --filter "TypeName~ManifestFieldUpdateTests" --verbosity=normal
```

For comprehensive testing (with PowerShell available):
```yaml
- name: Install PowerShell
  run: |
	# Ubuntu example
	sudo apt-get update
	sudo apt-get install -y powershell

- name: Run All Tests
  run: dotnet test --verbosity=normal
```

## Adding New Tests

1. Add test method to appropriate class (`ManifestFieldUpdateTests` for unit, `CmdletExportTests` for integration)
2. Use descriptive test names: `MethodBeingTested_Scenario_ExpectedOutcome`
3. Follow Arrange-Act-Assert pattern
4. Run locally before committing: `dotnet test`

## Future Enhancements

- [ ] Add tests for other module types (not just MicrosoftTeams)
- [ ] Add performance benchmarks for repackaging workflow
- [ ] Add tests for edge cases (malformed manifests, missing files, etc.)
- [ ] Add snapshot tests for generated manifest output
- [ ] Add tests for nested module dependencies
