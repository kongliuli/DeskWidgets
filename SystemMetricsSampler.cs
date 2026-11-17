using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace DeskWidgets;

[Flags]
internal enum MetricChannels { None = 0, Cpu = 1, Gpu = 2, Memory = 4 }
internal sealed record SystemMetricsSample(long Timestamp, double? CpuPercent, double? GpuPercent, double? MemoryPercent,
    ulong UsedMemoryBytes, ulong TotalMemoryBytes, string CpuStatus, string GpuStatus, string MemoryStatus);

internal sealed class SystemMetricsSampler : IDisposable
{
    private ulong idle, kernel, user;
    private bool cpuPrimed, gpuAttempted;
    private nint query, counter, buffer;
    private uint capacity;
    private const uint MoreData = 0x800007D2;
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, Load;
        public ulong TotalPhys, AvailablePhys, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtended;
    }
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct CounterValue { [FieldOffset(0)] public uint Status; [FieldOffset(8)] public double Value; }
    [StructLayout(LayoutKind.Sequential)]
    private struct CounterItem { public nint Name; public CounterValue Value; }
    [DllImport("kernel32.dll")] private static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
    [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    [DllImport("kernel32.dll")] private static extern uint GetActiveProcessorCount(ushort group);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhOpenQueryW(string? source, nint data, out nint query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhAddEnglishCounterW(nint query, string path, nint data, out nint counter);
    [DllImport("pdh.dll")] private static extern uint PdhCollectQueryData(nint query);
    [DllImport("pdh.dll")] private static extern uint PdhCloseQuery(nint query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhGetFormattedCounterArrayW(nint counter, uint format, ref uint size, out uint count, nint values);

    internal SystemMetricsSample Read(MetricChannels channels)
    {
        double? cpu = null, gpu = null, memory = null;
        ulong total = 0, used = 0;
        string cpuStatus = "未启用", gpuStatus = "未启用", memoryStatus = "未启用";
        if (channels.HasFlag(MetricChannels.Cpu))
        {
            cpuStatus = "不可用";
            if (GetSystemTimes(out var i, out var k, out var u))
            {
                if (cpuPrimed) cpu = CpuPercent(idle, kernel, user, i, k, u);
                idle = i; kernel = k; user = u; cpuPrimed = true;
                cpuStatus = cpu.HasValue ? GetActiveProcessorCount(ushort.MaxValue) > 64 ? "当前处理器组" : "全系统" : "预热中";
            }
        }
        else cpuPrimed = false;
        if (channels.HasFlag(MetricChannels.Memory))
        {
            var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
            memoryStatus = "不可用";
            if (GlobalMemoryStatusEx(ref status) && status.TotalPhys > 0 && status.AvailablePhys <= status.TotalPhys)
            {
                total = status.TotalPhys; used = total - status.AvailablePhys; memory = 100d * used / total; memoryStatus = "物理内存";
            }
        }
        if (channels.HasFlag(MetricChannels.Gpu))
        {
            bool priming = !gpuAttempted;
            if (!gpuAttempted)
            {
                gpuAttempted = true;
                if (PdhOpenQueryW(null, 0, out query) != 0 || PdhAddEnglishCounterW(query, @"\GPU Engine(*)\Utilization Percentage", 0, out counter) != 0) CloseGpu();
            }
            gpuStatus = "计数器不可用";
            if (query != 0 && PdhCollectQueryData(query) == 0)
            {
                if (priming) gpuStatus = "预热中";
                else
                {
                    gpu = ReadGpu();
                    gpuStatus = gpu.HasValue ? "最忙引擎" : "无有效引擎样本";
                }
            }
        }
        else { CloseGpu(); gpuAttempted = false; }
        return new(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), cpu, gpu, memory, used, total, cpuStatus, gpuStatus, memoryStatus);
    }

    internal static double? CpuPercent(ulong oldIdle, ulong oldKernel, ulong oldUser, ulong newIdle, ulong newKernel, ulong newUser)
    {
        if (newIdle < oldIdle || newKernel < oldKernel || newUser < oldUser) return null;
        double total = (double)(newKernel - oldKernel) + (newUser - oldUser);
        return total > 0 ? Math.Clamp(100 * (1 - (newIdle - oldIdle) / total), 0, 100) : null;
    }
    internal static double? AggregateGpu(IEnumerable<KeyValuePair<string, double>> samples)
    {
        var engines = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var sample in samples)
        {
            int start = sample.Key.IndexOf("_luid_", StringComparison.OrdinalIgnoreCase);
            if (start < 0 || !double.IsFinite(sample.Value) || sample.Value < 0) continue;
            string engine = sample.Key[(start + 1)..];
            engines[engine] = engines.GetValueOrDefault(engine) + sample.Value;
        }
        double? highest = null;
        foreach (double value in engines.Values) highest = Math.Max(highest ?? 0, value);
        return highest.HasValue ? Math.Clamp(highest.Value, 0, 100) : null;
    }
    private double? ReadGpu()
    {
        uint size = capacity;
        uint result = PdhGetFormattedCounterArrayW(counter, 0x8200, ref size, out uint count, buffer);
        if (result == MoreData && size is > 0 and <= 16777216)
        {
            if (buffer != 0) Marshal.FreeHGlobal(buffer);
            buffer = Marshal.AllocHGlobal((int)size); capacity = size;
            result = PdhGetFormattedCounterArrayW(counter, 0x8200, ref size, out count, buffer);
        }
        if (result != 0 || buffer == 0) return null;
        var samples = new List<KeyValuePair<string, double>>((int)count);
        int itemSize = Marshal.SizeOf<CounterItem>();
        if ((ulong)count * (uint)itemSize > capacity) return null;
        for (int index = 0; index < count; index++)
        {
            var item = Marshal.PtrToStructure<CounterItem>(buffer + index * itemSize);
            if (item.Value.Status is 0 or 1 && Marshal.PtrToStringUni(item.Name) is string name) samples.Add(new(name, item.Value.Value));
        }
        return AggregateGpu(samples);
    }
    private void CloseGpu() { if (query != 0) PdhCloseQuery(query); query = 0; counter = 0; }
    public void Dispose() { CloseGpu(); if (buffer != 0) Marshal.FreeHGlobal(buffer); buffer = 0; capacity = 0; }
}
