using System.Collections.ObjectModel;
using VirtualMarina.Core.Rendering;

namespace VirtualMarina.Core.Api;

/// <summary>
/// Tooltip content shown above the selection. The visualizer pre-fills it from the berth data; handlers of
/// <see cref="IMarinaVisualizer.BerthSelected"/> and <see cref="IMarinaVisualizer.MultiBerthSelected"/> may change,
/// extend or clear it.
/// </summary>
public sealed class BerthTooltip
{
    /// <summary>Bold heading. Default: the berth's display name, or "N berths selected".</summary>
    public string? Title { get; set; }

    /// <summary>Muted line under the title. Default: the pier name(s).</summary>
    public string? Subtitle { get; set; }

    /// <summary>Color of the tooltip's accent bar. Defaults to the berth's status color.</summary>
    public ColorRgba? AccentColor { get; set; }

    /// <summary>Label/value rows.</summary>
    public IList<BerthTooltipLine> Lines { get; } = new List<BerthTooltipLine>();

    /// <summary>Optional free text under the rows.</summary>
    public string? Footer { get; set; }

    /// <summary>Set false to show no tooltip for this selection.</summary>
    public bool IsVisible { get; set; } = true;

    /// <summary>Appends a label/value row.</summary>
    /// <param name="label">Left column; empty for a full-width value.</param>
    /// <param name="value">Right column.</param>
    /// <param name="emphasize">Draw the value in bold.</param>
    /// <returns>This tooltip, for chaining.</returns>
    public BerthTooltip AddLine(string label, string? value, bool emphasize = false)
    {
        Lines.Add(new BerthTooltipLine(label, value) { IsEmphasized = emphasize });
        return this;
    }

    /// <summary>Replaces the value of the first line with this label, or adds the line.</summary>
    public BerthTooltip SetLine(string label, string? value)
    {
        for (var i = 0; i < Lines.Count; i++)
        {
            if (string.Equals(Lines[i].Label, label, StringComparison.OrdinalIgnoreCase))
            {
                Lines[i] = Lines[i] with { Value = value };
                return this;
            }
        }

        return AddLine(label, value);
    }

    /// <summary>Removes the first row with this label (case-insensitive), e.g. <c>RemoveLine("Owner")</c>. Returns false when none matched.</summary>
    public bool RemoveLine(string label)
    {
        for (var i = 0; i < Lines.Count; i++)
        {
            if (string.Equals(Lines[i].Label, label, StringComparison.OrdinalIgnoreCase))
            {
                Lines.RemoveAt(i);
                return true;
            }
        }

        return false;
    }

    /// <summary>Removes the title, subtitle, rows, footer and accent (the default content), keeping <see cref="IsVisible"/>.</summary>
    public void Clear()
    {
        Title = null;
        Subtitle = null;
        Footer = null;
        AccentColor = null;
        Lines.Clear();
    }

    internal BerthTooltip Clone()
    {
        var copy = new BerthTooltip { Title = Title, Subtitle = Subtitle, AccentColor = AccentColor, Footer = Footer, IsVisible = IsVisible };
        foreach (var line in Lines) copy.Lines.Add(line);
        return copy;
    }
}

/// <param name="Label">Left column. Empty for a full-width value.</param>
/// <param name="Value">Right column.</param>
public sealed record BerthTooltipLine(string Label, string? Value)
{
    /// <summary>Draw the value in bold.</summary>
    public bool IsEmphasized { get; init; }
}

/// <summary>Visual emphasis of a <see cref="BerthAction"/>.</summary>
public enum BerthActionStyle
{
    /// <summary>Regular entry.</summary>
    Normal,

    /// <summary>The main action (drawn highlighted).</summary>
    Primary,

    /// <summary>A destructive action (drawn in red).</summary>
    Danger,
}

/// <summary>An entry in the actions window that opens on right-click.</summary>
public sealed class BerthAction
{
    /// <summary>Creates an action. Usually added through <see cref="BerthActionCollection.Add(string, string, bool, string?)"/>.</summary>
    /// <param name="actionId">Identifier passed back when the action is invoked.</param>
    /// <param name="caption">Text shown in the actions window.</param>
    /// <param name="enabled">False shows the action grayed out.</param>
    public BerthAction(string actionId, string caption, bool enabled = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);
        ActionId = actionId;
        Caption = caption ?? actionId;
        Enabled = enabled;
    }

    /// <summary>Identifier passed back in <see cref="BerthActionInvokedEventArgs.ActionId"/>.</summary>
    public string ActionId { get; }

    /// <summary>Text shown in the actions window.</summary>
    public string Caption { get; set; }

    /// <summary>Disabled actions are shown grayed out and cannot be invoked.</summary>
    public bool Enabled { get; set; }

    /// <summary>Hidden actions are not shown.</summary>
    public bool Visible { get; set; } = true;

    /// <summary>Hint shown when hovering the action (e.g. why it is disabled).</summary>
    public string? Description { get; set; }

    /// <summary>Short glyph drawn before the caption, e.g. an emoji or a single symbol character.</summary>
    public string? Icon { get; set; }

    /// <summary>Optional keyboard hint drawn right-aligned (informational only).</summary>
    public string? ShortcutText { get; set; }

    /// <summary>Normal, Primary (highlighted) or Danger (red).</summary>
    public BerthActionStyle Style { get; set; } = BerthActionStyle.Normal;

    /// <summary>Draw a separator above this action.</summary>
    public bool BeginGroup { get; set; }

    /// <summary>Keep the actions window open after this action is invoked.</summary>
    public bool KeepOpen { get; set; }

    /// <summary>Host-owned value passed back with the invocation.</summary>
    public object? Tag { get; set; }
}

/// <summary>Ordered, editable list of actions with lookup by <see cref="BerthAction.ActionId"/>.</summary>
public sealed class BerthActionCollection : Collection<BerthAction>
{
    /// <summary>Creates, appends and returns an action; set further properties on the result.</summary>
    /// <param name="actionId">Identifier passed back when the action is invoked.</param>
    /// <param name="caption">Text shown in the actions window.</param>
    /// <param name="enabled">False shows the action grayed out.</param>
    /// <param name="icon">Optional short glyph, e.g. "⚓".</param>
    /// <example><code>e.Actions.Add("release", "Release berth", icon: "⇥").Style = BerthActionStyle.Danger;</code></example>
    public BerthAction Add(string actionId, string caption, bool enabled = true, string? icon = null)
    {
        var action = new BerthAction(actionId, caption, enabled) { Icon = icon };
        Add(action);
        return action;
    }

    /// <summary>The action with this id (case-insensitive), or null.</summary>
    public BerthAction? Find(string actionId) =>
        this.FirstOrDefault(a => string.Equals(a.ActionId, actionId, StringComparison.OrdinalIgnoreCase));

    /// <summary>Removes the action with this id. Returns false when none matched.</summary>
    public bool Remove(string actionId) => Find(actionId) is { } action && Remove(action);

    /// <summary>Rejects null actions.</summary>
    protected override void InsertItem(int index, BerthAction item)
    {
        ArgumentNullException.ThrowIfNull(item);
        base.InsertItem(index, item);
    }

    /// <summary>Rejects null actions.</summary>
    protected override void SetItem(int index, BerthAction item)
    {
        ArgumentNullException.ThrowIfNull(item);
        base.SetItem(index, item);
    }
}
