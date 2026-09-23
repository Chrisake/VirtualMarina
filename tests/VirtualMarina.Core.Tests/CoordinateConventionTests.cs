using System.Numerics;
using VirtualMarina.Core.Camera;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Mathematics;
using VirtualMarina.Core.Rendering;

namespace VirtualMarina.Core.Tests;

/// <summary>
/// Pins every direction the library names, so the conventions written up in Docs/20-coordinate-conventions.md cannot drift:
/// plan X is east, plan Y is world Z and points south, headings turn from +Z toward +X, and the two meanings of "right".
/// </summary>
public class CoordinateConventionTests
{
    private static readonly Vector2 East = new(1, 0);
    private static readonly Vector2 West = new(-1, 0);
    private static readonly Vector2 South = new(0, 1);
    private static readonly Vector2 North = new(0, -1);

    [Theory]
    [InlineData(0f, 0f, 1f)]      // +Z, south
    [InlineData(90f, 1f, 0f)]     // +X, east
    [InlineData(180f, 0f, -1f)]   // −Z, north
    [InlineData(270f, -1f, 0f)]   // −X, west
    [InlineData(-90f, -1f, 0f)]
    public void Headings_TurnFromPlusZTowardPlusX(float heading, float x, float y)
    {
        AssertClose(new Vector2(x, y), MarinaMath.HeadingToDirection(heading));
        Assert.Equal(0f, MarinaMath.DeltaAngle(heading, MarinaMath.DirectionToHeading(MarinaMath.HeadingToDirection(heading))), 3);
    }

    [Fact]
    public void HeadingToRight_IsTheLocalPlusXAxis()
    {
        AssertClose(East, MarinaMath.HeadingToRight(0f));
        AssertClose(North, MarinaMath.HeadingToRight(90f));
        AssertClose(West, MarinaMath.HeadingToRight(180f));
    }

    [Fact]
    public void PierRight_IsTheWalkersRight_AndThe_OppositeOfBerthRight()
    {
        var pier = new Pier("A", "A", Vector2.Zero, headingDegrees: 0f, length: 40f);

        // Walking south (+Z) the right hand is west; seen from above with north up that is the left of the screen.
        AssertClose(West, pier.Right);
        AssertClose(East, pier.LocalX);
        AssertClose(-pier.Right, pier.LocalX);
        AssertClose(East, pier.SideNormal(PierSide.Left));
        AssertClose(West, pier.SideNormal(PierSide.Right));

        var north = pier with { HeadingDegrees = 180f };
        AssertClose(East, north.Right);
        AssertClose(West, north.SideNormal(PierSide.Left));

        var berth = new Berth("B", "A", Vector2.Zero, 0f, 10f, 4f);
        AssertClose(East, berth.Right);
        AssertClose(berth.Right, berth.LocalX);

        var divider = new Divider("D", Vector2.Zero, 0f, 5f);
        AssertClose(East, divider.Right);
        AssertClose(divider.Right, divider.LocalX);

        var rect = new OrientedRect(Vector2.Zero, new Vector2(2, 4), 0f);
        AssertClose(East, rect.Right);
        AssertClose(rect.Right, rect.LocalX);
        AssertClose(South, rect.Forward);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(37f)]
    [InlineData(180f)]
    [InlineData(-120f)]
    public void GeneratedBerths_LieOnTheSideNormal_WithTheirBowsTowardThePier(float heading)
    {
        var pier = new Pier("A", "A", new Vector2(10, -5), heading, 60f, width: 3f);
        foreach (var side in new[] { PierSide.Left, PierSide.Right })
        {
            var berth = BerthGenerator.AtPier(pier, "B", side, offsetAlong: 10f, berthWidth: 5f, berthLength: 12f);
            var outward = Vector2.Dot(berth.Center - pier.Start, pier.SideNormal(side));
            Assert.Equal(pier.Width * 0.5f + 6f, outward, 3);
            AssertClose(-pier.SideNormal(side), berth.Forward);

            var divider = BerthGenerator.DividerAtPier(pier, "D", side, 10f, 12f, DividerType.Piles);
            AssertClose(pier.SideNormal(side), divider.Direction);
        }
    }

    [Fact]
    public void LandOnLeft_IsPlanLeft_WhichIsTheWalkersRightSeenFromAbove()
    {
        var eastward = new Shoreline(new[] { new Vector2(-100, 0), new Vector2(100, 0) }, landOnLeft: true);

        // Walking east, plan-left (+X turned toward +Z) is +Z: south.
        Assert.True(eastward.Contains(new Vector2(0, 50)));
        Assert.False(eastward.Contains(new Vector2(0, -50)));
        AssertClose(South, eastward.LandSideNormal(0));

        var other = eastward with { LandOnLeft = false };
        AssertClose(North, other.LandSideNormal(0));
        Assert.True(other.Contains(new Vector2(0, -50)));

        // The same side a pier walked the same way calls Right.
        var pier = new Pier("P", "P", new Vector2(-100, 0), headingDegrees: 90f, length: 200f);
        AssertClose(pier.Right, eastward.LandSideNormal(0));
    }

    [Fact]
    public void SunAzimuth_UsesTheHeadingConvention_NotACompassBearing()
    {
        var lighting = new LightingSettings();
        lighting.SetSunAngles(0f, 30f);
        Assert.True(lighting.SunDirection.Z > 0.5f, "azimuth 0° is toward +Z (south)");
        lighting.SetSunAngles(90f, 30f);
        Assert.True(lighting.SunDirection.X > 0.5f, "azimuth 90° is toward +X (east)");
        Assert.True(lighting.SunDirection.Y > 0.4f, "the sun is above the horizon");
    }

    [Fact]
    public void CameraYaw_IsWhereTheCameraStands()
    {
        // Yaw 0 stands on the +Z (south) side looking north; 90 stands east.
        var south = OrbitCamera.ComputeEye(new CameraPose(Vector3.Zero, 0f, 30f, 100f));
        Assert.True(south.Z > 50f);
        var east = OrbitCamera.ComputeEye(new CameraPose(Vector3.Zero, 90f, 30f, 100f));
        Assert.True(east.X > 50f);
    }

    [Fact]
    public void OrientedRectCorners_RunCounterClockwiseInPlanCoordinates()
    {
        var corners = new OrientedRect(Vector2.Zero, new Vector2(2, 4), 0f).GetCorners();
        Assert.True(PolygonMath.SignedArea(corners) > 0f);
        AssertClose(new Vector2(-1, -2), corners[0]);
    }

    private static void AssertClose(Vector2 expected, Vector2 actual)
    {
        Assert.True(Vector2.Distance(expected, actual) < 1e-4f, $"expected {expected}, got {actual}");
    }
}
