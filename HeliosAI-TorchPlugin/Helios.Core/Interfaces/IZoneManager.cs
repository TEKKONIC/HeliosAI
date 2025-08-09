using System;
using System.Threading.Tasks;
using Torch.API;

namespace Helios.Core.Interfaces
{
    public interface IZoneManager : IDisposable
    {
        bool IsInitialized { get; }
        
        Task InitializeAsync(ITorchBase torch);
        Task LoadZonesAsync(string path);
        void Tick();
        void Shutdown();
    }
}