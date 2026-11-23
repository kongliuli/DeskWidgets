using System;
using System.Runtime.InteropServices;

namespace DeskWidgets;

internal sealed record AudioSample(long Timestamp, double? Volume, bool Muted, double? Peak, double? Dbfs, string Status);
internal sealed class AudioEndpoint : IDisposable
{
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, uint mask, out nint devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IDevice device);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint context, nint parameters, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
        [PreserveSig] int OpenPropertyStore(uint access, out nint properties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out uint state);
    }
    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(nint callback);
        [PreserveSig] int UnregisterControlChangeNotify(nint callback);
        [PreserveSig] int GetChannelCount(out uint channels);
        [PreserveSig] int SetMasterVolumeLevel(float decibels, ref Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float volume, ref Guid context);
        [PreserveSig] int GetMasterVolumeLevel(out float decibels);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float volume);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float level, ref Guid context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float level);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
    [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMeter
    {
        [PreserveSig] int GetPeakValue(out float peak);
        [PreserveSig] int GetMeteringChannelCount(out uint channels);
        [PreserveSig] int GetChannelsPeakValues(uint count, [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] float[] peaks);
        [PreserveSig] int QueryHardwareSupport(out uint support);
    }
    private IDeviceEnumerator? enumerator;
    private IDevice? device;
    private IVolume? volume;
    private IMeter? meter;
    private string? deviceId;
    private long nextDeviceCheck;
    internal AudioSample Read(bool includePeak)
    {
        try
        {
            EnsureDevice();
            if (volume == null) return Empty("没有默认多媒体输出设备");
            Marshal.ThrowExceptionForHR(volume.GetMasterVolumeLevelScalar(out float scalar));
            Marshal.ThrowExceptionForHR(volume.GetMute(out bool muted));
            if (!float.IsFinite(scalar)) return Empty("音量数据不可用");
            double? peak = null;
            if (includePeak && meter != null && meter.GetPeakValue(out float value) >= 0 && float.IsFinite(value)) peak = Math.Clamp(value, 0, 1);
            return new(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Math.Clamp(scalar * 100d, 0, 100), muted, peak, ToDbfs(peak), includePeak && !peak.HasValue ? "电平不可用" : "默认多媒体输出");
        }
        catch (COMException) { ClearDevice(); nextDeviceCheck = Environment.TickCount64 + 2000; return Empty("输出设备不可用"); }
    }
    internal void SetVolume(double percent)
    {
        if (!double.IsFinite(percent) || percent is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(percent));
        EnsureDevice(); if (volume == null) throw new InvalidOperationException("没有默认播放设备");
        var context = Guid.Empty; Marshal.ThrowExceptionForHR(volume.SetMasterVolumeLevelScalar((float)(percent / 100), ref context));
    }
    internal void SetMute(bool muted)
    {
        EnsureDevice(); if (volume == null) throw new InvalidOperationException("没有默认播放设备");
        var context = Guid.Empty; Marshal.ThrowExceptionForHR(volume.SetMute(muted, ref context));
    }
    internal static double? ToDbfs(double? peak) => peak is > 0 && double.IsFinite(peak.Value) ? 20 * Math.Log10(Math.Min(1, peak.Value)) : null;
    private static AudioSample Empty(string status) => new(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), null, false, null, null, status);
    private void EnsureDevice()
    {
        if (Environment.TickCount64 < nextDeviceCheck) return;
        nextDeviceCheck = Environment.TickCount64 + 2000;
        enumerator ??= (IDeviceEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"), true)!)!;
        Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(0, 1, out var candidate));
        try
        {
            Marshal.ThrowExceptionForHR(candidate.GetId(out string id));
            if (id == deviceId) return;
            ClearDevice();
            var volumeId = typeof(IVolume).GUID;
            Marshal.ThrowExceptionForHR(candidate.Activate(ref volumeId, 23, 0, out object volumeObject));
            volume = (IVolume)volumeObject;
            var meterId = typeof(IMeter).GUID;
            if (candidate.Activate(ref meterId, 23, 0, out object meterObject) >= 0) meter = (IMeter)meterObject;
            device = candidate; candidate = null!; deviceId = id;
        }
        finally { Release(candidate); }
    }
    private static void Release(object? value) { if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
    private void ClearDevice() { Release(meter); Release(volume); Release(device); meter = null; volume = null; device = null; deviceId = null; }
    public void Dispose() { ClearDevice(); Release(enumerator); enumerator = null; }
}
