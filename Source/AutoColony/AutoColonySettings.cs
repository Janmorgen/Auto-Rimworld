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

        /// <summary>
        /// Repeatedly snapshot the game and replay the same stretch of time once per candidate
        /// strategy. Learns far faster, but the game visibly reloads between trials.
        /// </summary>
        public bool trainingMode;

        /// <summary>Candidate strategies per training round (the incumbent is always one).</summary>
        public int trialCandidates = 4;

        /// <summary>Fit a starting strategy by watching how the player runs the colony.</summary>
        public bool learnFromPlayer = true;

        /// <summary>
        /// Keep the game unpaused and running fast. Without this the director stalls whenever
        /// RimWorld auto-pauses for an event, since a paused game stops ticking entirely.
        /// </summary>
        public bool controlTime = true;

        /// <summary>0 = Normal, 1 = Fast, 2 = Superfast, 3 = Ultrafast (needs dev mode).</summary>
        public int maxSpeed = 2;

        /// <summary>Close event popups that hold the game paused (research completed, and similar).</summary>
        public bool dismissPauseDialogs = true;

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
            Scribe_Values.Look(ref trainingMode, "trainingMode", false);
            Scribe_Values.Look(ref trialCandidates, "trialCandidates", 4);
            Scribe_Values.Look(ref learnFromPlayer, "learnFromPlayer", true);
            Scribe_Values.Look(ref controlTime, "controlTime", true);
            Scribe_Values.Look(ref maxSpeed, "maxSpeed", 2);
            Scribe_Values.Look(ref dismissPauseDialogs, "dismissPauseDialogs", true);
            Scribe_Values.Look(ref verboseLogging, "verboseLogging", false);
            Scribe_Collections.Look(ref disabledModules, "disabledModules", LookMode.Value);
            if (disabledModules == null) disabledModules = new List<string>();
            AcLog.VerboseEnabled = verboseLogging;
        }
    }
}
