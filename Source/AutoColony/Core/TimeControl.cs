using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace AutoColony
{
    /// <summary>
    /// Keeps the game running, and running fast, while the director is in charge.
    ///
    /// An autonomous colony manager that cannot unpause the game stalls the moment RimWorld
    /// decides something is worth the player's attention — a finished research project, a
    /// raid, a trade caravan. Left alone the game sits paused indefinitely and the director
    /// never gets another tick.
    ///
    /// That last point is the reason this lives on the frame update rather than the tick:
    /// <c>GameComponentTick</c> does not run while the game is paused, so a director that only
    /// acted on ticks could never resume itself. <c>GameComponentUpdate</c> runs every frame
    /// regardless, which is the only place a pause can be observed and undone.
    ///
    /// Two distinct things cause a stall and they need different handling:
    ///   - the time speed being set to Paused, which is fixed by setting it back;
    ///   - a modal window being open, which sets <see cref="TickManager.ForcePaused"/> so
    ///     that setting the speed does nothing at all until the window is closed.
    ///
    /// Deliberately does not touch <c>Prefs.AutomaticPauseMode</c> or <c>Prefs.PauseOnLoad</c>.
    /// Those are persistent player settings that live in Prefs.xml, and a mod that rewrites
    /// them leaves the player's game permanently altered if it is ever removed mid-session.
    /// Correcting the speed after the fact is idempotent and leaves nothing behind.
    /// </summary>
    public static class TimeControl
    {
        /// <summary>
        /// How long a pause is allowed to stand before being undone, in real seconds.
        ///
        /// Not zero: an event pause that vanished on the same frame it appeared would make
        /// letters unreadable and would fight a player who paused deliberately to look at
        /// something. A beat of visibility costs nothing at superfast speed.
        /// </summary>
        public const float ResumeDelaySeconds = 1.25f;

        /// <summary>Real seconds between corrections, so this costs nothing per frame.</summary>
        const float CheckInterval = 0.25f;

        static float lastCheckTime;
        static float pausedSinceTime = -1f;

        public static int ResumesPerformed;
        public static int DialogsDismissed;
        public static string LastAction = "idle";

        /// <summary>Called every frame while a game is running.</summary>
        public static void Update()
        {
            var settings = AutoColonyMod.Settings;
            if (settings == null || !settings.masterEnabled || !settings.controlTime) return;

            // Never fight the game while it is tearing itself down for a training reload.
            if (TrainingSession.ReloadPending) return;

            if (Current.ProgramState != ProgramState.Playing) return;
            if (Current.Game == null || Find.TickManager == null) return;
            if (Find.CurrentMap == null) return;

            float now = Time.realtimeSinceStartup;
            if (now - lastCheckTime < CheckInterval) return;
            lastCheckTime = now;

            try
            {
                Maintain(settings, now);
            }
            catch (Exception e)
            {
                AcLog.WarningOnce("timeControl", "Time control failed, leaving speed alone: " + e.Message);
            }
        }

        static void Maintain(AutoColonySettings settings, float now)
        {
            var ticks = Find.TickManager;

            // A modal window blocks ticking outright; the speed setting is irrelevant until
            // it is gone, so this has to be dealt with first.
            if (Find.WindowStack != null && Find.WindowStack.WindowsForcePause)
            {
                if (settings.dismissPauseDialogs) TryDismissBlockingDialog(now);
                return;
            }

            var desired = DesiredSpeed(settings);

            if (ticks.CurTimeSpeed == TimeSpeed.Paused)
            {
                // Let the pause stand briefly so the player can see what caused it.
                if (pausedSinceTime < 0f) pausedSinceTime = now;
                if (now - pausedSinceTime < ResumeDelaySeconds) return;

                ticks.CurTimeSpeed = desired;
                pausedSinceTime = -1f;
                ResumesPerformed++;
                LastAction = "resumed from a pause";
                AcLog.Verbose("Time control: resumed the game at " + desired);
                return;
            }

            pausedSinceTime = -1f;

            if (ticks.CurTimeSpeed != desired)
            {
                ticks.CurTimeSpeed = desired;
                LastAction = "set speed to " + desired;
            }
        }

        static TimeSpeed DesiredSpeed(AutoColonySettings settings)
        {
            switch (settings.maxSpeed)
            {
                case 0: return TimeSpeed.Normal;
                case 1: return TimeSpeed.Fast;
                case 3:
                    // Ultrafast is a development speed; the game only offers it with dev mode
                    // on, and it is unstable enough that it should not be reachable by accident.
                    return Prefs.DevMode ? TimeSpeed.Ultrafast : TimeSpeed.Superfast;
                default: return TimeSpeed.Superfast;
            }
        }

        /// <summary>
        /// Closes an event popup that is holding the game paused.
        ///
        /// Only the two window types RimWorld raises for events are ever touched. Closing
        /// windows indiscriminately would shut the options menu or this mod's own settings
        /// out from under the player, so anything the player plausibly opened themselves is
        /// treated as a signal to stop interfering entirely.
        /// </summary>
        static void TryDismissBlockingDialog(float now)
        {
            var stack = Find.WindowStack;
            if (stack == null) return;

            var windows = stack.Windows;

            // If a settings or options screen is up, the player is driving. Leave everything.
            for (int i = 0; i < windows.Count; i++)
            {
                if (windows[i] is Dialog_Options || windows[i] is Dialog_ModSettings) return;
            }

            // Same grace period as a speed pause, so an event dialog is readable before it goes.
            if (pausedSinceTime < 0f) pausedSinceTime = now;
            if (now - pausedSinceTime < ResumeDelaySeconds) return;

            for (int i = windows.Count - 1; i >= 0; i--)
            {
                var window = windows[i];
                if (window == null || !window.forcePause) continue;
                if (!(window is Dialog_NodeTree) && !(window is Dialog_MessageBox)) continue;

                stack.TryRemove(window, false);
                pausedSinceTime = -1f;
                DialogsDismissed++;
                LastAction = "dismissed a " + window.GetType().Name;
                AcLog.Verbose("Time control: dismissed " + window.GetType().Name + " holding the game paused");
                return;
            }
        }

        /// <summary>Clears per-game runtime state so counters do not leak across loads.</summary>
        public static void Reset()
        {
            pausedSinceTime = -1f;
            lastCheckTime = 0f;
            ResumesPerformed = 0;
            DialogsDismissed = 0;
            LastAction = "idle";
        }
    }
}
