using Godot;
using System.Collections.Generic;

namespace ProjectEnchanter;

/// <summary>
/// A wand: a small rectangular prism lying on the enchanting table. While being
/// enchanted it is shown up close in front of the camera; a successful cast
/// engraves the drawn rune onto its top face and tints it with the element
/// colour. Once enchanted it can be picked up (E) into the player's hand.
/// </summary>
public partial class Wand : StaticBody3D, IInteractable
{
    public string Element { get; private set; } = "";
    public bool Enchanted => Element.Length > 0;
    public bool Held { get; private set; }

    /// <summary>The strokes of the rune currently etched on the wand (for editing).</summary>
    public IReadOnlyList<IReadOnlyList<Vector2>> CurrentStrokes { get; private set; } = [];

    /// <summary>True while the wand is held up close for enchanting.</summary>
    public bool InCloseUp { get; private set; }

    private MeshInstance3D _mesh = null!;
    private CollisionShape3D _collision = null!;
    private MeshInstance3D _decal = null!;

    private Node? _restoreParent;
    private Transform3D _restoreTransform;

    public override void _Ready()
    {
        AddToGroup("wand");
        _mesh = GetNode<MeshInstance3D>("Mesh");
        _collision = GetNode<CollisionShape3D>("Collision");
        _decal = GetNode<MeshInstance3D>("RuneDecal");
        _decal.Visible = false;
    }

    public string InteractionPrompt => Held || InCloseUp
        ? ""
        : Enchanted
            ? "[E] Pick up wand"
            : "Enchant this wand at the table";

    public void Interact(PlayerController player)
    {
        if (!Enchanted || Held || InCloseUp) return;

        Camera3D camera = player.GetNode<Camera3D>("Camera3D");
        Reparent(camera, false);
        Position = new Vector3(0.32f, -0.24f, -0.6f);
        RotationDegrees = new Vector3(0f, 20f, -10f);
        _collision.Disabled = true;
        Held = true;
    }

    /// <summary>
    /// Moves the wand in front of the camera, large and with its rune face
    /// towards the viewer, for the enchanting overlay. Works while the wand is
    /// held, so an existing enchantment can be edited; <see cref="EndCloseUp"/>
    /// restores it to the hand.
    /// </summary>
    public void ShowCloseUp(Node3D camera)
    {
        if (InCloseUp) return;

        InCloseUp = true;
        _restoreParent = GetParent();
        _restoreTransform = GlobalTransform;

        if (GetParent() != camera) Reparent(camera, false);
        Position = new Vector3(0f, -0.05f, -0.42f);
        RotationDegrees = new Vector3(90f, 0f, 0f); // top face toward the camera
        Scale = new Vector3(1.4f, 1.4f, 1.4f);
        _collision.Disabled = true;
    }

    /// <summary>Returns the wand to where it was before the close-up (table or hand).</summary>
    public void EndCloseUp()
    {
        if (!InCloseUp) return;

        InCloseUp = false;
        if (_restoreParent is Node parent && IsInsideTree())
        {
            if (GetParent() != parent) Reparent(parent, false);
            GlobalTransform = _restoreTransform;
        }
        Scale = Vector3.One;
        _collision.Disabled = Held;
    }

    /// <summary>
    /// Fires the wand's element effect straight ahead of the camera (only while
    /// held): fire = one fireball, water = three water jets, density = three cones.
    /// </summary>
    public void Shoot(PlayerController player)
    {
        if (!Held) return;
        IReadOnlyList<ShotDescriptor> shots = WandShotFactory.For(Element);
        if (shots.Count == 0) return;

        Camera3D camera = player.GetNode<Camera3D>("Camera3D");
        Transform3D cam = camera.GlobalTransform;
        Vector3 forward = -cam.Basis.Z;
        Node scene = GetTree().CurrentScene;

        foreach (ShotDescriptor shot in shots)
        {
            Vector3 direction = (cam.Basis * shot.Boost + forward).Normalized();
            Projectile.Launch(scene, GlobalPosition, direction, shot);
        }
    }

    /// <summary>
    /// Applies an element to the wand: colour/glow plus the drawn rune etched
    /// onto the top face.
    /// </summary>
    public void Enchant(string element, IReadOnlyList<IReadOnlyList<Vector2>> strokes)
    {
        Element = element;
        Color color = WandShotFactory.ColorFor(element);

        List<List<Vector2>> runeStrokes = [];
        foreach (IReadOnlyList<Vector2> stroke in strokes) runeStrokes.Add([.. stroke]);
        CurrentStrokes = runeStrokes;

        _mesh.MaterialOverride = new StandardMaterial3D
        {
            AlbedoColor = color,
            EmissionEnabled = true,
            Emission = color * 0.5f,
        };

        _decal.MaterialOverride = new StandardMaterial3D
        {
            AlbedoTexture = RuneTextureFactory.Create(strokes),
            AlbedoColor = color.Lightened(0.4f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        _decal.Visible = true;
    }
}
