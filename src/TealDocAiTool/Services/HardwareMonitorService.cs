using LibreHardwareMonitor.Hardware;

namespace TealDocAiTool.Services;

public sealed class HardwareSnapshot
{
    public float CpuLoadPercent { get; init; }
    public float? CpuTempCelsius { get; init; }
    public float GpuLoadPercent { get; init; }
    public float? GpuTempCelsius { get; init; }
    public float GpuMemUsedMb { get; init; }
    public float GpuMemTotalMb { get; init; }
    public string GpuName { get; init; } = "";
    public bool HasGpu { get; init; }
    public bool ElevatedSensorsAvailable { get; init; }
}

/// <summary>
/// Vékony wrapper a LibreHardwareMonitorLib köré. A CPU-hőmérséklet és néhány
/// GPU-szenzor csak rendszergazdai jogosultsággal érhető el sok gépen (pl.
/// laptopoknál gyakori) — ha nincs admin, ezek az értékek "null" maradnak,
/// SOHA nem találunk ki hamis számot.
/// </summary>
public sealed class HardwareMonitorService : IDisposable
{
    private readonly Computer _computer;
    private readonly UpdateVisitor _visitor = new();
    private bool _disposed;

    public HardwareMonitorService()
    {
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = false,
            IsMotherboardEnabled = false,
            IsStorageEnabled = false,
            IsNetworkEnabled = false,
        };
        _computer.Open();
    }

    public HardwareSnapshot ReadSnapshot()
    {
        _computer.Accept(_visitor);

        float cpuLoad = 0;
        float? cpuTemp = null;
        float gpuLoad = 0;
        float? gpuTemp = null;
        float gpuMemUsed = 0;
        float gpuMemTotal = 0;
        string gpuName = "";
        bool hasGpu = false;
        bool anySensorRestricted = false;

        foreach (var hw in _computer.Hardware)
        {
            if (hw.HardwareType == HardwareType.Cpu)
            {
                foreach (var sensor in hw.Sensors)
                {
                    if (sensor.SensorType == SensorType.Load && sensor.Name.Contains("Total", StringComparison.OrdinalIgnoreCase))
                        cpuLoad = sensor.Value ?? cpuLoad;
                    else if (sensor.SensorType == SensorType.Temperature &&
                             (sensor.Name.Contains("Package", StringComparison.OrdinalIgnoreCase) ||
                              sensor.Name.Contains("Average", StringComparison.OrdinalIgnoreCase)))
                        cpuTemp = sensor.Value;
                }
                if (cpuTemp is null) anySensorRestricted = true;
            }
            else if (hw.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel)
            {
                // Ha több GPU van (pl. Intel iGPU + NVIDIA), az NVIDIA/AMD dedikáltat részesítjük előnyben
                bool isDiscrete = hw.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd;
                if (hasGpu && !isDiscrete) continue;

                hasGpu = true;
                gpuName = hw.Name;
                foreach (var sensor in hw.Sensors)
                {
                    if (sensor.SensorType == SensorType.Load && sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase))
                        gpuLoad = sensor.Value ?? gpuLoad;
                    else if (sensor.SensorType == SensorType.Temperature && sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase))
                        gpuTemp = sensor.Value;
                    else if (sensor.SensorType == SensorType.SmallData && sensor.Name.Contains("Memory Used", StringComparison.OrdinalIgnoreCase))
                        gpuMemUsed = sensor.Value ?? gpuMemUsed;
                    else if (sensor.SensorType == SensorType.SmallData && sensor.Name.Contains("Memory Total", StringComparison.OrdinalIgnoreCase))
                        gpuMemTotal = sensor.Value ?? gpuMemTotal;
                }
            }
        }

        return new HardwareSnapshot
        {
            CpuLoadPercent = cpuLoad,
            CpuTempCelsius = cpuTemp,
            GpuLoadPercent = gpuLoad,
            GpuTempCelsius = gpuTemp,
            GpuMemUsedMb = gpuMemUsed,
            GpuMemTotalMb = gpuMemTotal,
            GpuName = gpuName,
            HasGpu = hasGpu,
            ElevatedSensorsAvailable = !anySensorRestricted,
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _computer.Close();
    }

    private sealed class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer) => computer.Traverse(this);
        public void VisitHardware(IHardware hardware)
        {
            hardware.Update();
            foreach (var sub in hardware.SubHardware) sub.Accept(this);
        }
        public void VisitSensor(ISensor sensor) { }
        public void VisitParameter(IParameter parameter) { }
    }
}
