using System.Collections.Generic;
using AutoColony.Learning;
using RimWorld;
using UnityEngine;
using Verse;

namespace AutoColony.UI
{
    /// <summary>
    /// Status panel for the director.
    ///
    /// An autonomous agent that cannot be inspected is impossible to trust or debug, so this
    /// exposes the whole decision loop: what the colony currently scores and why, which
    /// strategy is being trialled, whether the search is improving, and what each subsystem
    /// last did.
    /// </summary>
    public class MainTabWindow_AutoColony : MainTabWindow
    {
        Vector2 scroll;

        public override Vector2 RequestedTabSize { get { return new Vector2(760f, 640f); } }

        static readonly Color GoodColor = new Color(0.45f, 0.78f, 0.45f);
        static readonly Color BadColor = new Color(0.80f, 0.42f, 0.38f);
        static readonly Color NeutralColor = new Color(0.55f, 0.60f, 0.70f);
        static readonly Color AcceptedColor = new Color(0.95f, 0.80f, 0.35f);

        public override void DoWindowContents(Rect inRect)
        {
            var director = Current.Game != null ? Current.Game.GetComponent<AutoColonyDirector>() : null;
            if (director == null)
            {
                Widgets.Label(inRect, "Auto-Colony is not active in this game.");
                return;
            }

            var viewRect = new Rect(0f, 0f, inRect.width - 20f, 1500f);
            Widgets.BeginScrollView(inRect, ref scroll, viewRect);

            var listing = new Listing_Standard();
            listing.Begin(viewRect);

            DrawHeader(listing, director);
            listing.GapLine(12f);

            DrawScore(listing, director);
            listing.GapLine(12f);

            DrawLearning(listing, director);
            listing.GapLine(12f);

            DrawHistory(listing, director);
            listing.GapLine(12f);

            DrawModules(listing, director);
            listing.GapLine(12f);

            DrawStrategy(listing, director);

            listing.End();
            Widgets.EndScrollView();
        }

        // ------------------------------------------------------------ sections

        static void DrawHeader(Listing_Standard listing, AutoColonyDirector director)
        {
            Text.Font = GameFont.Medium;
            listing.Label("Auto-Colony director");
            Text.Font = GameFont.Small;

            var settings = AutoColonyMod.Settings;
            var state = director.LastState;

            if (settings == null || !settings.masterEnabled)
            {
                GUI.color = BadColor;
                listing.Label("Paused — enable it in mod settings to hand the colony over.");
                GUI.color = Color.white;

                DrawObservationProgress(listing, director, settings);
                return;
            }

            if (settings.controlTime)
            {
                Text.Font = GameFont.Tiny;
                listing.Label("Time control: " + TimeControl.LastAction +
                              "  ·  " + TimeControl.ResumesPerformed + " pauses undone, " +
                              TimeControl.DialogsDismissed + " popups dismissed");
                Text.Font = GameFont.Small;
            }

            if (TrainingSession.Active)
            {
                GUI.color = AcceptedColor;
                listing.Label("Training: " + TrainingSession.StatusLine +
                              " — the game reloads between trials so every candidate faces the same world.");
                GUI.color = Color.white;
            }

            var evo = director.evolution;
            string phase = evo.phase == EpochPhase.Challenger
                ? "trialling a new strategy"
                : "re-measuring the current best";

            listing.Label("Epoch " + evo.epochIndex + " — " + phase);

            if (state != null && state.Valid)
            {
                listing.Label(string.Format(
                    "Day {0} · {1} colonists · {2:0.0} days of food · wealth {3:N0} · {4}",
                    state.day, state.colonists, state.daysOfFood, state.wealthTotal,
                    state.danger == StoryDanger.None ? "no active threat" : state.danger + " danger"));
            }
        }

        /// <summary>
        /// Shown while automation is off: how far the mod has got towards a strategy fitted
        /// to this player's own habits.
        /// </summary>
        static void DrawObservationProgress(Listing_Standard listing, AutoColonyDirector director,
                                            AutoColonySettings settings)
        {
            if (settings == null || !settings.learnFromPlayer) return;

            var model = director.playerModel;
            if (model == null) return;

            listing.Gap(8f);
            listing.Label("Learning from how you play");

            var row = listing.GetRect(22f);
            Widgets.Label(new Rect(row.x, row.y, 130f, row.height), "Observations");
            var bar = new Rect(row.x + 135f, row.y + 4f, row.width - 260f, 14f);
            DrawBar(bar, model.Progress, model.IsUsable ? GoodColor : NeutralColor);

            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(bar.xMax + 8f, row.y, 200f, row.height),
                model.samples + " / " + PlayerModel.MinSamples);
            Text.Font = GameFont.Small;

            listing.Label(model.IsUsable
                ? "Enough has been seen. Switching automation on will start from your habits rather than from defaults."
                : "Still watching. One observation per in-game hour.");

            if (model.IsUsable)
            {
                Text.Font = GameFont.Tiny;
                listing.Label(model.ToGenome().Summarize(10));
                Text.Font = GameFont.Small;
            }
        }

        static void DrawScore(Listing_Standard listing, AutoColonyDirector director)
        {
            Text.Font = GameFont.Medium;
            listing.Label("Last epoch score");
            Text.Font = GameFont.Small;

            if (float.IsNaN(director.lastScore))
            {
                listing.Label("No epoch has finished yet. The first score arrives after " +
                              AutoColonyMod.Settings.epochDays + " in-game days.");
                return;
            }

            listing.Label("Overall: " + director.lastScore.ToString("0.000"));

            var breakdown = director.LastBreakdown;
            if (breakdown == null) return;

            for (int i = 0; i < breakdown.Count; i++)
            {
                var term = breakdown[i];
                var row = listing.GetRect(22f);

                var labelRect = new Rect(row.x, row.y, 130f, row.height);
                Widgets.Label(labelRect, term.name);

                var barRect = new Rect(row.x + 135f, row.y + 4f, row.width - 260f, 14f);
                DrawBar(barRect, term.raw, ColorForFraction(term.raw));

                var valueRect = new Rect(barRect.xMax + 8f, row.y, 120f, row.height);
                Text.Font = GameFont.Tiny;
                Widgets.Label(valueRect, string.Format("{0:0.00} × {1:0.00} weight", term.raw, term.weight));
                Text.Font = GameFont.Small;
            }
        }

        static void DrawLearning(Listing_Standard listing, AutoColonyDirector director)
        {
            var evo = director.evolution;

            Text.Font = GameFont.Medium;
            listing.Label("Search state");
            Text.Font = GameFont.Small;

            listing.Label("Current best score: " + Fmt(evo.incumbentScore) +
                          "   (measured " + evo.incumbentSamples + "×)");
            listing.Label("Best ever: " + Fmt(evo.bestEverScore));

            // How noisy the score is decides whether the search can see anything at all, so
            // it is worth surfacing rather than hiding in the algorithm.
            if (!float.IsNaN(evo.noiseEstimate))
            {
                listing.Label("Score noise: ±" + evo.noiseEstimate.ToString("0.000") +
                              "   (a challenger must beat the incumbent by " +
                              evo.AcceptanceMargin.ToString("0.000") + ")");
            }
            listing.Label("Improvements accepted: " + evo.acceptedCount + " of " + evo.epochIndex + " epochs");
            listing.Label("Strategy generation: " + evo.Incumbent.generation);

            // Step size tells the player whether the search is still exploring or has settled.
            var row = listing.GetRect(22f);
            Widgets.Label(new Rect(row.x, row.y, 130f, row.height), "Mutation step");
            var bar = new Rect(row.x + 135f, row.y + 4f, row.width - 260f, 14f);
            DrawBar(bar, evo.sigma / 0.6f, NeutralColor);
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(bar.xMax + 8f, row.y, 160f, row.height),
                evo.sigma.ToString("0.000") + (evo.sigma > 0.2f ? " — exploring" : " — refining"));
            Text.Font = GameFont.Small;
        }

        static void DrawHistory(Listing_Standard listing, AutoColonyDirector director)
        {
            var history = director.evolution.history;

            Text.Font = GameFont.Medium;
            listing.Label("Score history");
            Text.Font = GameFont.Small;

            if (history == null || history.Count == 0)
            {
                listing.Label("Nothing recorded yet.");
                return;
            }

            var area = listing.GetRect(90f);
            Widgets.DrawBoxSolid(area, new Color(0.12f, 0.12f, 0.14f));

            float slot = area.width / Mathf.Max(history.Count, 1);
            float barWidth = Mathf.Max(2f, slot - 2f);

            for (int i = 0; i < history.Count; i++)
            {
                var rec = history[i];
                float h = Mathf.Clamp01(rec.score) * (area.height - 6f);
                var bar = new Rect(area.x + i * slot + 1f, area.yMax - 3f - h, barWidth, h);
                // Highlight the epochs where a challenger actually beat the incumbent.
                Widgets.DrawBoxSolid(bar, rec.accepted ? AcceptedColor : ColorForFraction(rec.score));
            }

            Text.Font = GameFont.Tiny;
            listing.Label("Each bar is one epoch; gold bars are strategies that beat the previous best. " +
                          "Recent average: " + Fmt(director.evolution.RecentAverage(10)));
            Text.Font = GameFont.Small;
        }

        static void DrawModules(Listing_Standard listing, AutoColonyDirector director)
        {
            Text.Font = GameFont.Medium;
            listing.Label("Subsystems");
            Text.Font = GameFont.Small;

            var modules = director.Modules;
            var settings = AutoColonyMod.Settings;

            for (int i = 0; i < modules.Count; i++)
            {
                var m = modules[i];
                var row = listing.GetRect(20f);

                bool on = settings.IsModuleEnabled(m.Name) && m.enabled && m.failures < DirectorModule.MaxFailures;
                GUI.color = on ? Color.white : BadColor;
                Widgets.Label(new Rect(row.x, row.y, 150f, row.height), m.Name);

                string status = !settings.IsModuleEnabled(m.Name)
                    ? "off (your setting)"
                    : m.failures >= DirectorModule.MaxFailures
                        ? "disabled after errors"
                        : m.lastAction;

                Text.Font = GameFont.Tiny;
                Widgets.Label(new Rect(row.x + 155f, row.y + 2f, row.width - 230f, row.height), status);
                Widgets.Label(new Rect(row.xMax - 70f, row.y + 2f, 70f, row.height), m.actionsTaken + " acts");
                Text.Font = GameFont.Small;
                GUI.color = Color.white;
            }
        }

        static void DrawStrategy(Listing_Standard listing, AutoColonyDirector director)
        {
            Text.Font = GameFont.Medium;
            listing.Label("Learned strategy");
            Text.Font = GameFont.Small;

            listing.Label("Genes that have moved away from their defaults:");
            Text.Font = GameFont.Tiny;
            listing.Label(director.evolution.Incumbent.Summarize(14));
            Text.Font = GameFont.Small;

            DrawTopArms(listing, director, Modules.ResearchModule.BanditId, "Best-rated research");
            DrawTopArms(listing, director, Modules.BasePlannerModule.BanditId, "Best-rated rooms");
            DrawTopArms(listing, director, Modules.ZoneModule.BanditId, "Best-rated crops");
        }

        static void DrawTopArms(Listing_Standard listing, AutoColonyDirector director, string banditId, string title)
        {
            var bandit = director.BanditFor(banditId);
            var best = new List<BanditArm>();
            foreach (var arm in bandit.Arms)
                if (arm.rawPulls > 0) best.Add(arm);

            if (best.Count == 0) return;

            best.Sort((a, b) => b.Mean.CompareTo(a.Mean));

            listing.Label(title + ":");
            Text.Font = GameFont.Tiny;
            int shown = Mathf.Min(4, best.Count);
            for (int i = 0; i < shown; i++)
            {
                listing.Label("    " + best[i].key + " — " + best[i].Mean.ToString("0.000") +
                              " over " + best[i].rawPulls + " tries");
            }
            Text.Font = GameFont.Small;
        }

        // ------------------------------------------------------------ helpers

        static void DrawBar(Rect rect, float fraction, Color color)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.16f, 0.16f, 0.18f));
            float f = Mathf.Clamp01(fraction);
            if (f > 0f)
                Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, rect.width * f, rect.height), color);
        }

        static Color ColorForFraction(float f)
        {
            if (float.IsNaN(f)) return NeutralColor;
            return f >= 0.66f ? GoodColor : (f >= 0.33f ? NeutralColor : BadColor);
        }

        static string Fmt(float value)
        {
            return float.IsNaN(value) ? "not measured yet" : value.ToString("0.000");
        }
    }
}
