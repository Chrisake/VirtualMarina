using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using VirtualMarina.Core.Geometry;
using VirtualMarina.Core.Rendering;
using VirtualMarina.Core.Serialization;

namespace VirtualMarina.Core.Tests;

/// <summary>
/// Every setting of every style section, found by reflection rather than listed by hand, so a setting added later cannot be
/// forgotten by <see cref="MarinaStyle.Clone"/> or by the marina file without one of these failing.
/// </summary>
public class StyleCompletenessTests
{
    [Fact]
    public void EverySetting_IsFoundBySection()
    {
        var sections = StyleProperties.Sections().Select(section => section.Name).ToList();
        Assert.Equal(new[] { "Lighting", "Water", "Status", "Land", "Piers", "Labels", "Selection", "View", "Shadows" }, sections);
        Assert.True(StyleProperties.Settable(typeof(LandStyle)).Count() >= 14);
    }

    [Fact]
    public void Clone_CopiesEverySettingOfEverySection()
    {
        var style = StyleProperties.WithEverySettingChanged();
        var copy = style.Clone();
        StyleProperties.AssertSame(style, copy);
        foreach (var section in StyleProperties.Sections()) Assert.NotSame(section.GetValue(style), section.GetValue(copy));
    }

    [Fact]
    public void AMarinaFile_KeepsEverySettingOfEverySection()
    {
        var style = StyleProperties.WithEverySettingChanged();
        var reread = MarinaDocument.Parse(new MarinaDocument { Style = style }.ToJson()).Style;
        StyleProperties.AssertSame(style, reread);
        Assert.Equal(style.Water.GridResolution, reread.Water.GridResolution);
    }

    [Fact]
    public void LightingAndWater_RaiseChanged_LikeTheOtherSections()
    {
        var style = new MarinaStyle();
        var lighting = 0;
        var water = 0;
        style.Lighting.Changed += (_, _) => lighting++;
        style.Water.Changed += (_, _) => water++;

        style.Lighting.SunDirection = new Vector3(0.1f, 1f, 0.2f);
        style.Lighting.FogDensity = new LightingSettings().FogDensity;   // no change, no event
        style.Water.Size = 900f;
        style.Water.Size = 900f;
        style.Water.WaveAmplitude = float.NaN;                   // held to the default, which it already was

        Assert.Equal(1, lighting);
        Assert.Equal(1, water);
    }
}

/// <summary>Reflection over the style sections, shared by the tests that must cover every setting.</summary>
internal static class StyleProperties
{
    /// <summary>The sections of <see cref="MarinaStyle"/>, in declaration order.</summary>
    public static IEnumerable<PropertyInfo> Sections() =>
        typeof(MarinaStyle).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => typeof(StyleSection).IsAssignableFrom(property.PropertyType))
            .OrderBy(property => property.MetadataToken);

    /// <summary>The settings of a section that can be changed after it is built (init-only ones are left out).</summary>
    public static IEnumerable<PropertyInfo> Settable(Type section) =>
        section.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.SetMethod is { IsPublic: true } setter &&
                               !setter.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(IsExternalInit)))
            .OrderBy(property => property.MetadataToken);

    /// <summary>A style whose every setting differs from the default (and the fixed grid resolution too).</summary>
    public static MarinaStyle WithEverySettingChanged()
    {
        var style = new MarinaStyle { Water = new WaterSettings { GridResolution = 77 } };
        var defaults = new MarinaStyle();
        foreach (var section in Sections())
        {
            var target = section.GetValue(style)!;
            var original = section.GetValue(defaults)!;
            foreach (var property in Settable(section.PropertyType))
            {
                var changed = Candidates(property.PropertyType).FirstOrDefault(candidate =>
                {
                    property.SetValue(target, candidate);
                    return !Same(property.GetValue(original), property.GetValue(target));
                });

                Assert.True(changed is not null, $"{section.Name}.{property.Name} could not be given a value other than its default");
                property.SetValue(target, changed);
            }

            // Set in order, a later setting (GhostBoatOpacity) may move an earlier one; each must still differ from its default.
            foreach (var property in Settable(section.PropertyType))
            {
                Assert.False(Same(property.GetValue(original), property.GetValue(target)), $"{section.Name}.{property.Name} is still the default");
            }
        }

        return style;
    }

    /// <summary>Asserts every readable setting of every section is the same in both (within what the file format rounds to).</summary>
    public static void AssertSame(MarinaStyle expected, MarinaStyle actual)
    {
        foreach (var section in Sections())
        {
            var want = section.GetValue(expected)!;
            var got = section.GetValue(actual)!;
            foreach (var property in section.PropertyType.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.GetMethod is { IsPublic: true } && p.GetIndexParameters().Length == 0))
            {
                Assert.True(Same(property.GetValue(want), property.GetValue(got)),
                    $"{section.Name}.{property.Name}: expected {property.GetValue(want)}, got {property.GetValue(got)}");
            }
        }
    }

    private static bool Same(object? a, object? b) => (a, b) switch
    {
        (float x, float y) => MathF.Abs(x - y) < 1e-3f,
        (Vector3 x, Vector3 y) => Vector3.Distance(x, y) < 2e-3f,
        (ColorRgba x, ColorRgba y) => MathF.Abs(x.R - y.R) < 3e-3f && MathF.Abs(x.G - y.G) < 3e-3f && MathF.Abs(x.B - y.B) < 3e-3f && MathF.Abs(x.A - y.A) < 3e-3f,
        (LabelFontDefinition x, LabelFontDefinition y) => x.Name == y.Name && x.IsBold == y.IsBold && x.Encode().SequenceEqual(y.Encode()),
        _ => Equals(a, b),
    };

    private static IEnumerable<object?> Candidates(Type type)
    {
        if (type == typeof(float)) return new object[] { 0.37f, 0.73f, 1.3f, 2.2f, 7.5f, 33f, 0.05f };
        if (type == typeof(bool)) return new object[] { false, true };
        if (type == typeof(Vector3)) return new object[] { new Vector3(0.25f, 0.5f, 0.75f), new Vector3(0.5f, 0.25f, 0.125f) };
        if (type == typeof(ColorRgba)) return new object[] { ColorRgba.FromHex("#123456"), ColorRgba.FromHex("#654321") };
        if (type.IsEnum) return Enum.GetValues(type).Cast<object?>();
        if (type == typeof(LabelFontDefinition))
        {
            var glyph = new LabelGlyph('A', 0.6f, new IReadOnlyList<Vector2>[] { new[] { new Vector2(0, 0), new Vector2(0.25f, 0.75f), new Vector2(0.5f, 0) } });
            return new object[] { new LabelFontDefinition("Test Sans", new[] { glyph }, isBold: true) };
        }

        throw new NotSupportedException($"No test values for a style setting of type {type.Name}; add some here.");
    }
}
