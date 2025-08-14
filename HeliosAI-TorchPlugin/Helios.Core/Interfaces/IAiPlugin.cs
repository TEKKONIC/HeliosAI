using System;

namespace Helios.Core.Interfaces
{
    /// <summary>
    /// Extends AI with custom logic (behaviors, comms, analytics, etc.).
    /// </summary>
    public interface IAiPlugin : IDisposable
    {
        string Name { get; }
        void Initialize(IAiManager manager);
        void OnTick();
    }
}