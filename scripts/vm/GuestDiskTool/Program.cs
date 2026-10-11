// KraicDev. Offline access to the dedicated disposable VM image only.
using DiscUtils.Ntfs;
using DiscUtils.Registry;
using DiscUtils.Vhd;
if (args.Length < 2) throw new ArgumentException("Usage: GuestDiskTool private.vhd inspect|prepare [bundle] [qemu-directory]");
var path = Path.GetFullPath(args[0]);
if (!path.StartsWith(@"D:\KraicDev\Swap-VM\", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(path) is not (".vhd" or ".img")) throw new InvalidOperationException("Only a private VHD or raw image in the VM lab is permitted.");
var writable = args[1] is "prepare" or "copy-helpers" or "renew-login";
if (System.Diagnostics.Process.GetProcessesByName("qemu-system-x86_64").Length > 0) throw new InvalidOperationException("Stop QEMU before offline virtual disk access.");
using DiscUtils.VirtualDisk disk = Path.GetExtension(path)==".img"
    ? new DiscUtils.Raw.Disk(path, writable ? FileAccess.ReadWrite : FileAccess.Read)
    : new Disk(path, writable ? FileAccess.ReadWrite : FileAccess.Read);
var partitions = disk.Partitions.Partitions;
if (partitions.Count != 2) throw new InvalidOperationException("Expected the two-partition private Windows guest.");
using var volume = partitions[1].Open();
using var fs = new NtfsFileSystem(volume);
if (!fs.FileExists(@"Windows\System32\config\SYSTEM")) throw new InvalidOperationException("Windows guest not found.");
if (args[1] == "renew-login") {
    // KraicDev: restore the existing disposable lab account, never reset a password or touch the host.
    var renewanswerPath = Path.Combine(Path.GetDirectoryName(path)!, "input", "autounattend.xml");
    var renewanswer = System.Xml.Linq.XDocument.Load(renewanswerPath);
    System.Xml.Linq.XNamespace renewns = "urn:schemas-microsoft-com:unattend";
    var login = renewanswer.Descendants(renewns + "AutoLogon").Single();
    if (login.Element(renewns + "Username")?.Value != "KraicDevVM" ||
        login.Element(renewns + "Password")?.Element(renewns + "PlainText")?.Value != "true")
        throw new InvalidOperationException("Expected the original local lab account.");
    var renewpassword = login.Element(renewns + "Password")!.Element(renewns + "Value")!.Value;
    if (string.IsNullOrEmpty(renewpassword)) throw new InvalidOperationException("Original lab credential missing.");
    var renewedHiveDirectory = path + ".hives";
    using (var systemStream = File.OpenRead(Path.Combine(renewedHiveDirectory, "SYSTEM.recovered")))
    using (var systemHive = new RegistryHive(systemStream)) {
        if (systemHive.Root.OpenSubKey(@"ControlSet001\Control\ComputerName\ComputerName")?.GetValue("ComputerName") as string != "SWAPTESTVM")
            throw new InvalidOperationException("Not the isolated lab guest.");
    }
    var renewedSoftwarePath = Path.Combine(renewedHiveDirectory, "SOFTWARE.recovered");
    using (var softwareStream = File.Open(renewedSoftwarePath, FileMode.Open, FileAccess.ReadWrite))
    using (var softwareHive = new RegistryHive(softwareStream)) {
        var logon = softwareHive.Root.OpenSubKey(@"Microsoft\Windows NT\CurrentVersion\Winlogon")!;
        if (logon.GetValue("DefaultUserName") as string != "KraicDevVM") throw new InvalidOperationException("Unexpected guest login account.");
        logon.SetValue("AutoAdminLogon", "1", RegistryValueType.String);
        logon.SetValue("AutoLogonCount", 3, RegistryValueType.Dword);
        logon.SetValue("DefaultPassword", renewpassword, RegistryValueType.String);
    }
    using (var recoveredSoftware = File.OpenRead(renewedSoftwarePath))
    using (var guestSoftware = fs.OpenFile(@"Windows\System32\config\SOFTWARE", FileMode.Create, FileAccess.Write))
        recoveredSoftware.CopyTo(guestSoftware);
    if (fs.FileExists(@"hiberfil.sys")) fs.DeleteFile(@"hiberfil.sys");
    Console.WriteLine("KraicDev: existing private guest login renewed; credential not exported.");
    return;
}
if(args[1]=="list-results") {
    foreach(var directory in new[]{@"\SwapResults",@"\SwapResults\ui",@"\SwapRuns"}) {
        if(!fs.DirectoryExists(directory))continue;
        Console.WriteLine(directory+": "+string.Join(",",fs.GetFiles(directory)));
        Console.WriteLine("Subdirectories: "+string.Join(",",fs.GetDirectories(directory)));
    }
    return;
}
if (args[1]=="extract-results") {
    if(System.Diagnostics.Process.GetProcessesByName("qemu-system-x86_64").Length>0) throw new InvalidOperationException("Stop the guest before exporting its offline results.");
    // Resolve against the project marker rather than any user-supplied output path.
    var project=new DirectoryInfo(AppContext.BaseDirectory);
    while(project is not null && !File.Exists(Path.Combine(project.FullName,"Swap.sln"))) project=project.Parent;
    if(project is null) throw new InvalidOperationException("Project marker not found");
    var output=Path.Combine(project.FullName,"artifacts","vm","qemu-results",DateTime.Now.ToString("yyyyMMdd-HHmmss"));
    Directory.CreateDirectory(output);
    long total=0;
    IEnumerable<string> Reports(string directory) => fs.GetFiles(directory).Concat(fs.GetDirectories(directory).SelectMany(Reports));
    var files=Reports(@"\SwapResults").ToArray();
    if(files.Length==0) throw new InvalidOperationException("No guest reports found; an empty export is not a success.");
    if(files.Length>100) throw new InvalidOperationException("Unexpected result file count");
    foreach(var file in files){
        if(Path.GetExtension(file) is not (".json" or ".png" or ".log")) throw new InvalidOperationException("Unexpected guest result type");
        var relative=Path.GetRelativePath(@"\SwapResults",file);
        var target=Path.GetFullPath(Path.Combine(output,relative));
        if(!target.StartsWith(output+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Unsafe report path");
        using var input=fs.OpenFile(file,FileMode.Open,FileAccess.Read);
        total+=input.Length;if(total>256*1024*1024)throw new InvalidOperationException("Result size exceeds limit");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        using var destination=File.Create(target);input.CopyTo(destination);
    }
    Console.WriteLine("KraicDev: offline guest results exported: "+output);
    return;
}
if(args[1]=="copy-helpers") {
    if(args.Length!=3) throw new ArgumentException("Agent directory required.");
    foreach(var helper in Directory.GetFiles(args[2],"gspawn-win64-helper*.exe")) {
        using var input=File.OpenRead(helper);
        using var output=fs.OpenFile(Path.Combine(@"SwapAgent",Path.GetFileName(helper)),FileMode.Create,FileAccess.Write); input.CopyTo(output);
    }
    if(fs.FileExists(@"hiberfil.sys")) fs.DeleteFile(@"hiberfil.sys");
    Console.WriteLine("KraicDev: GLib helpers copied only to the stopped disposable guest.");
    return;
}
var hiveDirectory=path+".hives";
if (args[1]=="inspect-ports") {
    using var hiveStream=File.OpenRead(Path.Combine(hiveDirectory,"SYSTEM.recovered"));
    using var hive=new RegistryHive(hiveStream);
    var current=Convert.ToInt32(hive.Root.OpenSubKey("Select")!.GetValue("Current"));
    var ports=hive.Root.OpenSubKey($"ControlSet{current:D3}\\Enum\\ACPI\\PNP0501");
    foreach(var child in ports?.GetSubKeyNames() ?? []) Console.WriteLine(child+": "+ports!.OpenSubKey(child+@"\Device Parameters")?.GetValue("PortName"));
    return;
}
if (args[1]=="extract-hive") {
    Directory.CreateDirectory(hiveDirectory);
    foreach (var leaf in new[]{"SYSTEM","SYSTEM.LOG1","SYSTEM.LOG2","SOFTWARE","SOFTWARE.LOG1","SOFTWARE.LOG2"}) {
        var guestPath=@"Windows\System32\config\"+leaf;
        if (!fs.FileExists(guestPath)) continue;
        using var input=fs.OpenFile(guestPath,FileMode.Open,FileAccess.Read);
        using var output=File.Create(Path.Combine(hiveDirectory,leaf)); input.CopyTo(output);
    }
    Console.WriteLine("Private guest hive and transaction logs extracted to "+hiveDirectory);
    return;
}
if (!writable) {
    Console.WriteLine(string.Join("\n", fs.GetDirectories(@"\")));
    foreach (var log in new[] { @"Windows\Panther\UnattendGC\setupact.log", @"Windows\Panther\UnattendGC\setuperr.log" }) {
        if (!fs.FileExists(log)) continue;
        using var reader = new StreamReader(fs.OpenFile(log, FileMode.Open, FileAccess.Read));
        var text = reader.ReadToEnd();
        foreach(var line in text.Split('\n').Where(x=>x.Contains("LogonCommands") || x.Contains("Error") || x.Contains("error"))) Console.WriteLine(line.Trim());
    }
    return;
}
if (args.Length != 4) throw new ArgumentException("Bundle and agent source directories required.");
void CopyTree(string source, string destination, string search="*") {
    fs.CreateDirectory(destination);
    foreach (var file in Directory.GetFiles(source,search,SearchOption.AllDirectories)) {
        if(destination==@"SwapInput" && Path.GetExtension(file).Equals(".exe",StringComparison.OrdinalIgnoreCase)) continue;
        var relative = Path.GetRelativePath(source,file);
        var target = Path.Combine(destination,relative);
        fs.CreateDirectory(Path.GetDirectoryName(target)!);
        using var input = File.OpenRead(file);
        using var output = fs.OpenFile(target,FileMode.Create,FileAccess.Write);
        input.CopyTo(output);
    }
}
CopyTree(args[2], @"SwapInput");
CopyTree(args[3], @"SwapAgent", "*.dll");
using (var source=File.OpenRead(Path.Combine(args[3],"qemu-ga.exe")))
using (var target=fs.OpenFile(@"SwapAgent\qemu-ga.exe",FileMode.Create,FileAccess.Write)) source.CopyTo(target);
foreach(var helper in Directory.GetFiles(args[3],"gspawn-win64-helper*.exe")) {
    using var source=File.OpenRead(helper);
    using var target=fs.OpenFile(Path.Combine(@"SwapAgent",Path.GetFileName(helper)),FileMode.Create,FileAccess.Write); source.CopyTo(target);
}
if (!fs.FileExists(@"SwapAgent\gspawn-win64-helper.exe")) throw new InvalidOperationException("QEMU guest-exec requires the matching GLib spawn helper.");
fs.CreateDirectory(@"SwapResults");
// The copied disposable guest must cold-boot after offline preparation; never touch host hibernation.
if (fs.FileExists(@"hiberfil.sys")) fs.DeleteFile(@"hiberfil.sys");
foreach(var leaf in new[]{"SYSTEM","SOFTWARE"}) {
    var recoveredPath=Path.Combine(hiveDirectory,leaf+".recovered");
    if (!File.Exists(recoveredPath)) continue;
    using var recovered=File.OpenRead(recoveredPath);
    using var target=fs.OpenFile(@"Windows\System32\config\"+leaf,FileMode.Create,FileAccess.Write); recovered.CopyTo(target);
}
using (var hiveStream=fs.OpenFile(@"Windows\System32\config\SYSTEM",FileMode.Open,FileAccess.ReadWrite))
using (var hive=new RegistryHive(hiveStream)) {
    var select=hive.Root.OpenSubKey("Select") ?? throw new InvalidOperationException("ControlSet missing");
    var current=Convert.ToInt32(select.GetValue("Current"));
    var service=hive.Root.CreateSubKey($"ControlSet{current:D3}\\Services\\QEMU-GA");
    service.SetValue("Type",16,RegistryValueType.Dword);
    service.SetValue("Start",2,RegistryValueType.Dword);
    service.SetValue("ErrorControl",1,RegistryValueType.Dword);
    service.SetValue("ObjectName","LocalSystem",RegistryValueType.String);
    service.SetValue("DisplayName","KraicDev isolated VM test agent",RegistryValueType.String);
    service.SetValue("ImagePath",@"C:\SwapAgent\qemu-ga.exe -d -r -m isa-serial -p COM2 -l C:\SwapAgent\agent.log -t C:\SwapAgent -a guest-sync,guest-sync-delimited,guest-ping,guest-info,guest-exec,guest-exec-status,guest-file-open,guest-file-read,guest-file-close",RegistryValueType.String);
}
// Renew only the disposable guest's unattended login, using its original local-only password.
var answerPath=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[2]))!,"autounattend.xml");
var answer=System.Xml.Linq.XDocument.Load(answerPath);
System.Xml.Linq.XNamespace ns="urn:schemas-microsoft-com:unattend";
var password=answer.Descendants(ns+"AutoLogon").Single().Element(ns+"Password")!.Element(ns+"Value")!.Value;
using (var hiveStream=fs.OpenFile(@"Windows\System32\config\SOFTWARE",FileMode.Open,FileAccess.ReadWrite))
using (var hive=new RegistryHive(hiveStream)) {
    var logon=hive.Root.OpenSubKey(@"Microsoft\Windows NT\CurrentVersion\Winlogon") ?? throw new InvalidOperationException("Guest logon key missing");
    logon.SetValue("AutoAdminLogon","1",RegistryValueType.String);
    logon.SetValue("AutoLogonCount",3,RegistryValueType.Dword);
    logon.SetValue("DefaultUserName","KraicDevVM",RegistryValueType.String);
    logon.SetValue("DefaultDomainName","SWAPTESTVM",RegistryValueType.String);
    logon.SetValue("DefaultPassword",password,RegistryValueType.String);
}
Console.WriteLine("KraicDev: private guest bundle and serial agent prepared. No host registry or physical volume touched.");
