using System.Text.Json.Nodes;

namespace VirtualMarina.Core.Serialization;

/// <summary>
/// One step that brings a marina file from an older version of the format to a newer one, working on the raw JSON before it is
/// read into the current classes. Those classes then only ever see the current format.
/// </summary>
internal interface IMarinaMigration
{
    /// <summary>The oldest version this step applies to; files at or above <see cref="To"/> skip it.</summary>
    Version From { get; }

    /// <summary>The version the file is in once this step has run.</summary>
    Version To { get; }

    /// <summary>Rewrites the file in place.</summary>
    /// <param name="root">The whole document.</param>
    /// <param name="context">What is known about where the file came from.</param>
    void Apply(JsonObject root, MigrationContext context);
}

/// <summary>What a migration step may need to know about the file besides its contents.</summary>
/// <param name="DeclaredVersion">The version the file says it is in, or null when it says none (or nothing readable).</param>
internal sealed record MigrationContext(Version? DeclaredVersion);

/// <summary>
/// The ordered list of <see cref="IMarinaMigration"/> steps, and the check for whether a file needs any of them.
/// </summary>
/// <remarks>
/// A file is migrated from the version it declares. A file that declares none, or declares a current version yet still holds names
/// only an older version wrote (a hand-edited file, or one saved by a tool that bumped the number without renaming anything), is
/// recognised by those names instead: structure wins over the label. To change the format, add a step at the end of
/// <see cref="Steps"/> and raise <see cref="MarinaDocument.CurrentVersion"/>.
/// </remarks>
internal static class MarinaMigrations
{
    /// <summary>Every step, oldest first.</summary>
    public static IReadOnlyList<IMarinaMigration> Steps { get; } = new IMarinaMigration[] { new PiersAndBerthsMigration() };

    /// <summary>The version a 1.x file is treated as when only its structure gives it away.</summary>
    public static Version Legacy { get; } = new(1, 0);

    /// <summary>
    /// Brings <paramref name="root"/> up to <see cref="MarinaDocument.CurrentVersion"/>, running every step whose range the file
    /// falls in, in order.
    /// </summary>
    /// <param name="root">The document.</param>
    /// <param name="startVersion">The version to start from: the declared one, or <see cref="Legacy"/> when the structure says so.</param>
    /// <param name="context">What is known about the file.</param>
    public static void Run(JsonObject root, Version startVersion, MigrationContext context)
    {
        var version = startVersion;
        foreach (var step in Steps)
        {
            if (Compare(version, step.To) >= 0) continue;
            step.Apply(root, context);
            version = step.To;
        }
    }

    /// <summary>Compares on major and minor only, which is all the format's versions carry.</summary>
    public static int Compare(Version a, Version b) =>
        a.Major != b.Major ? a.Major.CompareTo(b.Major) : Math.Max(a.Minor, 0).CompareTo(Math.Max(b.Minor, 0));
}

/// <summary>
/// Format 1.x to 2.0: piers were called docks and berths slips, and the list called "berths" held the multi-berth groups, whose
/// members were "slipIds". The designer's settings and the label mode carried the old names too.
/// </summary>
internal sealed class PiersAndBerthsMigration : IMarinaMigration
{
    /// <summary>The designer settings renamed in 2.0, old name first.</summary>
    private static readonly (string Old, string New)[] DesignerRenames =
    {
        ("dockType", "pierType"),
        ("dockWidth", "pierWidth"),
        ("dockBerthingSides", "pierBerthingSides"),
        ("slipWidth", "berthWidth"),
        ("slipLength", "berthLength"),
        ("slipDepth", "berthDepth"),
        ("slipSeparators", "berthSeparators"),
        ("slipGap", "berthGap"),
        ("alignSlipsToExisting", "alignBerthsToExisting"),
        ("slipServices", "berthServices"),
        ("landSlipHeading", "landBerthHeading"),
    };

    public Version From { get; } = new(1, 0);

    public Version To { get; } = new(2, 0);

    /// <summary>
    /// True when the file holds a name only 1.x wrote, whatever version it declares. It is asked of the file as first read into
    /// the current classes, where every such name is left over among the properties they don't know; names are matched ignoring
    /// case, as the reader does.
    /// </summary>
    public static bool LooksLegacy(DocumentDto document)
    {
        if (document.Layout is { } layout)
        {
            if (Has(layout.Extra, "docks") || Has(layout.Extra, "slips")) return true;
            if ((layout.Berths ?? []).Any(entry => entry is not null && (Has(entry.Extra, "slipIds") || Has(entry.Extra, "berthIds") || Has(entry.Extra, "dockId")))) return true;
            if ((layout.Dividers ?? []).Any(entry => entry is not null && Has(entry.Extra, "dockId"))) return true;
            if ((layout.MultiBerths ?? []).Any(entry => entry is not null && Has(entry.Extra, "slipIds"))) return true;
        }

        if (Has(document.Presentation?.Extra, "slipLabels")) return true;
        return document.Designer?.Extra is { } designer && DesignerRenames.Any(rename => Has(designer, rename.Old));
    }

    public void Apply(JsonObject root, MigrationContext context)
    {
        if (Child(root, "layout") is { } layout) MigrateLayout(layout, context);
        if (Child(root, "presentation") is { } presentation) Rename(presentation, "slipLabels", "berthLabels");
        if (Child(root, "designer") is { } designer)
        {
            foreach (var (old, current) in DesignerRenames) Rename(designer, old, current);
        }
    }

    private static void MigrateLayout(JsonObject layout, MigrationContext context)
    {
        Rename(layout, "docks", "piers");

        // "berths" was the list of groups when the file is 1.x by its own account, when it has slips (so its berths are those),
        // or when its entries are plainly groups. A file that has none of these keeps its berths as they are.
        var berthsAreGroups =
            (context.DeclaredVersion is { Major: < 2 }) ||
            Has(layout, "slips") ||
            Items(layout, "berths").Any(entry => Has(entry, "slipIds") || Has(entry, "berthIds"));
        if (berthsAreGroups && Take(layout, "berths") is JsonArray groups)
        {
            var key = FindKey(layout, "multiBerths");
            if (key is null)
            {
                layout["multiBerths"] = groups;
            }
            else if (layout[key] is JsonArray existing)
            {
                // Both lists: keep the groups from each.
                foreach (var group in groups.ToArray())
                {
                    groups.Remove(group);
                    existing.Add(group);
                }
            }
        }

        Rename(layout, "slips", "berths");

        foreach (var entry in Items(layout, "berths").Concat(Items(layout, "dividers"))) Rename(entry, "dockId", "pierId");
        foreach (var group in Items(layout, "multiBerths")) Rename(group, "slipIds", "berthIds");
    }

    /// <summary>Moves a property to its new name; when both are there, the new one wins and the old one goes.</summary>
    private static void Rename(JsonObject node, string old, string current)
    {
        var value = Take(node, old);
        if (value is null || Has(node, current)) return;
        node[current] = value;
    }

    /// <summary>Removes a property (matched ignoring case) and hands back its value.</summary>
    private static JsonNode? Take(JsonObject node, string name)
    {
        var key = FindKey(node, name);
        if (key is null) return null;
        var value = node[key];
        node.Remove(key);
        return value;
    }

    private static bool Has(JsonObject node, string name) => FindKey(node, name) is not null;

    private static JsonObject? Child(JsonObject node, string name) => FindKey(node, name) is { } key ? node[key] as JsonObject : null;

    private static IEnumerable<JsonObject> Items(JsonObject node, string name) =>
        FindKey(node, name) is { } key && node[key] is JsonArray array ? array.OfType<JsonObject>() : Enumerable.Empty<JsonObject>();

    private static bool Has(Dictionary<string, System.Text.Json.JsonElement>? extra, string name) =>
        extra is not null && extra.Keys.Any(key => string.Equals(key, name, StringComparison.OrdinalIgnoreCase));

    private static string? FindKey(JsonObject node, string name)
    {
        if (node.ContainsKey(name)) return name;
        foreach (var (key, _) in node)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase)) return key;
        }

        return null;
    }
}
