using System.Collections.Generic;
using Verse;

namespace AutoColony
{
    public class AutoColonySettings : ModSettings
    {
        /// <summary>Master switch. When off the director does nothing at all.</summary>
        public bool masterEnabled = true;

        /// <summary>In-game days per learning epoch — the unit the strategy is scored over.</summary>
        public int epochDays = 10;

        /// <summary>Carry learned strategies between colonies via the on-disk archive.</summary>
        public bool shareAcrossSaves = true;

        public bool verboseLogging;

        /// <summary>Names of modules the player has switched off.</summary>
        public List<string> disabledModules = new List<string>();

        public bool IsModuleEnabled(string moduleName)
        {
            return disabledModules == null || !disabledModules.Contains(moduleName);
        }

        public void SetModuleEnabled(string moduleName, bool value)
        {
            if (disabledModules == null) disabledModules = new List<string>();
            if (value) disabledModules.Remove(moduleName);
            else if (!disabledModules.Contains(moduleName)) disabledModules.Add(moduleName);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref masterEnabled, "masterEnabled", true);
            Scribe_Values.Look(ref epochDays, "epochDays", 10);
            Scribe_Values.Look(ref shareAcrossSaves, "shareAcrossSaves", true);
            Scribe_Values.Look(ref verboseLogging, "verboseLogging", false);
            Scribe_Collections.Look(ref disabledModules, "disabledModules", LookMode.Value);
            if (disabledModules == null) disabledModules = new List<string>();
            AcLog.VerboseEnabled = verboseLogging;
        }
    }
}
