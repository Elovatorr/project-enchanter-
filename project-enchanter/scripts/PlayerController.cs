using Godot;
using System;

namespace ProjectEnchanter;

/// <summary>
/// First-person player: WASD to move, Shift to sprint, Space to jump,
/// mouse to look. Esc releases/recaptures the mouse.
/// </summary>
public partial class PlayerController : CharacterBody3D
{
    [Export] public float MoveSpeed { get; set; } = 4.5f;
    [Export] public float SprintSpeed { get; set; } = 7.5f;
    [Export] public float JumpVelocity { get; set; } = 4.5f;
    [Export] public float MouseSensitivity { get; set; } = 0.0025f;
    [Export] public float Gravity { get; set; } = 9.8f;
    [Export] public float InteractRange { get; set; } = 3f;

    /// <summary>
    /// Seconds between shots while holding the button with a water wand.
    /// 0.0225 s ≈ 44 shots/s — four times the original 0.09 s rate.
    /// </summary>
    [Export] public float WaterFireInterval { get; set; } = 0.0225f;

    private Camera3D _camera = null!;
    private float _pitch;
    private Hud? _hud;
    private Wand? _wand;
    private bool _fireHeld;
    private float _continuousCooldown;

    /// <summary>Lazy: the HUD may be readied after the player (scene order).</summary>
    private Hud? HudRef => _hud ??= GetTree().GetFirstNodeInGroup("hud") as Hud;

    /// <summary>Lazy for the same reason as the HUD lookup.</summary>
    private Wand? WandRef => _wand ??= GetTree().GetFirstNodeInGroup("wand") as Wand;

    /// <summary>False while a UI overlay is open (movement/look/interaction paused).</summary>
    public bool ControlsEnabled { get; private set; } = true;

    public void SetControlsEnabled(bool enabled)
    {
        ControlsEnabled = enabled;
        if (!enabled)
        {
            Velocity = Vector3.Zero;
            _fireHeld = false;
            _continuousCooldown = 0f;
            HudRef?.SetPrompt(null);
        }
    }

    public override void _Ready()
    {
        AddToGroup("player");
        _camera = GetNode<Camera3D>("Camera3D");
        
        Input.MouseMode = Input.MouseModeEnum.Captured;
        GD.Print($"Player ready at {GlobalPosition} (WASD move, Space jump, E interact, Esc release mouse)");
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!ControlsEnabled) return;

        if (@event is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            RotateY(-motion.Relative.X * MouseSensitivity);

            _pitch = Mathf.Clamp(
                _pitch - motion.Relative.Y * MouseSensitivity,
                Mathf.DegToRad(-89f),
                Mathf.DegToRad(89f));
            _camera.Rotation = new Vector3(_pitch, 0f, 0f);
        }
        else if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            Input.MouseMode = Input.MouseMode == Input.MouseModeEnum.Captured
                ? Input.MouseModeEnum.Visible
                : Input.MouseModeEnum.Captured;
        }
        else if (@event is InputEventKey { Pressed: true, Keycode: Key.E })
        {
            TryInteract();
        }
        else if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left } click)
        {
            _fireHeld = click.Pressed;
            if (click.Pressed)
            {
                // Fire immediately; continuous elements keep firing while held.
                _continuousCooldown = Mathf.Max(WaterFireInterval, 0.005f);
                Wand? wand = WandRef;
                if (wand is not null && wand.Held) wand.Shoot(this);
            }
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!ControlsEnabled)
        {
            Velocity = Vector3.Zero;
            return;
        }

        Vector3 velocity = Velocity;
        float dt = (float)delta;

        if (!IsOnFloor())
        {
            velocity.Y -= Gravity * dt;
        }
        else if (Input.IsPhysicalKeyPressed(Key.Space))
        {
            velocity.Y = JumpVelocity;
        }

        Vector2 input = Vector2.Zero;
        if (Input.IsPhysicalKeyPressed(Key.W)) input.Y -= 1f;
        if (Input.IsPhysicalKeyPressed(Key.S)) input.Y += 1f;
        if (Input.IsPhysicalKeyPressed(Key.A)) input.X -= 1f;
        if (Input.IsPhysicalKeyPressed(Key.D)) input.X += 1f;

        Vector3 direction = (Transform.Basis * new Vector3(input.X, 0f, input.Y)).Normalized();
        float speed = Input.IsPhysicalKeyPressed(Key.Shift) ? SprintSpeed : MoveSpeed;

        velocity.X = direction.X * speed;
        velocity.Z = direction.Z * speed;

        Velocity = velocity;
        MoveAndSlide();

        UpdatePrompt();
        HandleContinuousFire(dt);
    }

    /// <summary>
    /// Fires repeatedly while the left button is held and the equipped wand is a
    /// continuous element (water). Fire/density stay one shot per click. The
    /// cooldown carries over between frames so the average rate is independent
    /// of the physics tick rate.
    /// </summary>
    private void HandleContinuousFire(float dt)
    {
        Wand? wand = WandRef;
        if (!_fireHeld || wand is not { Held: true } || !WandShotFactory.FiresContinuously(wand.Element))
        {
            return;
        }

        float interval = Mathf.Max(WaterFireInterval, 0.005f);
        _continuousCooldown -= dt;
        while (_continuousCooldown <= 0f)
        {
            wand.Shoot(this);
            _continuousCooldown += interval;
        }
    }

    /// <summary>Updates the HUD prompt based on what the reticle is aimed at.</summary>
    private void UpdatePrompt()
    {
        IInteractable? target = RaycastInteractable();
        string? prompt = target?.InteractionPrompt;
        HudRef?.SetPrompt(string.IsNullOrEmpty(prompt) ? null : prompt);
    }

    /// <summary>Looks along the camera's forward axis for an interactable.</summary>
    public IInteractable? RaycastInteractable()
    {
        Vector3 from = _camera.GlobalPosition;
        Vector3 to = from + -_camera.GlobalTransform.Basis.Z * InteractRange;

        PhysicsRayQueryParameters3D query = PhysicsRayQueryParameters3D.Create(from, to);
        query.Exclude = new Godot.Collections.Array<Rid> { GetRid() };

        Godot.Collections.Dictionary hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0) return null;

        Node? collider = hit["collider"].As<Node>();
        return collider as IInteractable;
    }

    private void TryInteract()
    {
        RaycastInteractable()?.Interact(this);
    }
}
