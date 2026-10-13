using System.Runtime.InteropServices;
using System.Text.Json;
using Swap.Core;
namespace Swap.Windows;

// KraicDev. Bounded, read-only display enumeration and hardware Direct3D creation; no mode changes or stress test.
public static class GraphicsInspector
{
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] private struct DisplayDevice
    {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=32)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=128)] public string Description;
        public int Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=128)] public string Id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=128)] public string RegistryKey;
        public static DisplayDevice New() => new() { Size=Marshal.SizeOf<DisplayDevice>(), Name="",Description="",Id="",RegistryKey="" };
    }
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] private struct DeviceMode
    {
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)] public string Name;
        public short SpecVersion,DriverVersion,Size,Extra;
        public int Fields,PositionX,PositionY,Orientation,FixedOutput;
        public short Color,Duplex,YResolution,TTOption,Collate;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)] public string FormName;
        public short LogPixels;
        public int Bits,Width,Height,Flags,Frequency,IcmMethod,IcmIntent,MediaType,DitherType,Reserved1,Reserved2,PanningWidth,PanningHeight;
        public static DeviceMode New() => new() {Size=(short)Marshal.SizeOf<DeviceMode>(),Name="",FormName=""};
    }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] private struct AdapterDescription
    {
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)] public string Name;
        public uint Vendor,Device,SubSystem,Revision;
        public nuint DedicatedVideo,DedicatedSystem,Shared;
        public uint LuidLow; public int LuidHigh;
    }
    [DllImport("user32.dll",CharSet=CharSet.Unicode,EntryPoint="EnumDisplayDevicesW")] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool EnumDevices(string? device,uint index,ref DisplayDevice data,uint flags);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,EntryPoint="EnumDisplaySettingsW")] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool EnumModes(string device,int index,ref DeviceMode mode);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,EntryPoint="ChangeDisplaySettingsExW")] private static extern int ChangeSettings(string device,ref DeviceMode mode,IntPtr window,uint flags,IntPtr parameter);
    [DllImport("dxgi.dll")] private static extern int CreateDXGIFactory(ref Guid id,out IntPtr factory);
    [DllImport("d3d11.dll")] private static extern int D3D11CreateDevice(IntPtr adapter,int type,IntPtr software,uint flags,IntPtr levels,uint levelCount,uint sdk,out IntPtr device,out int level,out IntPtr context);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int EnumAdapter(IntPtr factory,uint index,out IntPtr adapter);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetDescription(IntPtr adapter,out AdapterDescription description);
    private static T Method<T>(IntPtr value,int index) where T:Delegate => Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(value),index*IntPtr.Size));

    public static (DisplayOutput[] Displays,GraphicsProbe[] Probes) ReadNative()
    {
        var displays=new List<DisplayOutput>();
        for(uint i=0;i<64;i++)
        {
            var adapter=DisplayDevice.New(); if(!EnumDevices(null,i,ref adapter,0))break;
            if((adapter.Flags&1)==0 || (adapter.Flags&8)!=0)continue;
            var current=DeviceMode.New(); if(!EnumModes(adapter.Name,-1,ref current))continue;
            int max=current.Frequency;
            for(int j=0;j<2048;j++) {var mode=DeviceMode.New(); if(!EnumModes(adapter.Name,j,ref mode))break; if(mode.Width==current.Width&&mode.Height==current.Height&&mode.Bits==current.Bits&&mode.Flags==current.Flags)max=Math.Max(max,mode.Frequency);}
            var monitor=DisplayDevice.New();var name=EnumDevices(adapter.Name,0,ref monitor,0)?monitor.Description:adapter.Description;
            displays.Add(new(adapter.Name,name,adapter.Id,current.Width,current.Height,current.Frequency,max));
        }
        var probes=new List<GraphicsProbe>(); var guid=new Guid("7B7166EC-21C7-44AE-B21A-C9AE321AE369");
        Marshal.ThrowExceptionForHR(CreateDXGIFactory(ref guid,out var factory));
        try
        {
            var enumerate=Method<EnumAdapter>(factory,7);
            for(uint i=0;i<64;i++)
            {
                int hr=enumerate(factory,i,out var adapter); if(hr==unchecked((int)0x887A0002))break; Marshal.ThrowExceptionForHR(hr);
                try
                {
                    Marshal.ThrowExceptionForHR(Method<GetDescription>(adapter,8)(adapter,out var description));
                    bool hardware=description.Vendor!=0x1414 && description.Vendor!=0;
                    int result=unchecked((int)0x887A0004),level=0; IntPtr device=IntPtr.Zero,context=IntPtr.Zero;
                    try { if(hardware) result=D3D11CreateDevice(adapter,0,IntPtr.Zero,0,IntPtr.Zero,0,7,out device,out level,out context); }
                    finally {if(context!=IntPtr.Zero)Marshal.Release(context);if(device!=IntPtr.Zero)Marshal.Release(device);}
                    probes.Add(new($@"PCI\VEN_{description.Vendor:X4}&DEV_{description.Device:X4}",description.Name,(long)description.DedicatedVideo,hardware,result>=0,level,result>=0?"":$"0x{result:X8}"));
                }
                finally {Marshal.Release(adapter);}
            }
        }
        finally {Marshal.Release(factory);}
        return(displays.ToArray(),probes.ToArray());
    }

    // Apply a refresh rate the display already reports for its current resolution. Returns the previous rate.
    // The caller asks for confirmation and restores the previous rate when it is not confirmed.
    public static int SetRefresh(string device,int hz)
    {
        var current=DeviceMode.New(); if(!EnumModes(device,-1,ref current)) throw new IOException("Display mode unavailable.");
        bool listed=false;
        for(int j=0;j<2048 && !listed;j++){var mode=DeviceMode.New(); if(!EnumModes(device,j,ref mode))break; listed=mode.Width==current.Width&&mode.Height==current.Height&&mode.Bits==current.Bits&&mode.Frequency==hz;}
        if(!listed) throw new IOException("Refresh rate not reported by the display.");
        var target=current; target.Frequency=hz; target.Fields=0x40000|0x80000|0x100000|0x400000;
        if(ChangeSettings(device,ref target,IntPtr.Zero,0x2,IntPtr.Zero)!=0) throw new IOException("Windows rejected the display mode.");
        if(ChangeSettings(device,ref target,IntPtr.Zero,0x1,IntPtr.Zero)!=0) throw new IOException("Windows did not apply the display mode.");
        return current.Frequency;
    }
    private sealed record EventsResult(DisplayIncident[] Incidents,bool Complete,PcieLink[]? Links);
    public static async Task<GraphicsDiagnostics> ReadAsync()
    {
        var warnings=new List<string>(); DisplayOutput[] displays=[];GraphicsProbe[] probes=[];DisplayIncident[] incidents=[];PcieLink[] links=[];
        bool displaysComplete=false,eventsComplete=false;
        try { (displays,probes)=await Task.Run(ReadNative);displaysComplete=true; } catch(Exception e){warnings.Add("Native graphics: "+e.GetType().Name);}
        try
        {
            // Read-only event and PCIe queries; slow PCs may need minutes.
            var result=JsonSerializer.Deserialize<EventsResult>(await WindowsPlatform.PowerShellAsync(WindowsPlatform.Script("HealthEvents"),600));
            if(result is not null){incidents=result.Incidents;eventsComplete=result.Complete;links=result.Links??[];}
        }
        catch(Exception e){warnings.Add("Display events: "+e.GetType().Name);}
        return new(DateTimeOffset.UtcNow,displays,probes,incidents,displaysComplete,eventsComplete,warnings,links);
    }
    public static GraphicsDiagnostics Demo(Adapter adapter) => new(DateTimeOffset.UtcNow,
        [new("DEMO-DISPLAY","Display",adapter.Id,2560,1440,60,144)],
        [new(adapter.Id[..adapter.Id.LastIndexOf('\\')],adapter.Name,8L*1024*1024*1024,true,Planner.IsOem(adapter.Inf),0xB000,Planner.IsOem(adapter.Inf)?"":"0x887A0004")],[],true,true,[]);
}
