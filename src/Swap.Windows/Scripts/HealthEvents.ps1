# KraicDev. Recent graphics incidents; do not export event messages, usernames, paths or report identifiers.
$incidents=@(); $complete=$true
foreach($query in @(
    @{LogName='System';ProviderName='Display';Id=4101;StartTime=(Get-Date).AddDays(-7)},
    @{LogName='Application';ProviderName='Windows Error Reporting';Id=1001;StartTime=(Get-Date).AddDays(-7)})) {
    try {
        $events=@(Get-WinEvent -FilterHashtable $query -MaxEvents 100 -ErrorAction Stop)
        foreach($event in $events) {
            $kind='TDR'
            if($event.Id -eq 1001) {
                $xml=[xml]$event.ToXml(); $data=@{}
                foreach($item in $xml.Event.EventData.Data){$data[[string]$item.Name]=[string]$item.'#text'}
                if($data['EventName'] -ne 'LiveKernelEvent' -or $data['P1'] -notin '117','141','142'){continue}
                $kind='LiveKernelEvent '+$data['P1']
            }
            $incidents+=@{Time=$event.TimeCreated.ToUniversalTime().ToString('o');Provider=[string]$event.ProviderName;Id=[int]$event.Id;Kind=$kind}
        }
    } catch {
        if($_.FullyQualifiedErrorId -notlike 'NoMatchingEventsFound*'){$complete=$false}
    }
}
# PCI Express link width of connected GPUs: a card in a reduced slot or not fully seated shows fewer lanes.
$links=@()
try {
    foreach($device in @(Get-PnpDevice -Class Display -PresentOnly -ErrorAction Stop)) {
        $p=@{}
        foreach($x in @(Get-PnpDeviceProperty -InstanceId $device.InstanceId -KeyName 'DEVPKEY_PciDevice_CurrentLinkWidth','DEVPKEY_PciDevice_MaxLinkWidth','DEVPKEY_PciDevice_CurrentLinkSpeed','DEVPKEY_PciDevice_MaxLinkSpeed' -ErrorAction SilentlyContinue)) { if($null -ne $x.Data){$p[[string]$x.KeyName]=[int]$x.Data} }
        if($p['DEVPKEY_PciDevice_MaxLinkWidth'] -gt 0){$links+=@{AdapterId=[string]$device.InstanceId;Width=[int]$p['DEVPKEY_PciDevice_CurrentLinkWidth'];MaxWidth=[int]$p['DEVPKEY_PciDevice_MaxLinkWidth'];Speed=[int]$p['DEVPKEY_PciDevice_CurrentLinkSpeed'];MaxSpeed=[int]$p['DEVPKEY_PciDevice_MaxLinkSpeed']}}
    }
} catch { }
@{Incidents=@($incidents|Sort-Object {[string]$_.Time} -Descending|Select-Object -First 100);Complete=$complete;Links=@($links)}|ConvertTo-Json -Depth 5 -Compress
