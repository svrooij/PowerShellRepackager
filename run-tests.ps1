#!/usr/bin/env pwsh
<#
.SYNOPSIS
	Quick test runner for PowerShellRepackager test suite.

.DESCRIPTION
	Runs unit tests (fast) or all tests (requires PowerShell 7.4+).
	Provides colorized output and quick validation.

.PARAMETER All
	Run all tests including integration tests (requires PowerShell 7.4+).
	Default: false (runs unit tests only).

.PARAMETER Verbose
	Show detailed output from tests.

.PARAMETER Quiet
	Minimal output, suitable for CI/CD.

.EXAMPLE
	# Run fast unit tests only
	.\run-tests.ps1

	# Run all tests with verbose output
	.\run-tests.ps1 -All -Verbose

	# Run all tests with minimal output
	.\run-tests.ps1 -All -Quiet

.NOTES
	For integration tests (-All), requires:
	- PowerShell 7.4 or later
	- Access to PowerShell Gallery (for module download)
	- Approximately 15-30 seconds to complete
#>

param(
	[switch]$All,
	[switch]$Verbose,
	[switch]$Quiet
)

$ErrorActionPreference = 'Stop'

# Colors for output
$colors = @{
	Success = 'Green'
	Warning = 'Yellow'
	Error   = 'Red'
	Info    = 'Cyan'
}

function Write-TestHeader {
	param([string]$Message)
	Write-Host "`n" + ("=" * 60) -ForegroundColor $colors.Info
	Write-Host $Message -ForegroundColor $colors.Info
	Write-Host ("=" * 60) -ForegroundColor $colors.Info
}

function Write-TestResult {
	param([string]$Message, [ValidateSet('Success', 'Warning', 'Error', 'Info')]$Type = 'Info')
	Write-Host $Message -ForegroundColor $colors[$Type]
}

Write-TestHeader "PowerShellRepackager Test Suite"

$repoRoot = Split-Path -Parent $PSCommandPath
$testProject = Join-Path $repoRoot "tests/PowerShellRepackager.Tests"

if (-not (Test-Path $testProject)) {
	Write-TestResult "Test project not found at $testProject" Error
	exit 1
}

Write-TestResult "Repository: $repoRoot" Info
Write-TestResult "Test Project: $testProject" Info

# Build if needed
Write-TestHeader "Building Test Project"
Push-Location $repoRoot
try {
	$buildArgs = @('build', $testProject, '-c', 'Debug', '--verbosity', 'quiet')
	if ($Quiet) { $buildArgs += '--nologo' }

	& dotnet $buildArgs
	if ($LASTEXITCODE -ne 0) {
		Write-TestResult "Build failed!" Error
		exit 1
	}
	Write-TestResult "✓ Build successful" Success
}
finally {
	Pop-Location
}

# Determine which tests to run
if ($All) {
	Write-TestHeader "Running All Tests (Unit + Integration)"
	Write-TestResult "Note: Integration tests require PowerShell 7.4+" Warning

	# Check PowerShell availability
	$pwsh = Get-Command pwsh -ErrorAction SilentlyContinue
	if ($pwsh) {
		Write-TestResult "✓ PowerShell 7.4+ found at: $($pwsh.Source)" Success
		$env:PWSH_PATH = $pwsh.Source
	}
	else {
		Write-TestResult "⚠ PowerShell 7.4+ not found in PATH" Warning
		Write-TestResult "   Set PWSH_PATH environment variable if installed in non-standard location" Info
	}

	$filter = $null  # Run all tests
}
else {
	Write-TestHeader "Running Unit Tests Only (Fast)"
	$filter = "--filter `"TypeName=PowerShellRepackager.Tests.ManifestFieldUpdateTests`""
}

# Build test command
$testArgs = @('test', $testProject, '-c', 'Debug')

if ($filter) {
	Write-TestResult "Filter: Manifest field update tests" Info
	$testArgs += $filter
}

if ($Verbose) {
	$testArgs += '--verbosity', 'detailed'
}
elseif ($Quiet) {
	$testArgs += '--verbosity', 'quiet'
	$testArgs += '--logger', 'console;verbosity=minimal'
}
else {
	$testArgs += '--verbosity', 'normal'
}

# Run tests
Push-Location $repoRoot
try {
	Write-Host "`n"
	& dotnet $testArgs
	$testResult = $LASTEXITCODE
}
finally {
	Pop-Location
}

# Summary
Write-TestHeader "Test Summary"

if ($testResult -eq 0) {
	Write-TestResult "✓ All tests passed!" Success
	if (-not $All) {
		Write-TestResult "Tip: Run with -All flag to include integration tests" Info
	}
	exit 0
}
else {
	Write-TestResult "✗ Some tests failed" Error
	Write-TestResult "Run with -Verbose flag for detailed output" Info
	exit 1
}
