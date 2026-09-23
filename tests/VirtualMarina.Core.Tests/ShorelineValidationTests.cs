using System.Numerics;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Mathematics;
using VirtualMarina.Core.Serialization;

namespace VirtualMarina.Core.Tests;

/// <summary>
/// A shoreline must split the plan into two clean sides. These are the ways a drawn line can fail to, each of which used to
/// build a shape that crossed itself (and so put land in the wrong places), and the shape's cache.
/// </summary>
public class ShorelineValidationTests
{
    [Fact]
    public void ALineThatCrossesItself_IsRefused()
    {
        // A figure of eight: the third segment runs back across the first.
        var knot = new Shoreline(new[] { new Vector2(-100, 0), new Vector2(100, 0), new Vector2(100, 50), new Vector2(0, -50), new Vector2(-200, -60) }, true);
        Assert.Contains(knot.Validate(), p => p.Contains("crosses itself", StringComparison.Ordinal));
        Assert.Empty(knot.BuildOutline());
        Assert.Null(knot.EndsAtTheMapEdge());
    }

    [Fact]
    public void ALineThatTurnsStraightBack_IsRefused()
    {
        var folded = new Shoreline(new[] { new Vector2(-100, 0), new Vector2(100, 0), new Vector2(50, 0) }, true);
        Assert.Contains(folded.Validate(), p => p.Contains("crosses itself", StringComparison.Ordinal));
    }

    [Fact]
    public void AnEndlessSegmentThatRunsBackAcrossTheLine_IsRefused()
    {
        // A hook: the last segment points back west, so carried on without end it cuts through the first segment.
        var hook = new Shoreline(new[] { new Vector2(-100, 0), new Vector2(100, 0), new Vector2(100, 40), new Vector2(50, 20) }, true);
        Assert.Contains(hook.Validate(), p => p.Contains("after its last point runs back across", StringComparison.Ordinal));

        var reversed = new Shoreline(hook.Points.Reverse(), true);
        Assert.Contains(reversed.Validate(), p => p.Contains("before its first point runs back across", StringComparison.Ordinal));
    }

    [Fact]
    public void ABentCoast_ThatDividesThePlan_IsSound_AndItsShapeIsSimple()
    {
        var bay = new Shoreline(new[] { new Vector2(-300, 0), new Vector2(-100, 0), new Vector2(-50, 80), new Vector2(50, 80), new Vector2(100, 0), new Vector2(300, 0) }, false);
        Assert.Empty(bay.Validate());
        Assert.True(PolygonMath.IsSimple(bay.BuildOutline()));
    }

    [Fact]
    public void TheShape_IsWorkedOutOnce_AndAgainOnlyWhenTheLineOrSideChanges()
    {
        var coast = new Shoreline(new[] { new Vector2(-100, 0), new Vector2(100, 0) }, true);
        var outline = coast.BuildOutline();
        Assert.Same(outline, coast.BuildOutline());

        // Other settings share it; the line or the side get their own.
        Assert.Same(outline, (coast with { Height = 3f }).BuildOutline());
        Assert.NotSame(outline, (coast with { LandOnLeft = false }).BuildOutline());
        var moved = coast with { Points = new[] { new Vector2(-100, 10), new Vector2(100, 10) } };
        Assert.True(moved.Contains(new Vector2(0, 11)));
        Assert.False(moved.Contains(new Vector2(0, 9)));

        // The cache takes no part in equality.
        var fresh = new Shoreline(new[] { new Vector2(-100, 0), new Vector2(100, 0) }, true);
        Assert.Equal(coast, fresh);
        Assert.Equal(coast.GetHashCode(), fresh.GetHashCode());

        // A thousand tests cost what one does.
        var started = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 20_000; i++) coast.Contains(new Vector2(i % 200 - 100, i % 50 - 25));
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void AFileWithACrossedShoreline_Opens_ButDoesNotLoad_LikeACrossedLandArea()
    {
        var document = new MarinaDocument
        {
            Layout = MarinaLayout.Empty with
            {
                Shoreline = new Shoreline(new[] { new Vector2(-100, 0), new Vector2(100, 0), new Vector2(100, 50), new Vector2(0, -50), new Vector2(-200, -60) }, true),
            },
        };

        var reread = MarinaDocument.Parse(document.ToJson());
        Assert.NotNull(reread.Layout.Shoreline);
        Assert.Equal(5, reread.Layout.Shoreline.Points.Count);
        Assert.Contains(reread.Validate(), p => p.Contains("crosses itself", StringComparison.Ordinal));
        Assert.Throws<MarinaLayoutException>(() => reread.ApplyTo(new MarinaVisualizer()));
    }
}
