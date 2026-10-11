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
@{Incidents=@($incidents|Sort-Object Time -Descending|Select-Object -First 100);Complete=$complete}|ConvertTo-Json -Depth 5 -Compress
