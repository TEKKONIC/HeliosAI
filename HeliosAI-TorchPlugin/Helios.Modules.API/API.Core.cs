using HeliosAI;
using HeliosAI.API;
using NexusModAPI;


namespace Helios.Modules.API {
    public class APIManager
    {
        // WeaponCore
        public static readonly WeaponCoreAdvancedAPI WeaponCore = new();
        public static WeaponCoreGridManager WeaponCoreManager;
        public static bool WeaponCoreApiLoaded = false;

        // Shields
        public static readonly ShieldApi Shields = new ShieldApi();
        public static bool ShieldsApiLoaded = false;

        // Nexus
        public static NexusAPI Nexus;
        public static bool NexusApiLoaded => Nexus?.Enabled ?? false;

        // Add more APIs as needed...

        public static void RegisterAPIs(int phase = 0)
        {
            // WeaponCore
            if (phase == 1)
            {
                WeaponCore.LoadWeaponCoreAPI();
                WeaponCoreApiLoaded = WeaponCore.IsReady;
                if (WeaponCoreApiLoaded && WeaponCoreManager == null)
                    WeaponCoreManager = new WeaponCoreGridManager(WeaponCore);
            }

            // Shields 
            if (phase == 2)
            {
                Shields.Load();
                ShieldsApiLoaded = Shields.IsReady;
            }

            // Nexus 
            if (phase == 0 && Nexus == null)
            {
                Nexus = new NexusAPI();
            }
        }
    }
}
