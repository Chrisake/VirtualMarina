using System.Globalization;

namespace VirtualMarina.Core.Api;

/// <summary>
/// The language of every piece of text the visualizer, the designer and the host panels display.
/// </summary>
/// <remarks>
/// Text lives in a <c>Strings.resx</c> per assembly and is looked up through <see cref="CultureInfo.CurrentUICulture"/>,
/// so an application that already sets the UI culture needs nothing here. <see cref="Culture"/> is the one-line way to
/// set it for the whole application, including threads started later. Adding a language means adding a
/// <c>Strings.&lt;culture&gt;.resx</c> next to the neutral file (see <c>Docs/15-localization.md</c>); a culture without a
/// translation falls back to the neutral English text, so nothing ever comes out blank.
/// </remarks>
/// <example>
/// <code>
/// MarinaLocalization.Culture = new CultureInfo("el-GR"); // before the first window is shown
/// </code>
/// </example>
public static class MarinaLocalization
{
    /// <summary>
    /// The culture text is looked up in. Null (the default) follows <see cref="CultureInfo.CurrentUICulture"/>.
    /// Setting it applies to the calling thread and to every thread started afterwards.
    /// </summary>
    public static CultureInfo? Culture
    {
        get => Resources.Strings.Culture;
        set
        {
            Resources.Strings.Culture = value;
            CultureInfo.DefaultThreadCurrentUICulture = value;
            if (value is not null) CultureInfo.CurrentUICulture = value;
        }
    }

    /// <summary>
    /// The cultures the assembly has text for: the neutral language plus each shipped <c>Strings.&lt;culture&gt;.resx</c>.
    /// </summary>
    /// <remarks>
    /// Built by asking the resources for each installed culture, so it also finds translations added by a host in a
    /// satellite assembly. Useful for filling a language picker.
    /// </remarks>
    public static IReadOnlyList<CultureInfo> AvailableCultures() => Resources.Strings.AvailableCultures();
}
