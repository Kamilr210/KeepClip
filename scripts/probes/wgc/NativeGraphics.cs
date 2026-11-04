using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

internal sealed class NativeGraphics : IDisposable
{
    public IDirect3DDevice Device { get; }
    public string Driver { get; }

    private NativeGraphics(IDirect3DDevice device, string driver) { Device = device; Driver = driver; }

    public static NativeGraphics Create(List<object> attempts)
    {
        foreach (var driver in new[] { (Id: 1u, Name: "hardware"), (Id: 5u, Name: "warp") })
        {
            IntPtr native = IntPtr.Zero, context = IntPtr.Zero, dxgi = IntPtr.Zero, inspectable = IntPtr.Zero;
            try
            {
                int hr = D3D11CreateDevice(IntPtr.Zero, driver.Id, IntPtr.Zero, 0x20, null, 0, 7, out native, out var featureLevel, out context);
                attempts.Add(new { driver = driver.Name, stage = "D3D11CreateDevice", hresult = $"0x{hr:X8}", featureLevel });
                if (hr < 0) continue;
                var iid = new Guid("54EC77FA-1377-44E6-8C32-88FD5F44C84C");
                Marshal.ThrowExceptionForHR(Marshal.QueryInterface(native, in iid, out dxgi));
                hr = CreateDirect3D11DeviceFromDXGIDevice(dxgi, out inspectable);
                attempts.Add(new { driver = driver.Name, stage = "CreateDirect3D11DeviceFromDXGIDevice", hresult = $"0x{hr:X8}" });
                Marshal.ThrowExceptionForHR(hr);
                var device = MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
                return new(device, driver.Name);
            }
            catch (Exception ex) { attempts.Add(new { driver = driver.Name, stage = "WrapDevice", hresult = $"0x{ex.HResult:X8}", error = ex.Message }); }
            finally
            {
                foreach (var pointer in new[] { inspectable, dxgi, context, native }) if (pointer != IntPtr.Zero) Marshal.Release(pointer);
            }
        }
        throw new InvalidOperationException("Neither hardware nor WARP D3D11 device could be created.");
    }

    public static GraphicsCaptureItem CreateForPrimaryMonitor()
    {
        var monitor = MonitorFromPoint(new Point(0, 0), 1);
        if (monitor == IntPtr.Zero) throw new InvalidOperationException("Primary monitor is unavailable.");
        var factory = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
        var iid = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");
        Marshal.ThrowExceptionForHR(factory.CreateForMonitor(monitor, in iid, out var pointer));
        try { return MarshalInspectable<GraphicsCaptureItem>.FromAbi(pointer); }
        finally { Marshal.Release(pointer); }
    }

    public static GraphicsCaptureItem CreateForWindow(IntPtr window)
    {
        var factory = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
        var iid = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");
        Marshal.ThrowExceptionForHR(factory.CreateForWindow(window, in iid, out var pointer));
        try { return MarshalInspectable<GraphicsCaptureItem>.FromAbi(pointer); }
        finally { Marshal.Release(pointer); }
    }

    public void Dispose() => Device.Dispose();

    [ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        [PreserveSig] int CreateForWindow(IntPtr window, in Guid iid, out IntPtr result);
        [PreserveSig] int CreateForMonitor(IntPtr monitor, in Guid iid, out IntPtr result);
    }

    [DllImport("d3d11.dll", ExactSpelling = true)] private static extern int D3D11CreateDevice(IntPtr adapter, uint driverType, IntPtr software, uint flags, uint[]? levels, uint levelCount, uint sdkVersion, out IntPtr device, out uint level, out IntPtr immediateContext);
    [DllImport("d3d11.dll", ExactSpelling = true)] private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr device, out IntPtr inspectable);
    [DllImport("user32.dll", ExactSpelling = true)] private static extern IntPtr MonitorFromPoint(Point point, uint flags);
}
