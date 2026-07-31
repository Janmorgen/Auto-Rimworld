using System.Collections.Generic;
using System.IO;

// Minimal stand-ins for the handful of Verse types the learning layer touches.
//
// RimWorld's reference assemblies contain no method bodies, so linking the real ones into a
// test project would compile and then throw the moment anything ran. Instead the learning
// sources are compiled against these stubs, which lets the actual production algorithms
// execute unmodified outside the game.
//
// Only persistence and logging are stubbed, and both are inert here: the tests exercise
// behaviour, not serialisation. The XML archive round-trip is genuinely covered because
// StrategyArchive uses System.Xml.Linq directly rather than Scribe.
namespace Verse
{
    public interface IExposable
    {
        void ExposeData();
    }

    public enum LoadSaveMode { Inactive, Saving, LoadingVars, ResolvingCrossRefs, PostLoadInit }

    public enum LookMode { Undefined, Value, Deep, Reference, Def, LocalTargetInfo, TargetInfo, GlobalTargetInfo, BodyPart }

    public static class Scribe
    {
        public static LoadSaveMode mode = LoadSaveMode.Inactive;
    }

    public static class Scribe_Values
    {
        public static void Look<T>(ref T value, string label, T defaultValue = default(T), bool forceSave = false) { }
    }

    public static class Scribe_Collections
    {
        public static void Look<T>(ref List<T> list, string label, LookMode lookMode = LookMode.Undefined,
                                   params object[] ctorArgs) { }

        public static void Look<K, V>(ref Dictionary<K, V> dict, string label,
                                      LookMode keyLookMode = LookMode.Undefined,
                                      LookMode valueLookMode = LookMode.Undefined) { }
    }

    public static class Scribe_Deep
    {
        public static void Look<T>(ref T target, string label, params object[] ctorArgs) where T : IExposable { }
    }

    public static class Log
    {
        public static readonly List<string> Captured = new List<string>();

        public static void Message(string text) { Captured.Add("MSG " + text); }
        public static void Warning(string text) { Captured.Add("WARN " + text); }
        public static void Error(string text) { Captured.Add("ERR " + text); }
    }

    public static class GenFilePaths
    {
        /// <summary>Redirected to a temp directory so archive tests never touch real saves.</summary>
        public static string SaveDataFolderPath
        {
            get
            {
                var dir = Path.Combine(Path.GetTempPath(), "AutoColonyTests");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                return dir;
            }
        }
    }
}
