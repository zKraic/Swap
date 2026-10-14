using Microsoft.Win32;
using System.Diagnostics;
using System.Text.Json;
namespace Swap.Windows;

// KraicDev. A sign-in task continues elevated after a restart; HKCU RunOnce with UAC remains the fallback.
public static class ResumeRegistration
{
    private const string RunOnce=@"Software\Microsoft\Windows\CurrentVersion\RunOnce";
    private const string ValueName="Swap-KraicDev-Continue";
    private static string DirectoryPath=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Swap","Continuation");
    private static string Marker=>Path.Combine(DirectoryPath,"pending.json");
    public sealed record Pending(string Session, string DataRoot, string ExeSha256);
    public static Pending? Load()
    {
        if(!File.Exists(Marker))return null;
        if(new FileInfo(Marker).Length>16384)throw new IOException("La continuación guardada no es válida.");
        var pending=JsonSerializer.Deserialize<Pending>(File.ReadAllText(Marker)) ?? throw new IOException("Continuación vacía.");
        var sessions=Path.GetFullPath(Path.Combine(pending.DataRoot,"Sessions"))+Path.DirectorySeparatorChar;
        if(!Path.GetFullPath(pending.Session).StartsWith(sessions,StringComparison.OrdinalIgnoreCase) || pending.ExeSha256.Length!=64 || pending.ExeSha256.Any(c=>!Uri.IsHexDigit(c)))throw new IOException("Ruta o checksum de continuación inválido.");
        return pending;
    }
    public static async Task ArmAsync(string executable,string session)
    {
        executable=Path.GetFullPath(executable);session=Path.GetFullPath(session);
        var parent=Directory.GetParent(session);
        if(parent?.Name!="Sessions" || !File.Exists(Path.Combine(session,"state.json")))throw new IOException("Sesión de continuación inválida.");
        var (cached,hash)=await CacheAsync(executable);
        var pending=new Pending(session,parent.Parent!.FullName,hash);
        File.WriteAllText(Marker+".tmp",JsonSerializer.Serialize(pending));File.Move(Marker+".tmp",Marker,true);
        var task=await LogonTask.RegisterAsync(LogonTask.Continue,executable,Argument);
        using var key=Registry.CurrentUser.CreateSubKey(RunOnce);
        key.SetValue(ValueName,'"'+cached+"\" "+Argument+(task?" "+LogonTask.FallbackArgument:""),RegistryValueKind.String);
    }
    public const string Argument="--continue";
    // A local copy keeps working when the original EXE was on a removable drive.
    internal static async Task<(string Cached,string Hash)> CacheAsync(string executable)
    {
        executable=Path.GetFullPath(executable);
        if(FileVersionInfo.GetVersionInfo(executable).CompanyName!="KraicDev")throw new IOException("El ejecutable no corresponde a Swap.");
        Directory.CreateDirectory(DirectoryPath);
        if((File.GetAttributes(DirectoryPath)&FileAttributes.ReparsePoint)!=0)throw new IOException("La carpeta de continuación no puede ser un enlace.");
        var cached=Path.Combine(DirectoryPath,"Swap.exe");
        using var source=new FileStream(executable,FileMode.Open,FileAccess.Read,FileShare.Read);
        var hash=Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(source));source.Position=0;
        // The same copy may be running (opened at sign-in); it is already the right one.
        if(!executable.Equals(cached,StringComparison.OrdinalIgnoreCase) && !(File.Exists(cached) && await WindowsPlatform.HashFileAsync(cached)==hash))
        {
            using(var output=new FileStream(cached+".tmp",FileMode.Create,FileAccess.Write,FileShare.None))await source.CopyToAsync(output);
            File.Move(cached+".tmp",cached,true);
        }
        return (cached,hash);
    }
    internal static string Folder=>DirectoryPath;
    public static void Clear()
    {
        using(var key=Registry.CurrentUser.OpenSubKey(RunOnce,true))key?.DeleteValue(ValueName,false);
        LogonTask.Delete(LogonTask.Continue);
        if(File.Exists(Marker))File.Delete(Marker);
        // Keep the small cache: an executable cannot be deleted while it is running.
    }
}
