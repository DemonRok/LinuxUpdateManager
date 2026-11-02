using System.Text;

namespace LinuxUpdateManager.App;

internal sealed class OperationOutputBuffer(int maximumLength = 250_000)
{
    private readonly object _sync = new();
    private readonly StringBuilder _builder = new();

    public void Append(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        lock (_sync)
        {
            var available = maximumLength - _builder.Length;
            if (available <= 0)
            {
                return;
            }

            _builder.Append(value.AsSpan(0, Math.Min(value.Length, available)));
        }
    }

    public override string ToString()
    {
        lock (_sync)
        {
            return _builder.ToString();
        }
    }
}
