using System.Collections.Concurrent;

namespace Plc.Runtime.Sim;

public sealed class SimulationService : IDisposable
{
    private readonly ConcurrentDictionary<string, double> _vars = new();
    private Timer? _timer;
    private int _periodMs = 100;

    public void Start(int periodMs = 100)
    {
        _periodMs = periodMs;
        _timer?.Dispose();
        _timer = new Timer(Tick, null, 0, _periodMs);
    }

    public void Stop() { _timer?.Dispose(); _timer = null; }

    private void Tick(object? state)
    {
        // 示例：x := x + 1;
        _vars.AddOrUpdate("x", 1, (_, v) => v + 1);
    }

    public IReadOnlyDictionary<string, double> Snapshot() => _vars;

    public void Set(string name, double value) => _vars[name] = value;

    public void Dispose() => Stop();
}

