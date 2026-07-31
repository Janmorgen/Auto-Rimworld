using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace AutoColony
{
    /// <summary>
    /// Keeps the game running, and running fast, while the director is in charge — without
    /// overriding the player.
    ///
    /// An autonomous colony manager that cannot unpause stalls the moment RimWorld decides
    /// something deserves attention: a research project finishing, a raid landing. Left alone
    /// the game sits paused and the director never gets another tick.
    ///
    /// The distinction that matters is *who* paused. A pause the game raised for an event is
    /// the director's to clear. A pause the player pressed is an instruction to stop, and is
    /// left strictly alone until they resume — otherwise the mod fights anyone trying to look
    /// at their own colony.
    ///
    /// This runs on the frame hooks rather than the tick, because a paused game issues no
    /// ticks at all; a director acting only in <c>GameComponentTick</c> could never observe a
    /// pause, let alone undo one. That was measured, not assumed.
    ///
    /// Two different things stop the game and need different treatment:
    ///   - a time speed of Paused, fixed by setting it back;
    ///   - a modal window, which sets <see cref="TickManager.ForcePaused"/> so that the speed
    ///     is ignored entirely until the window is gone.
    ///
    /// Deliberately does not touch <c>Prefs.AutomaticPauseMode</c> or <c>Prefs.PauseOnLoad</c>.
    /// Those are persistent player settings in Prefs.xml; a mod that rewrites them leaves the
    /// game altered if it is ever removed. Correcting things afterwards leaves nothing behind.
    /// </summary>
    public static class TimeControl
    {
        /// <summary>
        /// How long an event pause stands before being undone, in real seconds. Not zero: a
        /// pause that vanished on the frame it appeared would make letters unreadable.
        /// </summary>
        public const float ResumeDelaySeconds = 1.25f;

        /// <summary>
        /// Longer grace for popups, since the player may have opened one to read it.
        /// </summary>
        public const float DialogDismissDelaySeconds = 3f;

        /// <summary>
        /// How recently a letter must have arrived for a pause to be blamed on it. RimWorld
        /// pauses on the same tick the letter lands, and ticks are frozen while paused, so
        /// this stays small in practice; the allowance is for the polling interval.
        /// </summary>
        const int EventPauseWindowTicks = 60;

        /// <summary>Real seconds between checks, so this costs nothing per frame.</summary>
        const float CheckInterval = 0.25f;

        /// <summary>
        /// How long after loading a save a pause is still assumed to be RimWorld's own.
        ///
        /// The game pauses itself after every load (<c>Prefs.PauseOnLoad</c>). That is neither
        /// an event nor the player reaching for the keyboard, so without this it is read as a
        /// manual pause and respected forever — the colony simply never starts. It also breaks
        /// training rounds, which reload between every trial.
        /// </summary>
        const float LoadPauseGraceSeconds = 30f;

        static float lastCheckTime;
        static float pausedSinceTime = -1f;
        static float loadedAtTime = -1f;
        static bool wasStoppedLastCheck;
        static bool currentPauseIsEventDriven;
        static bool respectingPlayerPause;

        public static int ResumesPerformed;
        public static int DialogsDismissed;
        public static string LastAction = "idle";

        /// <summary>True while the mod is deliberately keeping its hands off a manual pause.</summary>
        public static bool RespectingPlayerPause { get { return respectingPlayerPause; } }

        /// <summary>Called from the frame hooks, which run even while the game is paused.</summary>
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
            bool speedPaused = ticks.CurTimeSpeed == TimeSpeed.Paused;
            bool windowPaused = Find.WindowStack != null && Find.WindowStack.WindowsForcePause;

            if (!speedPaused && !windowPaused)
            {
                // Nothing is holding the game up. Forget any earlier pause and hold the speed.
                wasStoppedLastCheck = false;
                respectingPlayerPause = false;
                pausedSinceTime = -1f;

                var wanted = DesiredSpeed(settings);
                if (ticks.CurTimeSpeed != wanted)
                {
                    ticks.CurTimeSpeed = wanted;
                    LastAction = "set speed to " + wanted;
                }
                return;
            }

            // Something has stopped the game. Work out whose doing it was, once, on the edge.
            if (!wasStoppedLastCheck)
            {
                wasStoppedLastCheck = true;
                pausedSinceTime = now;

                // A popup is always something the game raised, as is the pause that follows
                // every load. A bare speed pause otherwise counts as the game's only if a
                // letter just landed; failing that, the player pressed pause.
                bool loadPause = loadedAtTime > 0f && now - loadedAtTime < LoadPauseGraceSeconds;
                currentPauseIsEventDriven = windowPaused || loadPause || ALetterJustArrived();
                respectingPlayerPause = !currentPauseIsEventDriven;

                if (respectingPlayerPause)
                {
                    LastAction = "paused by you — left alone";
                    AcLog.Verbose("Time control: this pause looks manual, leaving it alone");
                }
            }

            if (respectingPlayerPause) return;

            if (windowPaused)
            {
                if (settings.dismissPauseDialogs && now - pausedSinceTime >= DialogDismissDelaySeconds)
                    TryDismissBlockingDialog();
                return;
            }

            if (now - pausedSinceTime < ResumeDelaySeconds) return;

            var speed = DesiredSpeed(settings);
            ticks.CurTimeSpeed = speed;
            wasStoppedLastCheck = false;
            pausedSinceTime = -1f;
            ResumesPerformed++;
            LastAction = "resumed after an event pause";
            AcLog.Verbose("Time control: resumed the game at " + speed + " after an event pause");
        }

        /// <summary>
        /// Whether a letter landed just now, which is what an event-driven pause looks like.
        /// Ticks are frozen while paused, so the gap stays where it was at the moment of the
        /// pause rather than growing.
        /// </summary>
        static bool ALetterJustArrived()
        {
            var stack = Find.LetterStack;
            if (stack == null) return false;

            int now = Find.TickManager.TicksGame;
            var letters = stack.LettersListForReading;
            for (int i = 0; i < letters.Count; i++)
            {
                var letter = letters[i];
                if (letter == null) continue;
                if (now - letter.arrivalTick <= EventPauseWindowTicks) return true;
            }
            return false;
        }

        static TimeSpeed DesiredSpeed(AutoColonySettings settings)
        {
            switch (settings.maxSpeed)
            {
                case 0: return TimeSpeed.Normal;
                case 1: return TimeSpeed.Fast;
                case 3:
                    // Ultrafast is a development speed; the game only offers it with dev mode
                    // on, and it is unstable enough not to be reachable by accident.
                    return Prefs.DevMode ? TimeSpeed.Ultrafast : TimeSpeed.Superfast;
                default: return TimeSpeed.Superfast;
            }
        }

        /// <summary>
        /// Closes an event popup holding the game. Only the two window types RimWorld raises
        /// for events are touched, and while the options or any mod's settings screen is open
        /// nothing is touched at all — that is taken as the player driving.
        /// </summary>
        static void TryDismissBlockingDialog()
        {
            var stack = Find.WindowStack;
            if (stack == null) return;

            var windows = stack.Windows;
            for (int i = 0; i < windows.Count; i++)
            {
                if (windows[i] is Dialog_Options || windows[i] is Dialog_ModSettings) return;
            }

            for (int i = windows.Count - 1; i >= 0; i--)
            {
                var window = windows[i];
                if (window == null || !window.forcePause) continue;

                if (!(window is Dialog_NodeTree) && !(window is Dialog_MessageBox))
                {
                    // A window type not on the list will hold the game open indefinitely and
                    // there is no way to know from here whether closing it is safe. Name it
                    // once so the situation is diagnosable instead of looking like a hang.
                    AcLog.WarningOnce("blockingWindow:" + window.GetType().FullName,
                        "Game is held paused by " + window.GetType().FullName +
                        ", which Auto-Colony will not close. The colony will not advance " +
                        "until it is dismissed.");
                    continue;
                }

                stack.TryRemove(window, false);
                wasStoppedLastCheck = false;
                pausedSinceTime = -1f;
                DialogsDismissed++;
                LastAction = "dismissed a " + window.GetType().Name;
                AcLog.Verbose("Time control: dismissed " + window.GetType().Name + " holding the game paused");
                return;
            }
        }

        /// <summary>
        /// Called when a save finishes loading, so the pause RimWorld applies on load is not
        /// mistaken for the player having pressed pause.
        /// </summary>
        public static void NotifyGameLoaded()
        {
            Reset();
            loadedAtTime = Time.realtimeSinceStartup;
        }

        /// <summary>Clears per-game runtime state so nothing leaks across loads.</summary>
        public static void Reset()
        {
            pausedSinceTime = -1f;
            loadedAtTime = -1f;
            lastCheckTime = 0f;
            wasStoppedLastCheck = false;
            currentPauseIsEventDriven = false;
            respectingPlayerPause = false;
            ResumesPerformed = 0;
            DialogsDismissed = 0;
            LastAction = "idle";
        }
    }
}
