using VirtualMarina.Core.Design;
using VirtualMarina.Core.Domain;
using VirtualMarina.Designer.Resources;

namespace VirtualMarina.Designer;

/// <summary>One line of the activity log.</summary>
/// <param name="Time">When it happened, in the clock's local time.</param>
/// <param name="Message">What happened.</param>
public sealed record ActivityLogEntry(DateTimeOffset Time, string Message)
{
    /// <summary>The line as the log shows it: the time, then the message.</summary>
    public string Text => Strings.Format(Strings.LogEntry, Time.DateTime, Message);

    /// <inheritdoc />
    public override string ToString() => Text;
}

/// <summary>
/// The Designer's activity log: what was drawn, removed, undone, opened and saved, newest first, and never more than
/// <see cref="Capacity"/> lines.
/// </summary>
public sealed class ActivityLog
{
    /// <summary>How many lines are kept; older ones fall off the end.</summary>
    public const int Capacity = 500;

    private readonly LinkedList<ActivityLogEntry> _entries = new();
    private readonly TimeProvider _time;

    /// <summary>Creates an empty log.</summary>
    /// <param name="time">The clock the lines are stamped by; the system clock when null.</param>
    public ActivityLog(TimeProvider? time = null) => _time = time ?? TimeProvider.System;

    /// <summary>A line was added (it is <see cref="Latest"/>). The oldest may have been dropped to make room.</summary>
    public event EventHandler? Added;

    /// <summary>The lines, newest first.</summary>
    public IReadOnlyCollection<ActivityLogEntry> Entries => _entries;

    /// <summary>How many lines there are.</summary>
    public int Count => _entries.Count;

    /// <summary>The newest line, or null while the log is empty.</summary>
    public ActivityLogEntry? Latest => _entries.First?.Value;

    /// <summary>Adds a line, stamped with the current time. Empty messages are ignored.</summary>
    /// <param name="message">What happened.</param>
    public void Add(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        var entry = new ActivityLogEntry(_time.GetLocalNow(), message);
        _entries.AddFirst(entry);
        while (_entries.Count > Capacity) _entries.RemoveLast();
        Added?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>What the activity log says about each thing the designer reports.</summary>
public static class DesignerLogText
{
    /// <summary>A shape was drawn: a coast, a land area, a pier, or berths. Null when nothing was actually added.</summary>
    /// <param name="e">The designer's event.</param>
    public static string? Created(DesignElementCreatedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        return e switch
        {
            { Shoreline: { } shore } => Strings.Format(Strings.LogCoastDrawn, shore.Points.Count),
            { LandArea: { } land } => Strings.Format(Strings.LogAddedLand, land.DisplayName, land.Points.Count, land.Area),
            { Pier: { } pier } => Strings.Format(Strings.LogAddedPier, pier.Id, pier.Length, pier.Width),
            { Berths.Count: 0 } => null,
            { Berths.Count: 1 } => Strings.Format(Strings.LogAddedBerth, e.Berths[0].Id),
            _ => Strings.Format(Strings.LogAddedBerths, e.Berths.Count, string.Join(", ", e.Berths.Select(berth => berth.Id))),
        };
    }

    /// <summary>Something was erased, with the berths and separators that went with it.</summary>
    /// <param name="e">The designer's event.</param>
    public static string Erased(DesignElementErasedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        return Strings.Format(Strings.LogRemoved, Describe(e.Element), e.RemovedBerths.Count, e.RemovedDividers.Count);
    }

    /// <summary>Trees were planted on, or cleared from, a lawn.</summary>
    /// <param name="e">The designer's event.</param>
    public static string TreesPlanted(DesignTreesPlantedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        return Strings.Format(Strings.LogTrees, e.LandArea.DisplayName, e.LandArea.Trees.Count, e.PreviousCount);
    }

    /// <summary>A step was undone.</summary>
    /// <param name="e">The designer's event.</param>
    public static string Undone(DesignActionUndoneEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        return Strings.Format(Strings.LogUndone, e.Description);
    }

    /// <summary>A step was redone.</summary>
    /// <param name="e">The designer's event.</param>
    public static string Redone(DesignActionRedoneEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        return Strings.Format(Strings.LogRedone, e.Description);
    }

    /// <summary>A change the designer was asked for did not happen, and why.</summary>
    /// <param name="e">The designer's event.</param>
    public static string Failed(DesignActionFailedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        return Strings.Format(Strings.ActionFailed, e.Description, e.Exception.Message);
    }

    /// <summary>A scale line was drawn over the reference image.</summary>
    /// <param name="e">The designer's event.</param>
    public static string ScaleLine(ScaleLineDrawnEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        return Strings.Format(Strings.LogScaleLine, e.MeasuredLength);
    }

    /// <summary>The reference image was loaded, moved, scaled, restyled or removed.</summary>
    /// <param name="e">The designer's event.</param>
    public static string ReferenceImage(ReferenceImageChangedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        return Strings.Format(Strings.LogReferenceImage, e.Change.ToString().ToLowerInvariant(), e.MetersPerPixel);
    }

    /// <summary>How the log names an erased element: "berth A-01", "pier A", "land area Quay 1".</summary>
    /// <param name="element">A berth, pier, land area or shoreline.</param>
    public static string Describe(object element) => element switch
    {
        Berth berth => Strings.Format(Strings.ElementBerth, berth.Id),
        Pier pier => Strings.Format(Strings.ElementPier, pier.Id),
        LandArea land => Strings.Format(Strings.ElementLandArea, land.DisplayName),
        Shoreline => Strings.ElementCoast,
        null => string.Empty,
        _ => element.GetType().Name.ToLowerInvariant(),
    };
}
