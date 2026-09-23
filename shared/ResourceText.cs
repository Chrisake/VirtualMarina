using System.Globalization;
using System.Reflection;
using System.Resources;

namespace VirtualMarina.Resources;

/// <summary>
/// Looks displayed text up in an assembly's <c>Strings.resx</c>, and works out which translations it carries.
/// </summary>
/// <remarks>
/// <para>
/// Every assembly that shows text keeps its own resources (see <c>Docs/15-localization.md</c>), so each has its own
/// <c>Strings</c> class. Only the machinery underneath was the same in all of them, and that is what lives here: the
/// file is linked into each project rather than referenced, so no assembly has to publish it to the others.
/// </para>
/// <para>Each copy is internal to the assembly it is compiled into, so the four do not see or clash with each other.</para>
/// </remarks>
internal sealed class ResourceText
{
    private readonly ResourceManager _manager;

    /// <summary>Opens the resources of one assembly.</summary>
    /// <param name="baseName">Root name of the resource, e.g. <c>VirtualMarina.Core.Resources.Strings</c>.</param>
    /// <param name="assembly">The assembly carrying it.</param>
    public ResourceText(string baseName, Assembly assembly) => _manager = new ResourceManager(baseName, assembly);

    /// <summary>Culture used to look the text up; null (the default) follows <see cref="CultureInfo.CurrentUICulture"/>.</summary>
    public CultureInfo? Culture { get; set; }

    /// <summary>The text of a resource by name, or the name itself when the resource is missing.</summary>
    /// <param name="name">Resource key.</param>
    public string Get(string name) => _manager.GetString(name, Culture) ?? name;

    /// <summary>Fills the placeholders of a localized format string using the current culture.</summary>
    /// <param name="format">The format string, itself usually a resource.</param>
    /// <param name="args">What to put in its placeholders.</param>
    public static string Format(string format, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, format, args);

    /// <summary>The neutral language plus every culture a Strings.&lt;culture&gt;.resx (or a host satellite assembly) supplies.</summary>
    public IReadOnlyList<CultureInfo> AvailableCultures()
    {
        var found = new List<CultureInfo> { CultureInfo.InvariantCulture };
        foreach (var culture in CultureInfo.GetCultures(CultureTypes.AllCultures))
        {
            if (culture.Equals(CultureInfo.InvariantCulture)) continue;
            // Only the culture itself, never its parents: that is what tells a real translation from a fallback.
            if (_manager.GetResourceSet(culture, createIfNotExists: true, tryParents: false) is not null) found.Add(culture);
        }

        return found;
    }
}
