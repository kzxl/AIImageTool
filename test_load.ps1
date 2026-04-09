$source = @"
using System;
using System.Runtime.InteropServices;

public class NativeLibTest {
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr LoadLibrary(string dllToLoad);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool FreeLibrary(IntPtr hModule);

    public static void TestLoad(string path) {
        IntPtr ptr = LoadLibrary(path);
        if (ptr == IntPtr.Zero) {
            int errorCode = Marshal.GetLastWin32Error();
            Console.WriteLine($"Failed to load {path}. Error code: {errorCode} (0x{errorCode:X})");
        } else {
            Console.WriteLine($"Successfully loaded {path} at 0x{ptr.ToInt64():X}");
            FreeLibrary(ptr);
        }
    }
}
"@
Add-Type -TypeDefinition $source
[NativeLibTest]::TestLoad("E:\15. Other\ImageTool\Publish_Release\Lite\onnxruntime.dll")
[NativeLibTest]::TestLoad("E:\15. Other\ImageTool\Publish_Release\Lite\onnxruntime_providers_shared.dll")
[NativeLibTest]::TestLoad("E:\15. Other\ImageTool\Publish_Release\Lite\DirectML.dll")
