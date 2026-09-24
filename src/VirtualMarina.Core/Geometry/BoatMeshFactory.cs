using System.Numerics;
using VirtualMarina.Core.Domain;

namespace VirtualMarina.Core.Geometry;

/// <summary>
/// Low-poly procedural stand-ins for each <see cref="BoatType"/>. They are placeholders until real GLTF
/// models are wired in; replace a mesh by registering a different <see cref="MeshData"/> under the same id.
/// </summary>
/// <remarks>
/// Model space: bow points +Z, the waterline is Y = 0, the hull is centered on X/Z,
/// and dimensions match <see cref="BoatTypeCatalog.GetNominalDimensions"/>.
/// </remarks>
public static class BoatMeshFactory
{
    private static readonly Vector3 White = new(0.95f, 0.95f, 0.93f);
    private static readonly Vector3 OffWhite = new(0.86f, 0.86f, 0.84f);
    private static readonly Vector3 LightGrey = new(0.72f, 0.74f, 0.76f);
    private static readonly Vector3 Teak = new(0.70f, 0.52f, 0.32f);
    private static readonly Vector3 Glass = new(0.10f, 0.16f, 0.22f);
    private static readonly Vector3 Metal = new(0.74f, 0.75f, 0.78f);
    private static readonly Vector3 Dark = new(0.11f, 0.11f, 0.12f);
    private static readonly Vector3 Navy = new(0.12f, 0.20f, 0.40f);
    private static readonly Vector3 SailCloth = new(0.98f, 0.97f, 0.93f);
    private static readonly Vector3 SailShade = new(0.90f, 0.90f, 0.86f);
    private static readonly Vector3 Beige = new(0.84f, 0.78f, 0.64f);
    private static readonly Vector3 Red = new(0.72f, 0.14f, 0.12f);
    private static readonly Vector3 Yellow = new(0.98f, 0.76f, 0.10f);
    private static readonly Vector3 SeaBlue = new(0.17f, 0.35f, 0.56f);
    private static readonly Vector3 Charcoal = new(0.33f, 0.35f, 0.38f);

    /// <summary>Builds the procedural model of a boat type.</summary>
    /// <param name="type">Boat type.</param>
    /// <param name="meshId">Id for the mesh, normally <see cref="MeshIds.ForBoat"/>.</param>
    public static MeshData Create(BoatType type, int meshId)
    {
        var b = new MeshBuilder();
        switch (type)
        {
            case BoatType.MonohullSailboat: BuildMonohullSailboat(b); break;
            case BoatType.CatamaranSailboat: BuildCatamaranSailboat(b); break;
            case BoatType.DayMotorBoat: BuildDayMotorBoat(b); break;
            case BoatType.CatamaranMotorboat: BuildCatamaranMotorboat(b); break;
            case BoatType.MotorYacht: BuildMotorYacht(b); break;
            case BoatType.FishingBoat: BuildFishingBoat(b); break;
            case BoatType.JetSki: BuildJetSki(b); break;
            case BoatType.Ferry: BuildFerry(b); break;
            default: throw new ArgumentOutOfRangeException(nameof(type), type, null);
        }

        return b.Build(meshId, "Boat." + type);
    }

    private static void BuildMonohullSailboat(MeshBuilder b)
    {
        AddHull(b, 0f, length: 12f, beam: 4f, freeboard: 1.1f, draft: 0.5f, new HullPaint(White, Teak, Navy));
        b.AddBox(new(0f, 1.45f, -0.6f), new(2.4f, 0.7f, 4.6f), OffWhite);      // coachroof
        b.AddBox(new(0f, 1.55f, -0.6f), new(2.46f, 0.22f, 4.66f), Glass);      // portlights band
        b.AddCylinder(new(0f, 1.1f, 1.3f), new(0f, 15.5f, 1.3f), 0.10f, 0.07f, 6, Metal);  // mast
        b.AddCylinder(new(0f, 2.6f, 1.2f), new(0f, 2.6f, -3.9f), 0.07f, 0.07f, 5, Metal);  // boom
        b.AddTriangularPlate(new(0f, 2.75f, 1.15f), new(0f, 15.1f, 1.15f), new(0f, 2.75f, -3.8f), 0.04f, SailCloth); // main
        b.AddTriangularPlate(new(0f, 13.8f, 1.45f), new(0f, 1.5f, 5.6f), new(0f, 1.9f, 1.45f), 0.04f, SailShade);   // jib
    }

    private static void BuildCatamaranSailboat(MeshBuilder b)
    {
        foreach (var x in new[] { -2.7f, 2.7f })
        {
            AddHull(b, x, length: 12f, beam: 1.6f, freeboard: 1.0f, draft: 0.4f, new HullPaint(White, OffWhite, Navy));
        }

        b.AddBox(new(0f, 1.2f, -0.8f), new(5.6f, 0.35f, 8.5f), OffWhite);      // bridge deck
        b.AddBox(new(0f, 1.95f, -1.6f), new(4.4f, 1.2f, 4.8f), White);         // cabin
        b.AddBox(new(0f, 2.15f, -1.6f), new(4.48f, 0.4f, 4.88f), Glass);       // window band
        b.AddBox(new(0f, 1.05f, 4.2f), new(3.8f, 0.06f, 3.2f), Dark);          // trampoline
        b.AddCylinder(new(0f, 2.55f, 1.8f), new(0f, 17f, 1.8f), 0.12f, 0.08f, 6, Metal);
        b.AddCylinder(new(0f, 3.0f, 1.7f), new(0f, 3.0f, -3.8f), 0.08f, 0.08f, 5, Metal);
        b.AddTriangularPlate(new(0f, 3.15f, 1.65f), new(0f, 16.6f, 1.65f), new(0f, 3.15f, -3.7f), 0.04f, SailCloth);
        b.AddTriangularPlate(new(0f, 15f, 1.95f), new(0f, 1.4f, 5.7f), new(0f, 1.8f, 1.95f), 0.04f, SailShade);
    }

    private static void BuildDayMotorBoat(MeshBuilder b)
    {
        AddHull(b, 0f, length: 7f, beam: 2.5f, freeboard: 0.85f, draft: 0.35f, new HullPaint(White, LightGrey, Red));
        b.AddBox(new(0f, 1.2f, 0.1f), new(0.9f, 0.7f, 0.7f), OffWhite);                       // console
        b.AddPrismX(-1.0f, 1.0f, new[] { (0.85f, 0.95f), (0.85f, 0.55f), (1.45f, 0.5f) }, Glass); // windshield
        b.AddBox(new(0f, 1.05f, -1.4f), new(1.7f, 0.45f, 0.9f), Beige);                         // bench seat
        b.AddBox(new(0f, 0.95f, 2.1f), new(1.2f, 0.2f, 1.2f), Beige);                           // bow cushion
        b.AddBox(new(0f, 0.95f, -3.75f), new(0.45f, 0.9f, 0.55f), Dark);                        // outboard
        b.AddBox(new(0f, 1.55f, -3.75f), new(0.55f, 0.45f, 0.65f), Dark);                       // cowl
        b.AddBox(new(0f, 0.1f, -3.8f), new(0.18f, 0.8f, 0.25f), Dark);                          // leg
    }

    private static void BuildCatamaranMotorboat(MeshBuilder b)
    {
        foreach (var x in new[] { -2.45f, 2.45f })
        {
            AddHull(b, x, length: 13f, beam: 1.6f, freeboard: 1.2f, draft: 0.5f, new HullPaint(White, OffWhite, Charcoal));
        }

        b.AddBox(new(0f, 1.3f, -0.4f), new(6.2f, 0.35f, 11.5f), OffWhite);   // main deck
        b.AddBox(new(0f, 2.3f, -1.3f), new(5.6f, 1.7f, 6.2f), White);        // saloon
        b.AddBox(new(0f, 2.45f, -1.3f), new(5.68f, 0.7f, 6.28f), Glass);     // windows
        b.AddBox(new(0f, 3.3f, -1.8f), new(5.2f, 0.22f, 5.4f), White);       // hardtop
        b.AddBox(new(0f, 3.66f, -3.2f), new(3.0f, 0.5f, 1.4f), Beige);       // flybridge seating
        b.AddSphere(new(0f, 3.75f, -0.5f), 0.35f, 8, 5, White);              // radar dome
    }

    private static void BuildMotorYacht(MeshBuilder b)
    {
        AddHull(b, 0f, length: 20f, beam: 5.5f, freeboard: 1.9f, draft: 0.8f, new HullPaint(White, Teak, Navy));
        b.AddBox(new(0f, 0.45f, -10.5f), new(5.0f, 0.2f, 1.2f), Teak);        // swim platform
        b.AddBox(new(0f, 2.9f, -1.8f), new(4.7f, 2.0f, 11f), White);          // main superstructure
        b.AddBox(new(0f, 3.05f, -1.8f), new(4.78f, 0.75f, 11.08f), Glass);    // main windows
        b.AddPrismX(-2.35f, 2.35f, new[] { (1.9f, 3.7f), (3.9f, 3.7f), (1.9f, 5.2f) }, Glass); // raked windshield
        b.AddBox(new(0f, 4.65f, -2.6f), new(3.9f, 1.5f, 7.5f), White);        // upper deck
        b.AddBox(new(0f, 4.8f, -2.6f), new(3.98f, 0.5f, 7.58f), Glass);       // upper windows
        b.AddBox(new(0f, 6.0f, -3.3f), new(3.6f, 0.18f, 5.2f), White);        // flybridge hardtop
        foreach (var x in new[] { -1.6f, 1.6f })
        {
            foreach (var z in new[] { -1.0f, -5.6f })
            {
                b.AddCylinder(new(x, 5.4f, z), new(x, 5.95f, z), 0.06f, 0.06f, 5, Metal);
            }
        }

        b.AddCylinder(new(0f, 5.4f, -2.4f), new(0f, 6.9f, -2.4f), 0.08f, 0.06f, 5, Metal); // radar mast
        b.AddSphere(new(0f, 7.1f, -2.4f), 0.45f, 8, 5, White);
    }

    private static void BuildFishingBoat(MeshBuilder b)
    {
        AddHull(b, 0f, length: 10f, beam: 3.5f, freeboard: 1.35f, draft: 0.5f, new HullPaint(SeaBlue, OffWhite, Red));
        b.AddBox(new(0f, 2.3f, 0.9f), new(2.4f, 1.9f, 2.6f), White);          // wheelhouse
        b.AddBox(new(0f, 2.75f, 0.9f), new(2.48f, 0.55f, 2.68f), Glass);      // wheelhouse windows
        b.AddBox(new(0f, 3.33f, 0.8f), new(2.8f, 0.15f, 3.0f), White);        // roof
        b.AddBox(new(0f, 1.62f, -3.3f), new(1.4f, 0.55f, 0.9f), White);       // fish box
        foreach (var side in new[] { -1f, 1f })
        {
            b.AddCylinder(new(1.1f * side, 3.4f, 0.2f), new(4.2f * side, 8.2f, -1.5f), 0.06f, 0.03f, 5, Metal); // outrigger
            b.AddCylinder(new(0.7f * side, 3.4f, 0.4f * side), new(0.7f * side, 5.2f + side * 0.6f, 0.4f * side), 0.03f, 0.02f, 4, Dark); // antenna
            b.AddCylinder(new(1.3f * side, 1.35f, -4.4f), new(1.5f * side, 2.8f, -4.9f), 0.03f, 0.02f, 4, Dark); // rod
        }
    }

    private static void BuildJetSki(MeshBuilder b)
    {
        AddHull(b, 0f, length: 3.2f, beam: 1.2f, freeboard: 0.5f, draft: 0.2f, new HullPaint(Yellow, Yellow, Dark));
        b.AddBox(new(0f, 0.65f, -0.55f), new(0.5f, 0.3f, 1.3f), Dark);                        // seat
        b.AddPrismX(-0.45f, 0.45f, new[] { (0.5f, 0.1f), (0.9f, 0.1f), (0.5f, 1.2f) }, Yellow); // hood
        b.AddCylinder(new(0f, 0.8f, 0.3f), new(0f, 1.05f, 0.15f), 0.05f, 0.05f, 5, Dark);       // steering column
        b.AddCylinder(new(-0.35f, 1.05f, 0.15f), new(0.35f, 1.05f, 0.15f), 0.035f, 0.035f, 5, Dark); // handlebar
    }

    /// <summary>A small coastal ferry: a long hull, two decks of superstructure with a window band, and a funnel.</summary>
    private static void BuildFerry(MeshBuilder b)
    {
        const float length = 45f;
        const float beam = 11f;
        AddHull(b, 0f, length, beam, freeboard: 3.4f, draft: 2.2f, new HullPaint(White, LightGrey, Navy));

        // Two decks, the upper one set in, so it reads as a ferry rather than a barge at any distance.
        b.AddBox(new(0f, 5.2f, -2f), new(beam * 0.86f, 3.2f, length * 0.66f), OffWhite);
        b.AddBox(new(0f, 5.4f, -2f), new(beam * 0.88f, 1.1f, length * 0.67f), Glass);
        b.AddBox(new(0f, 8.2f, -4f), new(beam * 0.62f, 2.6f, length * 0.42f), White);
        b.AddBox(new(0f, 8.5f, -4f), new(beam * 0.64f, 0.9f, length * 0.43f), Glass);

        // Wheelhouse forward on the top deck, looking over the bow.
        b.AddBox(new(0f, 10.6f, 2.5f), new(beam * 0.5f, 2f, 5f), White);
        b.AddBox(new(0f, 10.9f, 2.5f), new(beam * 0.52f, 0.9f, 5.1f), Glass);

        // Funnel and mast.
        b.AddCylinder(new(0f, 9.5f, -9f), new(0f, 13.5f, -9f), 1.5f, 1.3f, 8, Red);
        b.AddCylinder(new(0f, 13.5f, -9f), new(0f, 13.9f, -9f), 1.35f, 1.35f, 8, Dark);
        b.AddCylinder(new(0f, 12.6f, 2.5f), new(0f, 17f, 2.5f), 0.14f, 0.09f, 5, Metal);

        // A boot-topping stripe along the hull and the open car deck aft.
        b.AddBox(new(0f, 3.5f, -length * 0.36f), new(beam * 0.9f, 0.25f, length * 0.2f), Charcoal);
    }

    /// <summary>
    /// A tapered hull: a narrow keel loop lofted to a wider deck outline with a pointed bow.
    /// The lower band uses the stripe (antifouling/boot-top) color.
    /// </summary>
    private static void AddHull(
        MeshBuilder b, float centerX, float length, float beam, float freeboard, float draft, HullPaint paint)
    {
        var keel = HullOutline(centerX, beam * 0.55f, length * 0.9f, -draft);
        var deck = HullOutline(centerX, beam, length, freeboard);
        var stripeFraction = (draft + 0.15f) / (draft + freeboard);
        var stripe = keel.Select((p, i) => Vector3.Lerp(p, deck[i], stripeFraction)).ToArray();

        b.AddLoft(keel, stripe, paint.Stripe, paint.Stripe, null);
        b.AddLoft(stripe, deck, paint.Hull, null, paint.Deck);
    }

    /// <summary>The colours of a hull: its sides, its deck, and the band along the waterline.</summary>
    private readonly record struct HullPaint(Vector3 Hull, Vector3 Deck, Vector3 Stripe);

    private static Vector3[] HullOutline(float cx, float beam, float length, float y)
    {
        var hb = beam * 0.5f;
        var hl = length * 0.5f;
        return new[]
        {
            new Vector3(cx - hb, y, -hl),
            new Vector3(cx + hb, y, -hl),
            new Vector3(cx + hb, y, hl * 0.3f),
            new Vector3(cx + hb * 0.62f, y, hl * 0.78f),
            new Vector3(cx, y, hl),
            new Vector3(cx - hb * 0.62f, y, hl * 0.78f),
            new Vector3(cx - hb, y, hl * 0.3f),
        };
    }
}
