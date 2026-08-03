using System;
using System.Collections.Generic;
using AutoColony.Learning;
using RimWorld;
using Verse;

namespace AutoColony.Modules
{
    /// <summary>
    /// Answers the decisions the game puts in front of the player: refugees asking for shelter,
    /// traders offering deals, quests wanting a yes or no.
    ///
    /// Without this the colony still runs but every offer expires unanswered, so this is the
    /// piece that actually removes the player from the loop. The accept/decline threshold is a
    /// gene: a cautious strategy turns away strangers and keeps a quiet colony, a bold one takes
    /// the bodies and the risk that comes with them, and which pays off is what gets measured.
    /// </summary>
    public class IncidentModule : DirectorModule
    {
        public override string Name { get { return "Incidents"; } }
        public override int IntervalTicks { get { return 2500; } }

        /// <summary>
        /// Leave a decision on screen this long before answering it, so a watching player
        /// still gets a chance to read it and step in.
        /// </summary>
        const int GraceTicks = 2500;

        static readonly string[] AcceptKeys = { "accept", "yes", "agree", "allow", "take", "join", "hire" };
        static readonly string[] DeclineKeys = { "reject", "decline", "refuse", "no,", "deny", "turn away" };

        /// <summary>
        /// Options that navigate rather than decide. Activating one of these would open a tab
        /// or move the camera and leave the actual decision unanswered, so they are never chosen.
        /// </summary>
        static readonly string[] NavigationKeys = { "jump to", "view in", "postpone", "read more", "info" };

        readonly List<Letter> pending = new List<Letter>();

        protected override void Act(DirectorContext ctx)
        {
            var stack = Find.LetterStack;
            if (stack == null) return;

            pending.Clear();
            pending.AddRange(stack.LettersListForReading);

            int tick = Find.TickManager.TicksGame;
            float risk = ctx.Gene(Genes.IncidentRiskTolerance);
            int handled = 0;

            for (int i = 0; i < pending.Count; i++)
            {
                var choice = pending[i] as ChoiceLetter;
                if (choice == null) continue;
                if (tick - choice.arrivalTick < GraceTicks) continue;

                if (Resolve(choice, risk, ctx)) handled++;
                if (handled >= 3) break;   // spread the work over successive passes
            }

            if (handled > 0) Note("answered " + handled + " pending decisions");
        }

        bool Resolve(ChoiceLetter letter, float risk, DirectorContext ctx)
        {
            DiaOption accept = null;
            DiaOption decline = null;
            DiaOption fallback = null;

            try
            {
                foreach (var option in letter.Choices)
                {
                    if (option == null || option.disabled) continue;
                    var text = OptionText(option);
                    if (MatchesAny(text, NavigationKeys)) continue;

                    if (accept == null && MatchesAny(text, AcceptKeys)) accept = option;
                    else if (decline == null && MatchesAny(text, DeclineKeys)) decline = option;
                    else if (fallback == null) fallback = option;
                }
            }
            catch (Exception e)
            {
                AcLog.WarningOnce("choiceEnum", "Could not read letter options: " + e.Message);
                return false;
            }

            // Decide with the colony's actual capacity in mind, not just the gene: taking in
            // more mouths during a food crisis is how a strategy loses colonists.
            float effectiveRisk = risk;
            if (ctx.state.daysOfFood < ctx.Gene(Genes.FoodDaysPerColonist) * 0.5f) effectiveRisk *= 0.4f;
            if (ctx.state.danger != StoryDanger.None) effectiveRisk *= 0.5f;

            var chosen = accept != null && effectiveRisk >= 0.5f
                ? accept
                : (decline ?? fallback);

            if (chosen == null)
            {
                // Nothing actionable left; clearing it keeps the stack from growing forever.
                Find.LetterStack.RemoveLetter(letter);
                return true;
            }

            try
            {
                if (chosen.action != null) chosen.action();

                // Put the screen back the way it was found.
                //
                // Some options do more than decide something — a quest letter's only real
                // option is "view quest", whose action switches to the Quests tab and leaves it
                // there. Nothing was closing it, so the director opened a full-screen panel over
                // the colony on day one and every screenshot after that was a picture of a
                // quest description. It changes nothing about how the colony is played and
                // everything about whether anybody can watch it being played.
                CloseAnythingTheOptionOpened();

                Find.LetterStack.RemoveLetter(letter);
                AcLog.Verbose("Answered '" + letter.Label + "' with '" + OptionText(chosen) + "'");
                Chronicle.Record(ChronicleCategory.Incident,
                    "answered '" + letter.Label + "' with '" + OptionText(chosen) + "'");
                return true;
            }
            catch (Exception e)
            {
                // A malformed or mod-added option must not stall the whole director.
                AcLog.WarningOnce("choiceAct", "Could not action a letter option: " + e.Message);
                try { Find.LetterStack.RemoveLetter(letter); } catch (Exception) { }
                return false;
            }
        }

        /// <summary>
        /// Shuts whatever tab or dialog a letter option put on screen.
        ///
        /// Deliberately blunt and deliberately guarded: there is no window the director needs
        /// open, and failing to close one must never stall the letter handling that called it.
        /// </summary>
        static void CloseAnythingTheOptionOpened()
        {
            try
            {
                if (Find.MainTabsRoot != null && Find.MainTabsRoot.OpenTab != null)
                    Find.MainTabsRoot.EscapeCurrentTab(false);
            }
            catch (Exception) { }
        }

        // DiaOption.text is not public, but classifying an option as accept or decline needs it.
        // Read once via reflection and cache; if the field ever moves, OptionText degrades to
        // an empty string and every letter is simply dismissed rather than mis-answered.
        static System.Reflection.FieldInfo textField;
        static bool textFieldResolved;

        static string OptionText(DiaOption option)
        {
            if (!textFieldResolved)
            {
                textFieldResolved = true;
                textField = typeof(DiaOption).GetField("text",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Public);
                if (textField == null)
                    AcLog.WarningOnce("diaText", "DiaOption.text not found; incident choices will be declined by default.");
            }

            if (textField == null) return "";
            var value = textField.GetValue(option) as string;
            return value != null ? value.ToLowerInvariant() : "";
        }

        static bool MatchesAny(string text, string[] keys)
        {
            for (int i = 0; i < keys.Length; i++)
                if (text.Contains(keys[i])) return true;
            return false;
        }
    }
}
