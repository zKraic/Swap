# Comparison lab step 2: the real signed AMD display package, staged and then bound to the emulated ATI card
# (PCI\VEN_1002&DEV_5159) through the "Have Disk" route (model PCI\VEN_1002&DEV_6738), like Device Manager does.
$cd = (Get-CimInstance Win32_LogicalDisk | Where-Object VolumeName -eq 'SWAPLAB').DeviceID
if (!(Test-Path C:\SwapLab\cmp\amd\u0202828.inf)) { Copy-Item "$cd\amd" C:\SwapLab\cmp -Recurse -Force }
$sig = Get-AuthenticodeSignature C:\SwapLab\cmp\amd\u0202828.cat
'catalog ' + $sig.Status + ' ' + $sig.SignerCertificate.Subject
$start = Get-Date
'stage ' + ((& pnputil.exe /add-driver C:\SwapLab\cmp\amd\u0202828.inf) -join ' ')
'stage extension ' + ((& pnputil.exe /add-driver C:\SwapLab\cmp\amd\amdwddme.inf) -join ' ')
$published = (Get-WindowsDriver -Online | Where-Object { $_.OriginalFileName -like '*\u0202828.inf' } | Select-Object -First 1).Driver
'published ' + $published
Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices; using System.Text;
public static class HaveDisk {
  [StructLayout(LayoutKind.Sequential)] public struct DevInfo { public int cbSize; public Guid ClassGuid; public int DevInst; public IntPtr Reserved; }
  [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] public struct InstallParams { public int cbSize; public uint Flags; public uint FlagsEx; public IntPtr Parent; public IntPtr Handler; public IntPtr Context; public IntPtr Queue; public UIntPtr ClassReserved; public uint Reserved; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DriverPath; }
  [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] public struct DrvInfo { public int cbSize; public int DriverType; public UIntPtr Reserved; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Description; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Mfg; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Provider; public uint DateLow; public uint DateHigh; public ulong Version; }
  [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr SetupDiGetClassDevsW(IntPtr guid, string enumerator, IntPtr hwnd, int flags);
  [DllImport("setupapi.dll", SetLastError = true)] static extern bool SetupDiEnumDeviceInfo(IntPtr set, int index, ref DevInfo data);
  [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool SetupDiGetDeviceInstanceIdW(IntPtr set, ref DevInfo data, StringBuilder id, int size, out int required);
  [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool SetupDiGetDeviceInstallParamsW(IntPtr set, ref DevInfo data, ref InstallParams p);
  [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool SetupDiSetDeviceInstallParamsW(IntPtr set, ref DevInfo data, ref InstallParams p);
  [DllImport("setupapi.dll", SetLastError = true)] static extern bool SetupDiBuildDriverInfoList(IntPtr set, ref DevInfo data, int type);
  [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool SetupDiEnumDriverInfoW(IntPtr set, ref DevInfo data, int type, int index, ref DrvInfo d);
  [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool SetupDiGetDriverInfoDetailW(IntPtr set, ref DevInfo data, ref DrvInfo d, IntPtr detail, int size, out int required);
  [DllImport("newdev.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool DiInstallDevice(IntPtr hwnd, IntPtr set, ref DevInfo data, ref DrvInfo d, int flags, out bool reboot);
  public static string Run(string devicePrefix, string inf, string model) {
    var log = new StringBuilder();
    IntPtr set = SetupDiGetClassDevsW(IntPtr.Zero, "PCI", IntPtr.Zero, 2 | 4);
    var dev = new DevInfo(); dev.cbSize = Marshal.SizeOf(typeof(DevInfo)); bool found = false;
    for (int i = 0; SetupDiEnumDeviceInfo(set, i, ref dev); i++) {
      var id = new StringBuilder(512); int req; SetupDiGetDeviceInstanceIdW(set, ref dev, id, 512, out req);
      if (id.ToString().StartsWith(devicePrefix, StringComparison.OrdinalIgnoreCase)) { log.AppendLine("device " + id); found = true; break; }
    }
    if (!found) return log.Append("device not found").ToString();
    var p = new InstallParams(); p.cbSize = Marshal.SizeOf(typeof(InstallParams));
    if (!SetupDiGetDeviceInstallParamsW(set, ref dev, ref p)) return log.Append("get params " + Marshal.GetLastWin32Error()).ToString();
    p.Flags |= 0x00010000; p.FlagsEx |= 0x00000800; p.DriverPath = inf;
    if (!SetupDiSetDeviceInstallParamsW(set, ref dev, ref p)) return log.Append("set params " + Marshal.GetLastWin32Error()).ToString();
    if (!SetupDiBuildDriverInfoList(set, ref dev, 1)) return log.Append("build " + Marshal.GetLastWin32Error()).ToString();
    var drv = new DrvInfo(); drv.cbSize = Marshal.SizeOf(typeof(DrvInfo)); IntPtr detail = Marshal.AllocHGlobal(65536); int count = 0;
    try {
      for (int i = 0; ; i++) {
        drv.cbSize = Marshal.SizeOf(typeof(DrvInfo));
        if (!SetupDiEnumDriverInfoW(set, ref dev, 1, i, ref drv)) break; count++;
        Marshal.WriteInt32(detail, 1584); int req;
        if (!SetupDiGetDriverInfoDetailW(set, ref dev, ref drv, detail, 65536, out req)) continue;
        string hw = Marshal.PtrToStringUni(new IntPtr(detail.ToInt64() + 1576));
        if (!string.Equals(hw, model, StringComparison.OrdinalIgnoreCase)) continue;
        log.AppendLine("driver " + drv.Description + " / " + drv.Provider + " / " + hw + " / size " + Marshal.SizeOf(typeof(DrvInfo)));
        bool reboot;
        bool ok = DiInstallDevice(IntPtr.Zero, set, ref dev, ref drv, 0, out reboot);
        return log.Append("install " + ok + " error " + Marshal.GetLastWin32Error() + " reboot " + reboot).ToString();
      }
    } finally { Marshal.FreeHGlobal(detail); }
    return log.Append("model not found among " + count).ToString();
  }
}
'@
[HaveDisk]::Run('PCI\VEN_1002&DEV_5159', "C:\Windows\INF\$published", 'PCI\VEN_1002&DEV_1638&SUBSYS_20981025&REV_C3')
'elapsed ' + ((Get-Date) - $start)
Get-PnpDevice -ErrorAction SilentlyContinue | Where-Object { $_.InstanceId -like 'PCI\VEN_1002*' -or $_.Class -eq 'Display' } | Format-List InstanceId, FriendlyName, Status, Problem, Class | Out-String
Get-CimInstance Win32_VideoController | Format-List Name, PNPDeviceID, DriverVersion, InstalledDisplayDrivers, ConfigManagerErrorCode | Out-String
Get-WindowsDriver -Online | Where-Object { $_.ProviderName -match 'Advanced Micro' } | Format-Table Driver, ClassName, @{n='Original';e={Split-Path $_.OriginalFileName -Leaf}} -AutoSize | Out-String -Width 200
'services: ' + ((Get-ChildItem HKLM:\SYSTEM\CurrentControlSet\Services | Where-Object { $_.PSChildName -match '^(amd|AMD|ati|Ati)' } | ForEach-Object PSChildName) -join ', ')
