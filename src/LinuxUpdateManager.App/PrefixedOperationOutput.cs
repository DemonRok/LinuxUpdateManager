using System.Text;

namespace LinuxUpdateManager.App;

internal sealed class PrefixedOperationOutput(string prefix, Action<string> outputReceived)
{
    private readonly object _sync = new();
    private readonly StringBuilder _line = new();
    private bool _pendingCarriageReturn;

    public void Append(string chunk)
    {
        if (string.IsNullOrEmpty(chunk))
        {
            return;
        }

        lock (_sync)
        {
            foreach (var character in chunk)
            {
                if (_pendingCarriageReturn)
                {
                    if (character == '\n')
                    {
                        Emit(Environment.NewLine);
                        _pendingCarriageReturn = false;
                        continue;
                    }

                    Emit(Environment.NewLine);
                    _pendingCarriageReturn = false;
                }

                if (character == '\r')
                {
                    _pendingCarriageReturn = true;
                }
                else if (character == '\n')
                {
                    Emit(Environment.NewLine);
                }
                else
                {
                    _line.Append(character);
                }
            }
        }
    }

    public void Flush()
    {
        lock (_sync)
        {
            if (_pendingCarriageReturn)
            {
                Emit(Environment.NewLine);
                _pendingCarriageReturn = false;
            }
            else if (_line.Length > 0)
            {
                Emit(Environment.NewLine);
            }
        }
    }

    private void Emit(string lineEnding)
    {
        outputReceived($"[{prefix}] {_line}{lineEnding}");
        _line.Clear();
    }
}
