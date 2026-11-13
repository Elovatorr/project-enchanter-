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

    private Camera3D _camera = null!;
    private float _pitch;

    public override void _Ready()
    {
        _camera = GetNode<Camera3D>("Camera3D");
        Input.MouseMode = Input.MouseModeEnum.Captured;
        GD.Print($"Player ready at {GlobalPosition} (WASD move, Space jump, Esc release mouse)");
    }

    public override void _UnhandledInput(InputEvent @event)
    {
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
    }

    public override void _PhysicsProcess(double delta)
    {
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
    }
}
