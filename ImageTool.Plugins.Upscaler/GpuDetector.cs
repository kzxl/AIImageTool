using System;
using System.Collections.Generic;
using System.Management;
using System.Threading.Tasks;

namespace ImageTool.Plugins.Upscaler;

public class GpuInfo
{
    public int DeviceId { get; set; }
    public string Name { get; set; } = string.Empty;

    public override string ToString()
    {
        return DeviceId == -1 ? "CPU Only" : $"GPU {DeviceId}: {Name}";
    }
}

public static class GpuDetector
{
    public static List<GpuInfo> GetAvailableDevices()
    {
        var devices = new List<GpuInfo>
        {
            new GpuInfo { DeviceId = -1, Name = "CPU Only" }
        };

        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
                int id = 0;
                foreach (var obj in searcher.Get())
                {
                    string name = obj["Name"]?.ToString() ?? "Unknown GPU";
                    devices.Add(new GpuInfo { DeviceId = id, Name = name });
                    id++;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GpuDetector WMI Lỗi: {ex.Message}");
            }
        }

        return devices;
    }

    public static Task<List<GpuInfo>> GetAvailableDevicesAsync()
    {
        return Task.Run(() => GetAvailableDevices());
    }
}
