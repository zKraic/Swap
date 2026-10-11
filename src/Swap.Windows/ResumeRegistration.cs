using Microsoft.Win32;
using System.Diagnostics;
using System.Text.Json;
namespace Swap.Windows;

// KraicDev. HKCU RunOnce uses normal user startup; elevation still goes through Windows UAC.
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
        if(FileVersionInfo.GetVersionInfo(executable).CompanyName!="KraicDev")throw new IOException("El ejecutable no corresponde a Swap.");
        Directory.CreateDirectory(DirectoryPath);
        if((File.GetAttributes(DirectoryPath)&FileAttributes.ReparsePoint)!=0)throw new IOException("La carpeta de continuación no puede ser un enlace.");
        var cached=Path.Combine(DirectoryPath,"Swap.exe");
        using var source=new FileStream(executable,FileMode.Open,FileAccess.Read,FileShare.Read);
        var hash=Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(source));source.Position=0;
        if(!executable.Equals(cached,StringComparison.OrdinalIgnoreCase))
        {
            using(var output=new FileStream(cached+".tmp",FileMode.Create,FileAccess.Write,FileShare.None))await source.CopyToAsync(output);
            File.Move(cached+".tmp",cached,true);
        }
        var pending=new Pending(session,parent.Parent!.FullName,hash);
        File.WriteAllText(Marker+".tmp",JsonSerializer.Serialize(pending));File.Move(Marker+".tmp",Marker,true);
        using var key=Registry.CurrentUser.CreateSubKey(RunOnce);
        key.SetValue(ValueName,'"'+cached+"\" --continue",RegistryValueKind.String);
    }
    public static void Clear()
    {
        using var key=Registry.CurrentUser.OpenSubKey(RunOnce,true);key?.DeleteValue(ValueName,false);
        if(File.Exists(Marker))File.Delete(Marker);
        // Keep the small cache: an executable cannot be deleted while it is running.
    }
}
