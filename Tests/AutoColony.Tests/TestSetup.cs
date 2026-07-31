using System.Runtime.CompilerServices;
using AutoColony.Learning;
using Xunit;

// The gene registry and the strategy archive are process-wide static state. Running tests
// in parallel against them would be flaky for reasons that have nothing to do with the code
// under test, so this assembly runs serially.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace AutoColony.Tests
{
    /// <summary>
    /// Registers the per-work-type genes exactly as the game's startup hook does.
    ///
    /// Without this the strategy space in tests would be missing roughly a third of its real
    /// dimensions, so the search tests would be measuring an easier problem than production
    /// actually faces. A module initializer guarantees it happens once, before any test.
    /// </summary>
    internal static class TestBootstrap
    {
        static readonly string[] VanillaWorkTypes =
        {
            "Firefighter", "Patient", "Doctor", "PatientBedRest", "Childcare", "BasicWorker",
            "Warden", "Handling", "Cooking", "Hunting", "Construction", "Growing", "Mining",
            "PlantCutting", "Smithing", "Tailoring", "Art", "Crafting", "Hauling", "Cleaning",
            "Research"
        };

        [ModuleInitializer]
        internal static void Init()
        {
            foreach (var name in VanillaWorkTypes)
                Genes.RegisterWorkType(name, "Work: " + name);
        }
    }
}
