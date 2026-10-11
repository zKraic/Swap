# KraicDev - enables only Microsoft's disposable test VM; never restarts the host.
param([string]$ResultPath = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/vm/installation.json'))
$ErrorActionPreference = 'Stop'
$ResultPath = [IO.Path]::GetFullPath($ResultPath)
[void][IO.Directory]::CreateDirectory((Split-Path $ResultPath -Parent))
$report = [ordered]@{ author='KraicDev'; started=(Get-Date).ToUniversalTime().ToString('o'); feature='Containers-DisposableClientVM'; phase='starting'; rebootRequired=$false; automaticReboot=$false }
function Save-Installation { $report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $ResultPath -Encoding UTF8 }
try {
    $principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
    if (!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Administrator approval is required to enable Windows Sandbox.' }
    Save-Installation
    $feature = Get-WindowsOptionalFeature -Online -FeatureName $report.feature
    $report.before = [string]$feature.State
    $report.phase = 'enabling'; Save-Installation
    if ($feature.State -eq 'EnablePending' -or $feature.State -eq 'DisablePending') {
        $report.rebootRequired = $true
    } elseif ($feature.State -ne 'Enabled') {
        $operation = Enable-WindowsOptionalFeature -Online -FeatureName $report.feature -All -NoRestart
        $report.rebootRequired = [bool]$operation.RestartNeeded
    }
    $report.after = [string](Get-WindowsOptionalFeature -Online -FeatureName $report.feature).State
    $report.rebootRequired = $report.rebootRequired -or $report.after -eq 'EnablePending'
    $report.sandboxExecutable = Test-Path -LiteralPath (Join-Path $env:SystemRoot 'System32/WindowsSandbox.exe')
    $report.phase = if ($report.rebootRequired) { 'restart-required' } elseif ($report.after -eq 'Enabled') { 'installed' } else { 'needs-review' }
    $report.finished = (Get-Date).ToUniversalTime().ToString('o'); Save-Installation
} catch {
    $report.phase = 'failed'; $report.error = $_.Exception.Message
    $report.finished = (Get-Date).ToUniversalTime().ToString('o'); Save-Installation
    exit 1
}
