# Read-only discovery. Respect the computer's configured Windows Update service (including WSUS).
$session = New-Object -ComObject Microsoft.Update.Session
$session.ClientApplicationID = 'Swap GPU'
$searcher = $session.CreateUpdateSearcher()
$searcher.Online = $true
$result = $searcher.Search("IsInstalled=0 and IsHidden=0 and Type='Driver'")
if ([int]$result.ResultCode -ne 2) { throw 'Windows Update did not complete the search successfully.' }
$offers = @()
foreach ($update in $result.Updates) {
    if ($update.DriverClass -ne 'Display' -or $update.IsBeta -or $update.InstallationBehavior.CanRequestUserInput) { continue }
    $offers += @{ Id=[string]$update.Identity.UpdateID; Revision=[int]$update.Identity.RevisionNumber;
        Title=[string]$update.Title; HardwareId=[string]$update.DriverHardwareID;
        Manufacturer=[string]$update.DriverManufacturer; Date=$update.DriverVerDate.ToUniversalTime().ToString('o');
        Bytes=[long]$update.MaxDownloadSize }
}
ConvertTo-Json -InputObject @($offers) -Depth 4 -Compress
