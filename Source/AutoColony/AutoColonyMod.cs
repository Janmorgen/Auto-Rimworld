using AutoColony.Learning;
using RimWorld;
using UnityEngine;
using Verse;

namespace AutoColony
{
    public class AutoColonyMod : Mod
    {
        public static AutoColonySettings Settings;

        public AutoColonyMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<AutoColonySettings>();
            AcLog.VerboseEnabled = Settings.verboseLogging;
        }

        public override string SettingsCategory()
        {
            return "Auto-Colony";
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            var listing = new Listing_Standard();
            listing.Begin(inRect);

            listing.CheckboxLabeled("Run the colony automatically", ref Settings.masterEnabled,
                "When off, the director takes no actions and no learning happens.");

            listing.Gap(6f);

            float days = Settings.epochDays;
            days = listing.SliderLabeled("Epoch length: " + Settings.epochDays + " days", days, 3f, 30f,
                0.5f, "How long each strategy is trialled before being scored. Shorter epochs learn " +
                      "faster but the score is noisier.");
            Settings.epochDays = Mathf.RoundToInt(days);

            listing.Gap(6f);

            listing.CheckboxLabeled("Carry learning between colonies", ref Settings.shareAcrossSaves,
                "Stores the best strategy found in a file alongside your saves, and seeds new " +
                "colonies from it. This is what lets the mod improve over many playthroughs.");

            listing.CheckboxLabeled("Learn from how you play", ref Settings.learnFromPlayer,
                "While automation is off, watches how you assign work, what you stockpile and " +
                "how you build, and fits a starting strategy to it. When you hand the colony " +
                "over it begins from your habits instead of from defaults.");

            listing.Gap(10f);
            listing.Label("Training mode");
            Text.Font = GameFont.Tiny;
            listing.Label(
                "A colony score is far noisier than the difference between two decent strategies, " +
                "so judging one strategy per epoch barely learns anything. Training mode snapshots " +
                "the game and replays the same stretch of time once per candidate, so every " +
                "candidate meets the same raids and weather and the comparison is about strategy " +
                "rather than luck. The game visibly reloads between trials, and the colony only " +
                "advances on alternate epochs.");
            Text.Font = GameFont.Small;

            listing.CheckboxLabeled("Run training rounds", ref Settings.trainingMode,
                "Requires a non-permadeath save. Uses a dedicated save slot and never touches " +
                "your own saves.");

            if (Settings.trainingMode)
            {
                float candidates = Settings.trialCandidates;
                candidates = listing.SliderLabeled(
                    "Candidates per round: " + Settings.trialCandidates, candidates, 2f, 8f, 0.5f,
                    "More candidates give a cleaner comparison but each round costs that many " +
                    "replays of the same stretch of time.");
                Settings.trialCandidates = Mathf.RoundToInt(candidates);
            }

            listing.CheckboxLabeled("Verbose logging", ref Settings.verboseLogging,
                "Logs every action the director takes. Useful for debugging, noisy otherwise.");
            AcLog.VerboseEnabled = Settings.verboseLogging;

            listing.Gap(10f);
            listing.Label("Subsystems");
            listing.Label("Turn one off to take that part of colony management back into your own hands.");

            foreach (var name in DirectorModules.AllNames())
            {
                bool on = Settings.IsModuleEnabled(name);
                bool before = on;
                listing.CheckboxLabeled("    " + name, ref on);
                if (on != before) Settings.SetModuleEnabled(name, on);
            }

            listing.Gap(12f);

            if (listing.ButtonText("Erase cross-colony learning"))
            {
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    "This permanently deletes every strategy Auto-Colony has learned across all " +
                    "your colonies. Learning in the current save is kept. Continue?",
                    StrategyArchive.ResetAll, true));
            }

            var archivePath = StrategyArchive.ArchivePath;
            if (!string.IsNullOrEmpty(archivePath))
            {
                Text.Font = GameFont.Tiny;
                listing.Label("Archive: " + archivePath);
                Text.Font = GameFont.Small;
            }

            listing.End();
        }

        public override void WriteSettings()
        {
            base.WriteSettings();
            AcLog.VerboseEnabled = Settings.verboseLogging;
        }
    }

    /// <summary>
    /// Registers one tunable weight per work type once defs are loaded, so the strategy
    /// search covers work types added by other mods without any hardcoding.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class AutoColonyStartup
    {
        static AutoColonyStartup()
        {
            int n = 0;
            var workTypes = DefDatabase<WorkTypeDef>.AllDefsListForReading;
            for (int i = 0; i < workTypes.Count; i++)
            {
                var wt = workTypes[i];
                if (wt == null || !wt.visible) continue;
                Genes.RegisterWorkType(wt.defName, "Work: " + wt.labelShort.CapitalizeFirst());
                n++;
            }
            AcLog.Message("Ready. Strategy space: " + Genes.All.Count + " genes (" + n + " work types).");
        }
    }
}
