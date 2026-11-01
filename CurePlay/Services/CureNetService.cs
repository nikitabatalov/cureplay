using System.Diagnostics;
using System.Net.NetworkInformation;
using CurePlay.Models;

namespace CurePlay.Services;

public sealed class CureNetService : IDisposable
{
    private readonly object _gate = new();
    private readonly Queue<PingSample> _samples = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;
    private string _target;
    private bool _enabled;
    private int _outages;
    private int _consecutiveFailures;
    private bool _inOutage;
    private long _previousRx, _previousTx;
    private long _previousTicks;
    private double _downMbps, _upMbps;
    public event Action<string>? EventOccurred;

    public CureNetService(string target, bool enabled)
    {
        _target = target;
        _enabled = enabled;
        _loop = Task.Run(LoopAsync);
    }
    public void Configure(string target, bool enabled)
    {
        lock (_gate)
        {
            if (_target != target)
            {
                _samples.Clear(); _consecutiveFailures = 0; _inOutage = false; _outages = 0;
            }
            _target = target; _enabled = enabled;
        }
    }
    private async Task LoopAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        try
        {
            // First sample without waiting for the first tick.
            await ProbeAsync(_stop.Token);
            while (await timer.WaitForNextTickAsync(_stop.Token))
                await ProbeAsync(_stop.Token);
        }
        catch (OperationCanceledException) { }
    }
    private async Task ProbeAsync(CancellationToken token)
    {
        string target; bool enabled;
        lock (_gate) { target = _target; enabled = _enabled; }
        if (!enabled) return;
        double? latency = null;
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(target, 1400).WaitAsync(token);
            if (reply.Status == IPStatus.Success) latency = reply.RoundtripTime;
        }
        catch (OperationCanceledException) { throw; }
        catch { /* DNS resolution, timeout or ICMP restrictions are reported as unsuccessful probes. */ }
        string? message = null;
        lock (_gate)
        {
            if (target != _target || !_enabled) return; // Ignore a response from a previous target.
            _samples.Enqueue(new(DateTime.Now, latency));
            while (_samples.Count > 60) _samples.Dequeue();
            if (latency.HasValue)
            {
                _consecutiveFailures = 0;
                if (_inOutage) { _inOutage = false; message = $"CureNet: связь с {target} восстановлена (ICMP)."; }
            }
            else
            {
                _consecutiveFailures++;
                if (_consecutiveFailures >= 3 && !_inOutage)
                {
                    _inOutage = true; _outages++;
                    message = $"CureNet: 3 неуспешных ICMP-проверки {target}. Возможен обрыв или фильтрация ICMP.";
                }
            }
        }
        if (message is not null) EventOccurred?.Invoke(message);
    }
    private void MeasureTraffic()
    {
        try
        {
            long rx = 0, tx = 0;
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up ||
                    adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                var stats = adapter.GetIPv4Statistics();
                rx += stats.BytesReceived; tx += stats.BytesSent;
            }
            var ticks = Stopwatch.GetTimestamp();
            if (_previousTicks != 0 && ticks > _previousTicks && rx >= _previousRx && tx >= _previousTx)
            {
                var elapsed = (ticks - _previousTicks) / (double)Stopwatch.Frequency;
                if (elapsed > 0.25)
                {
                    _downMbps = (rx - _previousRx) * 8 / elapsed / 1_000_000;
                    _upMbps = (tx - _previousTx) * 8 / elapsed / 1_000_000;
                }
            }
            _previousRx = rx; _previousTx = tx; _previousTicks = ticks;
        }
        catch { /* adapter counters unavailable */ }
    }
    public NetworkSnapshot Read()
    {
        lock (_gate)
        {
            MeasureTraffic();
            if (!_enabled) return new(_target, null, null, 0, 0, _outages, _downMbps, _upMbps, "Мониторинг выключен");
            var recent = _samples.ToArray();
            var valid = recent.Where(x => x.LatencyMs.HasValue).Select(x => x.LatencyMs!.Value).ToArray();
            var jitter = valid.Length < 2 ? (double?)null : Enumerable.Range(1, valid.Length - 1)
                .Average(i => Math.Abs(valid[i] - valid[i - 1]));
            var last = recent.Length == 0 ? null : recent[^1];
            var status = recent.Length == 0 ? "Ожидание первого ответа" : _inOutage ? "Возможный обрыв / ICMP заблокирован" :
                last?.LatencyMs is null ? "Последний запрос без ответа" : "Ответ получен";
            return new(_target, last?.LatencyMs, jitter,
                recent.Length > 0 ? recent.Count(x => !x.LatencyMs.HasValue) * 100d / recent.Length : 0,
                recent.Length, _outages, _downMbps, _upMbps, status);
        }
    }
    public void Dispose()
    {
        _stop.Cancel();
        try { _loop.Wait(TimeSpan.FromSeconds(2)); } catch (AggregateException) { }
        _stop.Dispose();
    }
}
