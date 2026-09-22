using VirtualMarina.Core.Domain;

namespace VirtualMarina.Core.Design;

/// <summary>
/// What renaming a pier's berths to a pattern would do, worked out before anything is changed so a clash can be
/// reported rather than half-applied.
/// </summary>
/// <seealso cref="MarinaDesigner.PlanBerthNames"/>
public sealed class BerthNamePlan
{
    /// <summary>Creates a plan.</summary>
    /// <param name="pierId">The pier whose berths it covers.</param>
    /// <param name="pattern">The pattern the names were built from.</param>
    /// <param name="renames">Every berth on the pier and the name it would get, in order along the pier.</param>
    /// <param name="clashes">Names that more than one berth would end up with, or that something else already has.</param>
    public BerthNamePlan(string pierId, string pattern, IEnumerable<(string From, string To)> renames, IEnumerable<string> clashes)
    {
        ArgumentNullException.ThrowIfNull(pierId);
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(renames);
        ArgumentNullException.ThrowIfNull(clashes);

        PierId = pierId;
        Pattern = pattern;
        Renames = renames.ToArray();
        Clashes = clashes.ToArray();
    }

    /// <summary>The pier whose berths this covers.</summary>
    public string PierId { get; }

    /// <summary>The pattern the names were built from.</summary>
    public string Pattern { get; }

    /// <summary>
    /// Every berth on the pier and the name it would get, in order along the pier. A berth already called what the
    /// pattern would call it is listed too, with the same name on both sides.
    /// </summary>
    public IReadOnlyList<(string From, string To)> Renames { get; }

    /// <summary>
    /// The names that stop this being applied: ones two berths on the pier would share, and ones a berth elsewhere
    /// in the marina already has. Empty when the plan is sound.
    /// </summary>
    public IReadOnlyList<string> Clashes { get; }

    /// <summary>True when nothing clashes and the plan can be applied.</summary>
    public bool IsClear => Clashes.Count == 0;
}
