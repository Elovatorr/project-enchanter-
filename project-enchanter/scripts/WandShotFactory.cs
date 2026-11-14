using Godot;
using System.Collections.Generic;

namespace ProjectEnchanter;

/// <summary>The visual shape a shot projectile uses.</summary>
public enum ShotShape
{
    Sphere,
    Cone,
    Cylinder,
}

/// <summary>
/// One projectile produced by a wand shot: shape, colour, size, velocity bias
/// (in camera space, -Z is forward), speed and lifetime in seconds.
/// </summary>
public sealed class ShotDescriptor
{
    public ShotShape Shape { get; init; }
    public Color Color { get; init; }

    /// <summary>Radius of the sphere / base radius of the cone / cylinder radius, in metres.</summary>
    public float Size { get; init; }

    /// <summary>Length of a cylinder beam, in metres (ignored by other shapes).</summary>
    public float Length { get; init; }

    /// <summary>Material opacity (1 = opaque); used to make jets look like water.</summary>
    public float Alpha { get; init; } = 1f;

    /// <summary>Emission strength multiplier (higher = brighter, more laser-like).</summary>
    public float EmissionStrength { get; init; } = 0.6f;

    /// <summary>Optional shader resource path; replaces the plain StandardMaterial3D.</summary>
    public string ShaderPath { get; init; } = "";

    /// <summary>Additive velocity bias in camera space (x right, y up, z back).</summary>
    public Vector3 Boost { get; init; }

    public float Speed { get; init; }
    public float Lifetime { get; init; }
}

/// <summary>
/// Pure logic: maps an enchanted element to the shot it fires. Kept out of the
/// Wand node so it is unit-testable.
/// </summary>
public static class WandShotFactory
{
    /// <summary>Procedural water-jet shader used by both water cylinder layers.</summary>
    public const string WaterShaderPath = "res://shaders/water_jet.gdshader";

    public static Color ColorFor(string element) => element switch
    {
        "fire" => new Color(1f, 0.25f, 0.15f),
        "water" => new Color(0.25f, 0.5f, 1f),
        "density" => new Color(0.25f, 0.85f, 0.35f),
        _ => new Color(1f, 0.8f, 0.3f),
    };

    /// <summary>Projectiles to spawn for the element; empty for unknown/none.</summary>
    public static IReadOnlyList<ShotDescriptor> For(string element) => element switch
    {
        // One basic fireball straight ahead.
        "fire" => new[]
        {
            new ShotDescriptor
            {
                Shape = ShotShape.Sphere,
                Color = ColorFor(element),
                Size = 0.12f,
                Boost = Vector3.Zero,
                Speed = 9f,
                Lifetime = 2.5f,
            },
        },
        // A straight water jet: a bright inner core inside a wider translucent
        // sheath, both long thin cylinders travelling dead ahead like a laser.
        "water" => new[]
        {
            new ShotDescriptor
            {
                Shape = ShotShape.Cylinder,
                Color = ColorFor(element),
                Size = 0.035f,
                Length = 2.6f,
                Alpha = 0.95f,
                EmissionStrength = 1.8f,
                ShaderPath = WaterShaderPath,
                Boost = Vector3.Zero,
                Speed = 26f,
                Lifetime = 0.5f,
            },
            new ShotDescriptor
            {
                Shape = ShotShape.Cylinder,
                Color = ColorFor(element),
                Size = 0.09f,
                Length = 2.4f,
                Alpha = 0.45f,
                EmissionStrength = 0.45f,
                ShaderPath = WaterShaderPath,
                Boost = Vector3.Zero,
                Speed = 26f,
                Lifetime = 0.5f,
            },
        },
        // Three green cones fanned left/centre/right.
        "density" => new[]
        {
            new ShotDescriptor
            {
                Shape = ShotShape.Cone,
                Color = ColorFor(element),
                Size = 0.07f,
                Boost = new Vector3(-0.12f, 0f, 0f),
                Speed = 6f,
                Lifetime = 1.5f,
            },
            new ShotDescriptor
            {
                Shape = ShotShape.Cone,
                Color = ColorFor(element),
                Size = 0.07f,
                Boost = Vector3.Zero,
                Speed = 6f,
                Lifetime = 1.5f,
            },
            new ShotDescriptor
            {
                Shape = ShotShape.Cone,
                Color = ColorFor(element),
                Size = 0.07f,
                Boost = new Vector3(0.12f, 0f, 0f),
                Speed = 6f,
                Lifetime = 1.5f,
            },
        },
        _ => System.Array.Empty<ShotDescriptor>(),
    };
}
