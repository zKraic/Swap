using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Swap.Core;
using Swap.Windows;
namespace Swap.App;

// KraicDev. Keep checkpoints separate from backups and never use a diagnostic finding to delete a driver.
public partial class MainWindow
{
    private GraphicsDiagnostics? graphicsDiagnostics;
    private GraphicsCheckpoint? checkpoint;
    private GraphicsCheckpoint? beforeOperation;
    private string CheckpointPath => Path.Combine(dataRoot,"verified-state.json");
    private static string Describe(HealthFinding finding) => T(finding.Code)+(string.IsNullOrEmpty(finding.Detail)?"":" · "+finding.Detail);
    private async Task InspectGraphicsAsync()
    {
        if(snapshot is null)return;
        graphicsDiagnostics=Demo ? TargetAdapter is {} adapter ? GraphicsInspector.Demo(adapter) : new GraphicsDiagnostics(DateTimeOffset.UtcNow,[],[],[],true,true,[]) : await GraphicsInspector.ReadAsync();
        if(Demo && demoHz is int hz) graphicsDiagnostics=graphicsDiagnostics with {Displays=graphicsDiagnostics.Displays.Select(d=>d with {Hz=hz}).ToArray()};
        checkpoint=null;
        if(File.Exists(CheckpointPath))
        {
            try
            {
                if(new FileInfo(CheckpointPath).Length>1024*1024)throw new IOException("Checkpoint too large.");
                checkpoint=JsonSerializer.Deserialize<GraphicsCheckpoint>(await File.ReadAllTextAsync(CheckpointPath));
            }
            catch(Exception e){Log(T("checkpointReadError")+" · "+e.GetType().Name);}
        }
        if(checkpoint is null && !File.Exists(CheckpointPath) && snapshot.Adapters.Count>0 && snapshot.Adapters.All(a=>a.Problem==0 && a.Signed && Planner.IsOem(a.Inf)))
            await SaveCheckpointCoreAsync();
        PaintGraphics();
    }
    // Direct3D 12, Vulkan and OpenCL as games see them, asked from a separate Swap process.
    private ApiReport? apiReport;
    private async Task ProbeApisAsync()
    {
        if(snapshot is null)return;
        apiReport=Demo ? TargetAdapter is {} adapter ? ApiProbe.Demo(adapter) : null : await ApiProbe.RunAsync(Environment.ProcessPath!);
        if(apiReport is {Completed:false})Log(T("apiProbeFailed")+" · "+string.Join("; ",apiReport.Errors));
        PaintGraphics();
    }
    private IReadOnlyList<HealthFinding> GameFindings(Adapter? target)
    {
        if(snapshot is null || apiReport is null || target is null)return [];
        var connected=snapshot.Adapters.Select(a=>a.Vendor).Where(v=>v!=Vendor.Unknown).ToHashSet();
        return GameReadiness.Evaluate(target,apiReport,connected);
    }
    private void PaintGames()
    {
        var findings=GameFindings(TargetAdapter);
        GamesText.Text=TargetAdapter is null ? T("selectGpu") : findings.Count==0 ? T("gamesUnknown") : string.Join("\n",findings.Select(Describe));
        if(findings.Any(f=>f.Attention) && !IsAuditRun)GamesDisclosure.IsExpanded=true;
    }
    private void PaintGraphics()
    {
        if(snapshot is null || graphicsDiagnostics is null)return;
        var findings=GraphicsHealth.Evaluate(TargetAdapter,graphicsDiagnostics);
        HealthText.Text=TargetAdapter is null && snapshot.Adapters.Count>1 ? T("selectGpu") : string.Join("\n",findings.Select(Describe));
        var games=GameFindings(TargetAdapter);
        GpuHealthSummary.Text=TargetAdapter is null ? T(snapshot.Adapters.Count>1?"selectGpu":"noGpu") : T(TargetAdapter.Problem==0?"pnpOk":"pnpError")+(TargetAdapter.Problem is >0 ? " "+TargetAdapter.Problem:"")+
            (games.Count==0 ? "" : " · "+T(games.Any(f=>f.Attention)?"gamesAttention":"gamesReady"));
        PaintGames();
        PaintRefresh();
        PaintSoftware();
        DisplayText.Text=graphicsDiagnostics.Displays.Count==0?T("displayUnknown"):string.Join("\n",graphicsDiagnostics.Displays.Select(d=>$"{d.Name} · {d.Width}×{d.Height} · {(d.Hz>1?d.Hz+" Hz":T("unknown"))}"+(d.MaximumHz>d.Hz+1 && d.Hz>1?$" · {T("maximumHz")} {d.MaximumHz} Hz":"")));
        EventText.Text=!graphicsDiagnostics.EventsComplete?T("eventsUnknown"):graphicsDiagnostics.Incidents.Count==0?T("noDisplayErrors"):string.Join("\n",graphicsDiagnostics.Incidents.Take(8).Select(e=>$"{e.Time.LocalDateTime:g} · {e.Kind} · {e.Provider} ({e.Id})"));
        if(checkpoint is null)ComparisonText.Text=T("checkpointNone");
        else
        {
            try {ComparisonText.Text=string.Join("\n",GraphicsHealth.Compare(checkpoint,snapshot,graphicsDiagnostics,Demo).Select(Describe));}
            catch(InvalidOperationException){ComparisonText.Text=T("checkpointOtherMachine");}
        }
    }
    private async Task SaveCheckpointCoreAsync()
    {
        if(snapshot is null || graphicsDiagnostics is null)return;
        var captured=GraphicsHealth.Capture(snapshot,graphicsDiagnostics,Demo);
        try
        {
            EnsureWritable(dataRoot);var path=CheckpointPath;
            await File.WriteAllTextAsync(path+".tmp",JsonSerializer.Serialize(captured));File.Move(path+".tmp",path,true);checkpoint=captured;
        }
        catch(Exception e){Log(T("checkpointReadError")+" · "+e.GetType().Name);}
    }
    private async void RefreshHealth(object sender,RoutedEventArgs e) => await Guard(AnalyzeAsync);
    private async void SaveCheckpoint(object sender,RoutedEventArgs e)
    {
        if(busy)return;
        if(MessageBox.Show(this,T("checkpointConfirm"),"Swap",MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)!=MessageBoxResult.Yes)return;
        await Guard(async ()=>{await AnalyzeAsync();await SaveCheckpointCoreAsync();PaintGraphics();});
    }
    private void ViewHealth(object sender,RoutedEventArgs e){DiagnosticsNav.IsChecked=true;}
    private void ViewRecovery(object sender,RoutedEventArgs e){RecoveryNav.IsChecked=true;}
    private async Task SaveVerifiedCheckpointAsync(bool verified)
    {
        if(!verified)return;
        // Show the comparison with the previous baseline before accepting the new verified driver.
        PaintGraphics();
        var reference=beforeOperation ?? checkpoint;
        if(reference is not null && snapshot is not null && graphicsDiagnostics is not null)
        {
            try
            {
                var comparison=GraphicsHealth.Compare(reference,snapshot,graphicsDiagnostics,Demo);
                await File.WriteAllTextAsync(Path.Combine(dataRoot,"last-comparison.json"),JsonSerializer.Serialize(comparison));
                LastComparisonText.Text=string.Join("\n",comparison.Select(Describe));LastComparisonPanel.Visibility=Visibility.Visible;
            }
            catch(Exception e){Log(T("checkpointReadError")+" · "+e.GetType().Name);}
        }
        beforeOperation=null;await SaveCheckpointCoreAsync();PaintGraphics();
    }

    // After a GPU change Windows often falls back to 60 Hz. Offer the highest rate the display reports, then confirm or revert.
    private int? demoHz;
    private (string Key,int Previous,int Applied)? refreshPending;
    private DispatcherTimer? refreshTimer; private int refreshSeconds;
    private void PaintRefresh()
    {
        RefreshPanel.Children.Clear();
        foreach(var display in graphicsDiagnostics?.Displays.Where(d=>d.Hz>1 && d.MaximumHz>d.Hz+1) ?? [])
        {
            var button=new Button{Content=string.Format(T("refreshApply"),display.MaximumHz)+" · "+display.Name,Tag=display,Margin=new Thickness(0,0,8,8)};
            button.Click+=ApplyRefresh; RefreshPanel.Children.Add(button);
        }
        RefreshPanel.Visibility=RefreshPanel.Children.Count>0?Visibility.Visible:Visibility.Collapsed;
        if(RefreshPanel.Children.Count>0 && !IsAuditRun) DisplayDisclosure.IsExpanded=true;
    }
    private bool IsAuditRun => Environment.GetCommandLineArgs().Any(a=>a.StartsWith("--ui-audit="));
    private async void ApplyRefresh(object sender,RoutedEventArgs e)
    {
        if(busy || refreshPending is not null || (sender as Button)?.Tag is not DisplayOutput display)return;
        try
        {
            int previous;
            if(Demo){previous=display.Hz;demoHz=display.MaximumHz;}
            else previous=await Task.Run(()=>GraphicsInspector.SetRefresh(display.Key,display.MaximumHz));
            refreshPending=(display.Key,previous,display.MaximumHz); refreshSeconds=15;
            RefreshConfirm.Visibility=Visibility.Visible; RefreshPanel.Visibility=Visibility.Collapsed; RefreshAsk.Text=string.Format(T("refreshAsk"),refreshSeconds);
            refreshTimer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};
            refreshTimer.Tick+=async (_,_)=>{ if(--refreshSeconds<=0) await FinishRefreshAsync(false); else RefreshAsk.Text=string.Format(T("refreshAsk"),refreshSeconds); };
            refreshTimer.Start();
        }
        catch(Exception ex) when (ex is IOException or InvalidOperationException){Status.Text=T("refreshFailed");Log(ex.Message);}
    }
    private async void KeepRefresh(object sender,RoutedEventArgs e)=>await FinishRefreshAsync(true);
    private async void RevertRefresh(object sender,RoutedEventArgs e)=>await FinishRefreshAsync(false);
    private async Task FinishRefreshAsync(bool keep)
    {
        if(refreshPending is not {} pending)return;
        refreshTimer?.Stop(); refreshTimer=null; refreshPending=null; RefreshConfirm.Visibility=Visibility.Collapsed;
        try
        {
            if(!keep){ if(Demo)demoHz=pending.Previous; else await Task.Run(()=>GraphicsInspector.SetRefresh(pending.Key,pending.Previous)); }
            Status.Text=keep?string.Format(T("refreshApplied"),pending.Applied):T("refreshReverted");
        }
        catch(Exception ex) when (ex is IOException or InvalidOperationException){Status.Text=T("refreshFailed");Log(ex.Message);}
        if(!busy){await InspectGraphicsAsync();}
    }
}