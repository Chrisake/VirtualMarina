using System.Collections.ObjectModel;
using VirtualMarina.Core.Rendering;

namespace VirtualMarina.Core.Api;

/// <summary>
/// Tooltip content shown above the selection. The visualizer pre-fills it from the slip data; handlers of
/// <see cref="IMarinaVisualizer.SlipSelected"/> and <see cref="IMarinaVisualizer.MultiSlipSelected"/> may change,
/// extend or clear it.
/// </summary>
public sealed class SlipTooltip
{
    /// <summary>Bold heading. Default: the slip's display name, or "N slips selected".</summary>
    public string? Title { get; set; }

    /// <summary>Muted line under the title. Default: the dock name(s).</summary>
    public string? Subtitle { get; set; }

    /// <summary>Color of the tooltip's accent bar. Defaults to the slip's status color.</summary>
    public ColorRgba? AccentColor { get; set; }

    /// <summary>Label/value rows.</summary>
    public IList<SlipTooltipLine> Lines { get; } = new List<SlipTooltipLine>();

    /// <summary>Optional free text under the rows.</summary>
    public string? Footer { get; set; }

    /// <summary>Set false to show no tooltip for this selection.</summary>
    public bool IsVisible { get; set; } = true;

    /// <summary>Appends a label/value row.</summary>
    /// <param name="label">Left column; empty for a full-width value.</param>
    /// <param name="value">Right column.</param>
    /// <param name="emphasize">Draw the value in bold.</param>
    /// <returns>This tooltip, for chaining.</returns>
    public SlipTooltip AddLine(string label, string? value, bool emphasize = false)
    {
        Lines.Add(new SlipTooltipLine(label, value) { IsEmphasized = emphasize });
        return this;
    }

    /// <summary>Replaces the value of the first line with this label, or adds the line.</summary>
    public SlipTooltip SetLine(string label, string? value)
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

    internal SlipTooltip Clone()
    {
        var copy = new SlipTooltip { Title = Title, Subtitle = Subtitle, AccentColor = AccentColor, Footer = Footer, IsVisible = IsVisible };
        foreach (var line in Lines) copy.Lines.Add(line);
        return copy;
    }
}

/// <param name="Label">Left column. Empty for a full-width value.</param>
/// <param name="Value">Right column.</param>
public sealed record SlipTooltipLine(string Label, string? Value)
{
    /// <summary>Draw the value in bold.</summary>
    public bool IsEmphasized { get; init; }
}

/// <summary>Visual emphasis of a <see cref="SlipAction"/>.</summary>
public enum SlipActionStyle
{
    /// <summary>Regular entry.</summary>
    Normal,

    /// <summary>The main action (drawn highlighted).</summary>
    Primary,

    /// <summary>A destructive action (drawn in red).</summary>
    Danger,
}

/// <summary>An entry in the actions window that opens on right-click.</summary>
public sealed class SlipAction
{
    /// <summary>Creates an action. Usually added through <see cref="SlipActionCollection.Add(string, string, bool, string?)"/>.</summary>
    /// <param name="actionId">Identifier passed back when the action is invoked.</param>
    /// <param name="caption">Text shown in the actions window.</param>
    /// <param name="enabled">False shows the action grayed out.</param>
    public SlipAction(string actionId, string caption, bool enabled = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);
        ActionId = actionId;
        Caption = caption ?? actionId;
        Enabled = enabled;
    }

    /// <summary>Identifier passed back in <see cref="SlipActionInvokedEventArgs.ActionId"/>.</summary>
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
    public SlipActionStyle Style { get; set; } = SlipActionStyle.Normal;

    /// <summary>Draw a separator above this action.</summary>
    public bool BeginGroup { get; set; }

    /// <summary>Keep the actions window open after this action is invoked.</summary>
    public bool KeepOpen { get; set; }

    /// <summary>Host-owned value passed back with the invocation.</summary>
    public object? Tag { get; set; }
}

/// <summary>Ordered, editable list of actions with lookup by <see cref="SlipAction.ActionId"/>.</summary>
public sealed class SlipActionCollection : Collection<SlipAction>
{
    /// <summary>Creates, appends and returns an action; set further properties on the result.</summary>
    /// <param name="actionId">Identifier passed back when the action is invoked.</param>
    /// <param name="caption">Text shown in the actions window.</param>
    /// <param name="enabled">False shows the action grayed out.</param>
    /// <param name="icon">Optional short glyph, e.g. "⚓".</param>
    /// <example><code>e.Actions.Add("release", "Release slip", icon: "⇥").Style = SlipActionStyle.Danger;</code></example>
    public SlipAction Add(string actionId, string caption, bool enabled = true, string? icon = null)
    {
        var action = new SlipAction(actionId, caption, enabled) { Icon = icon };
        Add(action);
        return action;
    }

    /// <summary>The action with this id (case-insensitive), or null.</summary>
    public SlipAction? Find(string actionId) =>
        this.FirstOrDefault(a => string.Equals(a.ActionId, actionId, StringComparison.OrdinalIgnoreCase));

    /// <summary>Removes the action with this id. Returns false when none matched.</summary>
    public bool Remove(string actionId) => Find(actionId) is { } action && Remove(action);

    /// <summary>Rejects null actions.</summary>
    protected override void InsertItem(int index, SlipAction item)
    {
        ArgumentNullException.ThrowIfNull(item);
        base.InsertItem(index, item);
    }

    /// <summary>Rejects null actions.</summary>
    protected override void SetItem(int index, SlipAction item)
    {
        ArgumentNullException.ThrowIfNull(item);
        base.SetItem(index, item);
    }
}
