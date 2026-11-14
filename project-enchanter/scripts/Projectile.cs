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

    /// < seconds elapsed since launch (tests observe movement via this). </summary>
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
        projectile.Position = position;
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
            _ => new SphereMesh { Radius = shot.Size },
        };

        MeshInstance3D meshNode = new()
        {
            Mesh = mesh,
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = shot.Color,
                EmissionEnabled = true,
                Emission = shot.Color * 0.6f,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            },
        };
        if (shot.Shape == ShotShape.Cone)
        {
            // Cone meshes point along +Y; rotate so the tip faces the node's -Z (forward).
            meshNode.RotationDegrees = new Vector3(-90f, 0f, 0f);
        }
        projectile.AddChild(meshNode);

        return projectile;
    }

    public override void _PhysicsProcess(double delta)
    {
        _age += (float)delta;
        TranslateObjectLocal(_direction * _speed * (float)delta);
        if (_age >= _lifetime) QueueFree();
    }
}
