using System.Diagnostics.Metrics;

namespace LoanApp.UnitTests;

internal sealed class TestMeterFactory : IMeterFactory
{
    private readonly List<Meter> _meters = [];

    public Meter Create(MeterOptions options)
    {
        var meter = new Meter(options);
        _meters.Add(meter);
        return meter;
    }

    public void Dispose() => _meters.ForEach(meter => meter.Dispose());
}
