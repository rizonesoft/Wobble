# The one gate for this repository: build, unit tests and the TODO tree. CI runs this file, and so does every section.
#
#   pwsh scripts/check.ps1              build, unit tests, TODO validation
#   pwsh scripts/check.ps1 -EndToEnd    also the Windows end-to-end tests (after tools\attach.ps1, see README)
#
# Exits with the first failing step's code. Nothing here filters a tool's output.
param([switch]$EndToEnd)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath (Split-Path $PSScriptRoot)

function Step([string]$Name, [scriptblock]$Run) {
  "== $Name"
  & $Run
  if ($LASTEXITCODE -ne 0) { "FAILED: $Name (exit $LASTEXITCODE)"; exit $LASTEXITCODE }
}

$python = if (Get-Command python3 -ErrorAction Ignore) { 'python3' } else { 'python' }

Step 'build' { dotnet build Wobble.slnx --nologo -warnaserror }
Step 'unit tests' { dotnet test tests/Wobble.Tests --no-build --nologo }
Step 'todo validate' { & $python scripts/todo-graph.py validate }
Step 'plan is current' { & $python scripts/todo-graph.py plan --check }
if ($EndToEnd) {
  Step 'end-to-end tests' { dotnet test tests/Wobble.EndToEnd --no-build --nologo }
}
'all checks passed'
