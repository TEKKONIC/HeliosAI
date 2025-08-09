using System;
using System.Threading.Tasks;
using Torch.API;

namespace Helios.Core.Interfaces
{
    public interface IHeliosModule : IDisposable
    {
        string Name { get; }
        Version Version { get; }
        bool IsInitialized { get; }
        
        Task InitializeAsync(ITorchBase torch);
        void Shutdown();
    }
}