using System;
using System.Collections.Generic;
using Man10BankService.Controllers;

namespace Test.Infrastructure;

public sealed class ControllerHost : IDisposable
{
    public required BankController Controller { get; init; }
    public required List<IDisposable> Resources { get; init; } = [];

    public void Dispose()
    {
        foreach (var d in Resources)
        {
            try { d.Dispose(); } catch { /* ignore */ }
        }
    }
}
