using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;

namespace Test.Infrastructure;

public sealed class ControllerHost : IDisposable
{
    public required ControllerBase Controller { get; init; }
    public required List<IDisposable> Resources { get; init; } = [];

    public void Dispose()
    {
        foreach (var d in Resources)
        {
            try { d.Dispose(); } catch { /* ignore */ }
        }
    }
}
