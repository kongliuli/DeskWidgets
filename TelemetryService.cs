using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace DeskWidgets;

internal sealed class TelemetryService : IDisposable
{
    private readonly Dispatcher dispatcher;
    private readonly Dictionary<WidgetWindow, MetricChannels> metricClients = [];
    private readonly Dictionary<WidgetWindow, bool> audioClients = [];
    private readonly SemaphoreSlim wake = new(0, 1);
    private readonly CancellationTokenSource stop = new();
    private Task? worker;
    private int wanted, desiredVolume = -1, desiredMute = -1;
    private bool disposed;
    internal int SubscriberCount => metricClients.Count + audioClients.Count;
    internal TelemetryService(Dispatcher dispatcher) => this.dispatcher = dispatcher;
    internal void SubscribeMetrics(WidgetWindow window, MetricChannels channels)
    {
        if (disposed) return;
        if (channels == MetricChannels.None) metricClients.Remove(window); else metricClients[window] = channels;
        RefreshDemand();
    }
    internal void SubscribeAudio(WidgetWindow window, bool enabled, bool meter)
    {
        if (disposed) return;
        if (enabled) audioClients[window] = meter; else audioClients.Remove(window);
        RefreshDemand();
    }
    internal void Remove(WidgetWindow window) { metricClients.Remove(window); audioClients.Remove(window); RefreshDemand(); }
    internal void SetVolume(WidgetWindow window, double value)
    {
        if (App.IsSelfTest && double.IsFinite(value) && value is >= 0 and <= 100) { window.PostTelemetry(new { type = "audioCommandPreview", volume = value }); return; }
        if (!disposed && audioClients.ContainsKey(window) && double.IsFinite(value) && value is >= 0 and <= 100) { Interlocked.Exchange(ref desiredVolume, (int)Math.Round(value)); Signal(); }
    }
    internal void SetMute(WidgetWindow window, bool muted)
    {
        if (App.IsSelfTest) { window.PostTelemetry(new { type = "audioCommandPreview", muted }); return; }
        if (!disposed && audioClients.ContainsKey(window)) { Interlocked.Exchange(ref desiredMute, muted ? 1 : 0); Signal(); }
    }
    private void RefreshDemand()
    {
        if (disposed) return;
        int mask = metricClients.Values.Aggregate(0, (value, channel) => value | (int)channel);
        if (audioClients.Count > 0) mask |= 8;
        else { Interlocked.Exchange(ref desiredVolume, -1); Interlocked.Exchange(ref desiredMute, -1); }
        if (audioClients.Values.Any(meter => meter)) mask |= 16;
        Volatile.Write(ref wanted, mask);
        if (mask != 0) worker ??= Task.Run(Run);
        Signal();
    }
    private void Signal() { if (!disposed && wake.CurrentCount == 0) wake.Release(); }
    private async Task Run()
    {
        SystemMetricsSampler? metrics = null;
        AudioEndpoint? audio = null;
        long nextMetrics = 0, nextAudio = 0;
        AudioSample? previousAudio = null;
        long lastAudioPush = 0;
        try
        {
            while (!stop.IsCancellationRequested)
            {
                int mask = Volatile.Read(ref wanted);
                if (mask == 0)
                {
                    metrics?.Dispose(); metrics = null; audio?.Dispose(); audio = null;
                    nextMetrics = 0; nextAudio = 0;
                    await wake.WaitAsync(stop.Token); continue;
                }
                long now = Environment.TickCount64;
                if ((mask & 7) != 0)
                {
                    metrics ??= new SystemMetricsSampler();
                    if (now >= nextMetrics)
                    {
                        var sample = metrics.Read((MetricChannels)(mask & 7));
                        nextMetrics = now + 2000;
                        _ = dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
                        {
                            if (disposed) return;
                            foreach (var window in metricClients.Keys.ToArray()) window.PostTelemetry(new { type = "systemMetrics", sample, intervalMs = 2000 });
                        });
                    }
                }
                else { metrics?.Dispose(); metrics = null; nextMetrics = 0; }
                if ((mask & 8) != 0)
                {
                    audio ??= new AudioEndpoint();
                    int volume = Interlocked.Exchange(ref desiredVolume, -1), mute = Interlocked.Exchange(ref desiredMute, -1);
                    try { if (volume >= 0) audio.SetVolume(volume); if (mute >= 0) audio.SetMute(mute != 0); }
                    catch (Exception error) when (error is COMException or InvalidOperationException)
                    {
                        _ = dispatcher.BeginInvoke(DispatcherPriority.Background, () => { foreach (var window in audioClients.Keys.ToArray()) window.PostTelemetry(new { type = "audioError", message = "音量调整失败，请检查默认输出设备" }); });
                    }
                    if (now >= nextAudio || volume >= 0 || mute >= 0)
                    {
                        var sample = audio.Read((mask & 16) != 0);
                        nextAudio = now + ((mask & 16) != 0 ? 100 : 1000);
                        bool changed = previousAudio == null || sample.Volume != previousAudio.Volume || sample.Muted != previousAudio.Muted || sample.Status != previousAudio.Status || Math.Round(sample.Peak ?? -1, 4) != Math.Round(previousAudio.Peak ?? -1, 4);
                        if (changed || now - lastAudioPush >= 1000 || volume >= 0 || mute >= 0)
                        {
                            previousAudio = sample; lastAudioPush = now;
                            _ = dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
                            {
                                if (disposed) return;
                                foreach (var window in audioClients.Keys.ToArray()) window.PostTelemetry(new { type = "audioMetrics", sample });
                            });
                        }
                    }
                }
                else { audio?.Dispose(); audio = null; nextAudio = 0; Interlocked.Exchange(ref desiredVolume, -1); Interlocked.Exchange(ref desiredMute, -1); }
                long deadline = nextMetrics == 0 ? nextAudio : nextAudio == 0 ? nextMetrics : Math.Min(nextMetrics, nextAudio);
                int delay = (int)Math.Clamp(deadline - Environment.TickCount64, 10, 2000);
                await wake.WaitAsync(delay, stop.Token);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        finally { metrics?.Dispose(); audio?.Dispose(); }
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; metricClients.Clear(); audioClients.Clear(); Volatile.Write(ref wanted, 0); stop.Cancel();
        if (worker == null) { wake.Dispose(); stop.Dispose(); }
        else _ = worker.ContinueWith(_ => { wake.Dispose(); stop.Dispose(); }, TaskScheduler.Default);
    }
}
