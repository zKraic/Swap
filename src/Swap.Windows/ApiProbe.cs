// Copyright © 2026 KraicDev. SPDX-License-Identifier: GPL-3.0-or-later
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Swap.Core;
namespace Swap.Windows;

// KraicDev. What games load: Direct3D 12 devices, Vulkan physical devices and OpenCL platforms, asked from the runtimes
// themselves. Collect() runs in a child process ("--probe-apis") because a leftover ICD or layer can crash or hang its host.
// Runtimes are loaded by full System32 path only. Nothing is rendered, installed or written.
public static class ApiProbe
{
    public const string Argument = "--probe-apis";
    public const string Marker = "SWAP-API-REPORT ";
    // Entry point of the child process.
    public static int WriteReport()
    {
        var json = JsonSerializer.Serialize(Collect());
        using var output = Console.OpenStandardOutput();
        var bytes = System.Text.Encoding.UTF8.GetBytes("\n" + Marker + json + "\n");
        output.Write(bytes, 0, bytes.Length); output.Flush();
        return 0;
    }
    private static string System32 => Environment.GetFolderPath(Environment.SpecialFolder.System);

    public static async Task<ApiReport> RunAsync(string executable, int timeoutSeconds = 45, CancellationToken token = default)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = new System.Text.UTF8Encoding(false) };
        info.ArgumentList.Add(Argument);
        try
        {
            using var process = Process.Start(info) ?? throw new IOException("Probe did not start.");
            var output = process.StandardOutput.ReadToEndAsync(token); _ = process.StandardError.ReadToEndAsync(token);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                // Only the probe itself is stopped; it never changes the system.
                try { process.Kill(true); } catch (InvalidOperationException) { }
                return Failed("timeout");
            }
            // A Vulkan layer may write to the console too; only the text after the marker is the report.
            var text = await output;
            var start = text.LastIndexOf(Marker, StringComparison.Ordinal);
            return start >= 0 && JsonSerializer.Deserialize<ApiReport>(text[(start + Marker.Length)..]) is { } report ? report : Failed("exit " + process.ExitCode);
        }
        catch (Exception e) when (e is IOException or JsonException or System.ComponentModel.Win32Exception or InvalidOperationException) { return Failed(e.GetType().Name); }
    }
    private static ApiReport Failed(string error) => new(DateTimeOffset.UtcNow, false, [], "unknown", [], "unknown", [], [error]);

    public static ApiReport Collect()
    {
        var errors = new List<string>(); List<ApiDevice> d3d12 = [], vulkan = [], opencl = [];
        string vulkanStatus, openclStatus;
        try { d3d12 = Direct3D12(); } catch (Exception e) when (e is not OutOfMemoryException) { errors.Add("D3D12 " + e.GetType().Name); }
        try { (vulkanStatus, vulkan) = Vulkan(); } catch (Exception e) when (e is not OutOfMemoryException) { vulkanStatus = "error " + e.GetType().Name; }
        try { (openclStatus, opencl) = OpenCl(); } catch (Exception e) when (e is not OutOfMemoryException) { openclStatus = "error " + e.GetType().Name; }
        return new(DateTimeOffset.UtcNow, true, d3d12, vulkanStatus, vulkan, openclStatus, opencl, errors);
    }

    // ---- Direct3D 12 through DXGI 1.1 ----
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateFactory(ref Guid id, out IntPtr factory);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int EnumAdapters1(IntPtr factory, uint index, out IntPtr adapter);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetDesc1(IntPtr adapter, IntPtr description);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateDevice(IntPtr adapter, int level, ref Guid id, out IntPtr device);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CheckFeature(IntPtr device, int feature, IntPtr data, int size);
    private static T Method<T>(IntPtr value, int index) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(value), index * IntPtr.Size));
    private static T Export<T>(IntPtr library, string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));

    private static List<ApiDevice> Direct3D12()
    {
        var result = new List<ApiDevice>();
        var d3d12Path = Path.Combine(System32, "d3d12.dll");
        if (!File.Exists(d3d12Path)) return result;
        var dxgi = NativeLibrary.Load(Path.Combine(System32, "dxgi.dll")); var d3d12 = NativeLibrary.Load(d3d12Path);
        var factoryId = new Guid("770aae78-f26f-4dba-a829-253c83d1b387"); var deviceId = new Guid("189819f1-1db6-4b57-be54-1821339b85f7");
        Marshal.ThrowExceptionForHR(Export<CreateFactory>(dxgi, "CreateDXGIFactory1")(ref factoryId, out var factory));
        var create = Export<CreateDevice>(d3d12, "D3D12CreateDevice");
        var description = Marshal.AllocHGlobal(512); var levels = Marshal.AllocHGlobal(5 * 4); var query = Marshal.AllocHGlobal(24);
        try
        {
            for (uint i = 0; i < 16; i++)
            {
                if (Method<EnumAdapters1>(factory, 12)(factory, i, out var adapter) < 0) break;
                try
                {
                    if (Method<GetDesc1>(adapter, 10)(adapter, description) < 0) continue;
                    var name = Marshal.PtrToStringUni(description) ?? ""; int vendor = Marshal.ReadInt32(description, 256), device = Marshal.ReadInt32(description, 260), flags = Marshal.ReadInt32(description, 304);
                    if ((flags & 2) != 0 || vendor == 0x1414) continue;
                    if (create(adapter, 0xb000, ref deviceId, out var created) < 0 || created == IntPtr.Zero) continue;
                    try
                    {
                        int maximum = 0xb000;
                        // D3D12_FEATURE_FEATURE_LEVELS; older runtimes reject 12_2, so retry without it.
                        foreach (var count in new[] { 5, 4 })
                        {
                            int[] requested = [0xb000, 0xb100, 0xc000, 0xc100, 0xc200];
                            Marshal.Copy(requested, 0, levels, 5);
                            Marshal.WriteInt32(query, 0, count); Marshal.WriteIntPtr(query, 8, levels); Marshal.WriteInt32(query, 16, 0);
                            if (Method<CheckFeature>(created, 13)(created, 2, query, 24) >= 0) { maximum = Marshal.ReadInt32(query, 16); break; }
                        }
                        result.Add(new("D3D12", name, vendor, device, GameReadiness.FeatureLevel(maximum)));
                    }
                    finally { Marshal.Release(created); }
                }
                finally { Marshal.Release(adapter); }
            }
        }
        finally { Marshal.FreeHGlobal(description); Marshal.FreeHGlobal(levels); Marshal.FreeHGlobal(query); Marshal.Release(factory); }
        return result;
    }

    // ---- Vulkan through the Khronos loader ----
    [StructLayout(LayoutKind.Sequential)] private struct ApplicationInfo { public int Type; public IntPtr Next; public IntPtr Name; public uint Version; public IntPtr Engine; public uint EngineVersion; public uint ApiVersion; }
    [StructLayout(LayoutKind.Sequential)] private struct InstanceInfo { public int Type; public IntPtr Next; public uint Flags; public IntPtr Application; public uint Layers; public IntPtr LayerNames; public uint Extensions; public IntPtr ExtensionNames; }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateInstance(ref InstanceInfo info, IntPtr allocator, out IntPtr instance);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int EnumeratePhysical(IntPtr instance, ref uint count, IntPtr devices);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Properties(IntPtr device, IntPtr properties);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void DestroyInstance(IntPtr instance, IntPtr allocator);

    private static (string, List<ApiDevice>) Vulkan()
    {
        var result = new List<ApiDevice>();
        var path = Path.Combine(System32, "vulkan-1.dll");
        if (!File.Exists(path)) return ("missing", result);
        var loader = NativeLibrary.Load(path);
        var name = Marshal.StringToHGlobalAnsi("Swap GPU readiness"); var properties = Marshal.AllocHGlobal(4096);
        var application = new ApplicationInfo { Type = 0, Name = name, Version = 1, Engine = name, EngineVersion = 1, ApiVersion = 1u << 22 };
        var applicationPointer = Marshal.AllocHGlobal(Marshal.SizeOf<ApplicationInfo>());
        try
        {
            Marshal.StructureToPtr(application, applicationPointer, false);
            var info = new InstanceInfo { Type = 1, Application = applicationPointer };
            int created = Export<CreateInstance>(loader, "vkCreateInstance")(ref info, IntPtr.Zero, out var instance);
            // VK_ERROR_INCOMPATIBLE_DRIVER: the loader works but no Vulkan driver is registered.
            if (created == -9) return ("ok", result);
            if (created != 0) return ("error " + created, result);
            try
            {
                var enumerate = Export<EnumeratePhysical>(loader, "vkEnumeratePhysicalDevices"); uint count = 0;
                if (enumerate(instance, ref count, IntPtr.Zero) != 0 || count == 0) return ("ok", result);
                count = Math.Min(count, 16); var devices = Marshal.AllocHGlobal((int)count * IntPtr.Size);
                try
                {
                    int listed = enumerate(instance, ref count, devices);
                    if (listed < 0) return ("error " + listed, result);
                    var read = Export<Properties>(loader, "vkGetPhysicalDeviceProperties");
                    for (int i = 0; i < count; i++)
                    {
                        read(Marshal.ReadIntPtr(devices, i * IntPtr.Size), properties);
                        // Software renderers (VK_PHYSICAL_DEVICE_TYPE_CPU) are not a GPU.
                        if (Marshal.ReadInt32(properties, 16) == 4) continue;
                        result.Add(new("Vulkan", Marshal.PtrToStringAnsi(properties + 20) ?? "", Marshal.ReadInt32(properties, 8), Marshal.ReadInt32(properties, 12),
                            GameReadiness.VulkanVersion((uint)Marshal.ReadInt32(properties, 0))));
                    }
                }
                finally { Marshal.FreeHGlobal(devices); }
            }
            finally { Export<DestroyInstance>(loader, "vkDestroyInstance")(instance, IntPtr.Zero); }
            return ("ok", result);
        }
        finally { Marshal.FreeHGlobal(name); Marshal.FreeHGlobal(properties); Marshal.FreeHGlobal(applicationPointer); }
    }

    // ---- OpenCL through the ICD loader. DeviceId carries the number of GPU devices of the platform. ----
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int PlatformIds(uint entries, IntPtr platforms, out uint count);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int PlatformInfo(IntPtr platform, uint parameter, UIntPtr size, IntPtr value, out UIntPtr returned);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int DeviceIds(IntPtr platform, ulong type, uint entries, IntPtr devices, out uint count);

    private static (string, List<ApiDevice>) OpenCl()
    {
        var result = new List<ApiDevice>();
        var path = Path.Combine(System32, "OpenCL.dll");
        if (!File.Exists(path)) return ("missing", result);
        var loader = NativeLibrary.Load(path);
        var ids = Export<PlatformIds>(loader, "clGetPlatformIDs");
        int status = ids(0, IntPtr.Zero, out var count);
        if (status == -1001 || count == 0) return ("ok", result);
        if (status != 0) return ("error " + status, result);
        count = Math.Min(count, 16); var platforms = Marshal.AllocHGlobal((int)count * IntPtr.Size); var text = Marshal.AllocHGlobal(1024);
        try
        {
            if ((status = ids(count, platforms, out count)) != 0) return ("error " + status, result);
            var info = Export<PlatformInfo>(loader, "clGetPlatformInfo"); var devices = Export<DeviceIds>(loader, "clGetDeviceIDs");
            string Read(IntPtr platform, uint parameter) => info(platform, parameter, 1023, text, out _) == 0 ? (Marshal.PtrToStringAnsi(text) ?? "").Trim() : "";
            for (int i = 0; i < count; i++)
            {
                var platform = Marshal.ReadIntPtr(platforms, i * IntPtr.Size);
                var vendor = Read(platform, 0x0903);
                int gpus = devices(platform, 4, 0, IntPtr.Zero, out var found) == 0 ? (int)found : 0;
                result.Add(new("OpenCL", Read(platform, 0x0902) + " · " + vendor, GameReadiness.PciVendor(GameReadiness.FromOpenClVendor(vendor)), gpus, Read(platform, 0x0901)));
            }
        }
        finally { Marshal.FreeHGlobal(platforms); Marshal.FreeHGlobal(text); }
        return ("ok", result);
    }

    // Simulated PC: without the vendor driver the runtimes see no GPU, as on a real basic display adapter.
    public static ApiReport Demo(Adapter adapter)
    {
        if (!Planner.IsOem(adapter.Inf)) return new(DateTimeOffset.UtcNow, true, [], "missing", [], "missing", [], []);
        int vendor = GameReadiness.PciVendor(adapter.Vendor); var index = adapter.Id.IndexOf("DEV_", StringComparison.OrdinalIgnoreCase);
        int device = index >= 0 ? Convert.ToInt32(adapter.Id.Substring(index + 4, 4), 16) : 0;
        return new(DateTimeOffset.UtcNow, true, [new("D3D12", adapter.Name, vendor, device, "12_2")], "ok", [new("Vulkan", adapter.Name, vendor, device, "1.3.280")],
            "ok", [new("OpenCL", adapter.Vendor == Vendor.Nvidia ? "NVIDIA CUDA · NVIDIA Corporation" : adapter.Vendor == Vendor.Amd ? "AMD Accelerated Parallel Processing · Advanced Micro Devices, Inc." : "Intel(R) OpenCL Graphics · Intel(R) Corporation", vendor, 1, "OpenCL 3.0")], []);
    }
}
