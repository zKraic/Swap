# Selection comes from the in-memory discovery result, never from a user-controlled file or URL.
$request = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('__REQUEST__')) | ConvertFrom-Json
$device = Get-PnpDevice -Class Display -PresentOnly | Where-Object InstanceId -eq $request.AdapterId
if (@($device).Count -ne 1) { throw 'The selected GPU is no longer present.' }
$ids = @((Get-PnpDeviceProperty -InstanceId $request.AdapterId -KeyName DEVPKEY_Device_HardwareIds).Data)
$ids += @((Get-PnpDeviceProperty -InstanceId $request.AdapterId -KeyName DEVPKEY_Device_CompatibleIds).Data)
if ($request.HardwareId -notin $ids) { throw 'The Windows Update hardware ID does not match this GPU.' }
$session = New-Object -ComObject Microsoft.Update.Session
$session.ClientApplicationID = 'Swap GPU'
$searcher = $session.CreateUpdateSearcher()
$searcher.Online = $true
$result = $searcher.Search("UpdateID='$($request.Id)' and RevisionNumber=$($request.Revision) and IsInstalled=0 and IsHidden=0 and Type='Driver'")
if ([int]$result.ResultCode -ne 2 -or $result.Updates.Count -ne 1) { throw 'The selected driver is no longer available. Search again.' }
$update = $result.Updates.Item(0)
if ($update.DriverClass -ne 'Display' -or $update.DriverHardwareID -ne $request.HardwareId -or $update.Title -ne $request.Title -or $update.DriverManufacturer -ne $request.Manufacturer -or $update.IsBeta -or $update.InstallationBehavior.CanRequestUserInput) { throw 'Driver metadata changed or needs interactive installation.' }
if (!$update.EulaAccepted) { $update.AcceptEula() }
$collection = New-Object -ComObject Microsoft.Update.UpdateColl
[void]$collection.Add($update)
if (!$update.IsDownloaded) {
    $downloader = $session.CreateUpdateDownloader()
    $downloader.Updates = $collection
    $download = $downloader.Download()
    if ([int]$download.ResultCode -ne 2 -or [int]$download.GetUpdateResult(0).ResultCode -ne 2 -or !$update.IsDownloaded) {
        throw "Driver download failed. HRESULT: $($download.GetUpdateResult(0).HResult)"
    }
}
if ($request.DownloadOnly) { @{ Code=0; Output='Windows Update downloaded and validated the driver in the Windows cache.' } | ConvertTo-Json -Compress; return }
# A download can take a long time. Recheck the physical target immediately before installation.
$device = @(Get-PnpDevice -Class Display -PresentOnly | Where-Object InstanceId -eq $request.AdapterId)
if ($device.Count -ne 1) { throw 'The selected GPU changed during download. No installation was started.' }
$ids = @((Get-PnpDeviceProperty -InstanceId $request.AdapterId -KeyName DEVPKEY_Device_HardwareIds).Data)
$ids += @((Get-PnpDeviceProperty -InstanceId $request.AdapterId -KeyName DEVPKEY_Device_CompatibleIds).Data)
if ($request.HardwareId -notin $ids) { throw 'The GPU hardware IDs changed during download. No installation was started.' }
$installer = $session.CreateUpdateInstaller()
if ($installer.IsBusy -or $installer.RebootRequiredBeforeInstallation) { throw 'Windows Update is busy or requires a restart.' }
$installer.Updates = $collection
$installer.AllowSourcePrompts = $false
$installer.ForceQuiet = $true
$installation = $installer.Install()
$item = $installation.GetUpdateResult(0)
if ([int]$installation.ResultCode -ne 2 -or [int]$item.ResultCode -ne 2) { throw "Driver installation failed. HRESULT: $($item.HResult)" }
$code = if ($installation.RebootRequired -or $item.RebootRequired) { 3010 } else { 0 }
@{ Code=$code; Output="Windows Update installation completed. HRESULT: $($item.HResult)" } | ConvertTo-Json -Compress
