using System.Collections.Generic;
using AutoColony.Modules;

namespace AutoColony
{
    /// <summary>
    /// The one place the set of subsystems is declared.
    ///
    /// Both the director and the settings window read from here, so a module can never be
    /// added to the loop but left out of the settings list (or vice versa) — the settings
    /// toggles are always wired to real modules by construction.
    /// </summary>
    public static class DirectorModules
    {
        public static List<DirectorModule> CreateAll()
        {
            return new List<DirectorModule>
            {
                new WorkPriorityModule(),
                new ZoneModule(),
                new BasePlannerModule(),
                new ItemPolicyModule(),
                new ProductionModule(),
                new ResourceModule(),
                new ResearchModule(),
                new DefenseModule(),
                new EquipmentModule(),
                new PowerModule(),
                new ColonistPolicyModule(),
                new PrisonerModule(),
                new IncidentModule(),
                new TradeModule(),
                new UpkeepModule()
            };
        }

        static string[] cachedNames;

        /// <summary>Display names of every subsystem, usable before any game is loaded.</summary>
        public static string[] AllNames()
        {
            if (cachedNames != null) return cachedNames;

            var modules = CreateAll();
            cachedNames = new string[modules.Count];
            for (int i = 0; i < modules.Count; i++) cachedNames[i] = modules[i].Name;
            return cachedNames;
        }
    }
}
