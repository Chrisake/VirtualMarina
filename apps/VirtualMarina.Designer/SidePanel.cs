namespace VirtualMarina.Designer;

/// <summary>
/// The shape the three settings panels share: a heading, a column of cards under it, and the ability to collapse
/// to nothing so the column beside the marina can show one panel at a time.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="AppearancePanel"/>, <see cref="CamerasPanel"/> and <see cref="InspectorPanel"/> differ only in what
/// they put in their cards. Everything about being a panel — measuring, collapsing, scrolling — is here, so a fix
/// to any of it is a fix to all three.
/// </para>
/// <para>
/// A derived panel builds its own header and cards and hands the header over with <see cref="SetHeader"/>, then
/// adds its cards to <see cref="Stack"/>.
/// </para>
/// </remarks>
internal abstract class SidePanel : UserControl
{
    /// <summary>
    /// The cards, stacked. It sizes to its content and sits inside <see cref="Scroller"/>: a TableLayoutPanel
    /// scrolls its own content unreliably, so the scrolling is left to a plain panel around it.
    /// </summary>
    protected TableLayoutPanel Stack { get; } = new()
    {
        ColumnCount = 1,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Dock = DockStyle.Top,
        BackColor = Theme.Background,
        Padding = new Padding(12, 12, 12, 12),
        GrowStyle = TableLayoutPanelGrowStyle.AddRows,
    };

    /// <summary>Scrolls the cards when there are more of them than fit, which a tall tool easily manages.</summary>
    protected Panel Scroller { get; } = new()
    {
        AutoScroll = true,
        Dock = DockStyle.Fill,
        BackColor = Theme.Background,
    };

    private TableLayoutPanel? _header;
    private bool _collapsed;
    private int _contentHeight;
    private int _measuredWidth = -1;

    /// <summary>Records the heading the derived panel built, so its height counts towards <see cref="ContentHeight"/>.</summary>
    /// <param name="header">The heading row.</param>
    protected void SetHeader(TableLayoutPanel header) => _header = header;

    /// <summary>
    /// Shows or hides the panel by its height rather than by <see cref="Control.Visible"/>.
    /// </summary>
    /// <remarks>
    /// WinForms does not lay out a hidden control, so hiding one throws its layout away and showing it again works
    /// the whole tree out afresh — most of a second on a panel with a few hundred nested auto-sized controls.
    /// Collapsing to nothing leaves it laid out, and the swap becomes a resize.
    /// </remarks>
    public bool Collapsed
    {
        get => _collapsed;
        set
        {
            _collapsed = value;
            if (value) Height = 0;
            else ContentChanged();
        }
    }

    /// <summary>How tall the panel wants to be: its heading plus its cards. Measured once per width.</summary>
    private int ContentHeight
    {
        get
        {
            if (_contentHeight <= 0)
            {
                _contentHeight = (_header?.PreferredSize.Height ?? 0) + Stack.PreferredSize.Height;
                _measuredWidth = Width;
            }

            return _contentHeight;
        }
    }

    /// <summary>Measures again, after something changed how much there is to show or how wide it is shown in.</summary>
    protected void ContentChanged()
    {
        _contentHeight = 0;
        if (!_collapsed) Height = ContentHeight;
    }

    /// <summary>A panel shown in a different width wraps differently, so its height has to be worked out again.</summary>
    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (_collapsed || Width == _measuredWidth) return;
        ContentChanged();
    }

    /// <summary>
    /// Hands the scrolling to whatever contains the panel. Used when several panels share one scrolling column: each
    /// then sizes to its content instead of to the space it is given, and scrolls away with everything else.
    /// </summary>
    public void UseOuterScrolling()
    {
        // The header is added after the scroller, so it still docks above it once both are Top.
        Scroller.AutoScroll = false;
        Scroller.Dock = DockStyle.Top;
        Scroller.AutoSize = true;
        Scroller.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Dock = DockStyle.Top;

        // The height is ours to set, so that collapsing to nothing can stand in for hiding.
        AutoSize = false;
        Height = ContentHeight;
    }
}
