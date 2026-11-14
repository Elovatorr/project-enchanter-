using Godot;

namespace ProjectEnchanter;

/// <summary>
/// A shot projectile: moves along its launch direction and frees itself once
/// its lifetime elapses. Added to the "projectile" group for test counting.
/// </summary>
public partial class Projectile : Node3D
{
    private Vector3 _direction = Vector3.Zero;
    private float _speed;
    private float _lifetime;
    private float _age;

    /// <summary>Seconds elapsed since launch.</summary>
    public float Age => _age;

    public Vector3 Direction => _direction;

    /// <summary>Creates and attaches a projectile to <paramref name="parent"/>.</summary>
    public static Projectile Launch(Node parent, Vector3 position, Vector3 direction, ShotDescriptor shot)
    {
        Projectile projectile = new()
        {
            _direction = direction.Normalized(),
            _speed = shot.Speed,
            _lifetime = shot.Lifetime,
        };
        parent.AddChild(projectile);
        // Global, not local: the parent may be a scene root away from the origin.
        projectile.GlobalPosition = position;
        projectile.LookAt(position + projectile._direction, Vector3.Up); // -Z faces the shot direction
        projectile.AddToGroup("projectile");

        Mesh mesh = shot.Shape switch
        {
            ShotShape.Cone => new CylinderMesh
            {
                TopRadius = 0f,
                BottomRadius = shot.Size,
                Height = shot.Size * 2.5f,
            },
            ShotShape.Cylinder => new CylinderMesh
            {
                // Narrows toward the tip so the jet looks like it is spreading/atomising.
                TopRadius = shot.Size * 0.55f,
                BottomRadius = shot.Size,
                Height = shot.Length,
                RadialSegments = 12,
                Rings = 1,
            },
            _ => new SphereMesh { Radius = shot.Size },
        };

        float alpha = Mathf.Clamp(shot.Alpha, 0f, 1f);
        bool translucent = alpha < 1f;
        MeshInstance3D meshNode = new()
        {
            Mesh = mesh,
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(shot.Color.R, shot.Color.G, shot.Color.B, alpha),
                EmissionEnabled = true,
                Emission = shot.Color * shot.EmissionStrength,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                Transparency = translucent
                    ? BaseMaterial3D.TransparencyEnum.Alpha
                    : BaseMaterial3D.TransparencyEnum.Disabled,
                ShadingMode = translucent
                    ? BaseMaterial3D.ShadingModeEnum.Unshaded
                    : BaseMaterial3D.ShadingModeEnum.PerPixel,
            },
        };
        if (shot.Shape is ShotShape.Cone or ShotShape.Cylinder)
        {
            // These meshes point along +Y; rotate so the tip faces the node's -Z (forward).
            meshNode.RotationDegrees = new Vector3(-90f, 0f, 0f);
        }
        if (shot.Shape == ShotShape.Cylinder)
        {
            // The mesh is centred on its pivot; shift it back so the jet starts
            // at the muzzle and extends forward.
            meshNode.Position = new Vector3(0f, 0f, -shot.Length * 0.5f);
        }
        projectile.AddChild(meshNode);

        return projectile;
    }

    public override void _PhysicsProcess(double delta)
    {
        _age += (float)delta;
        GlobalPosition += _direction * _speed * (float)delta;
        if (_age >= _lifetime) QueueFree();
    }
}
