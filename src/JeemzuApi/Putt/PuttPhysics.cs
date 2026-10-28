namespace JeemzuApi.Putt;

public sealed class Ball
{
    public double X;
    public double Z;
    public double VX;
    public double VZ;
    public bool Sunk;

    public double Speed => Math.Sqrt(VX * VX + VZ * VZ);
    public bool Moving => VX != 0 || VZ != 0;

    public void PlaceAt(Vec2 p)
    {
        X = p.X;
        Z = p.Z;
        VX = VZ = 0;
        Sunk = false;
    }
}

/// <summary>
/// Collision data for one hole, built once from its definition. Balls only collide with
/// the course (never with each other), so each ball steps independently.
/// </summary>
public sealed class HoleCollider
{
    private readonly Vec2[] _boundary;
    private readonly List<(Vec2 A, Vec2 B, double Restitution)> _segments = [];
    private readonly List<(Vec2 Center, double Radius)> _circles = [];
    private readonly List<Vec2[]> _boxes = [];

    public Vec2 Tee { get; }
    public Vec2 Cup { get; }

    public HoleCollider(HoleDefinition hole)
    {
        _boundary = hole.Boundary!.ToArray();
        Tee = hole.Tee!.Value;
        Cup = hole.Cup!.Value;
        for (int i = 0; i < _boundary.Length; i++)
            _segments.Add((_boundary[i], _boundary[(i + 1) % _boundary.Length], PuttRules.WallRestitution));
        foreach (var o in hole.Obstacles ?? [])
        {
            if (o.Type == ObstacleTypes.Cylinder)
            {
                _circles.Add((o.Center!.Value, o.Radius!.Value));
                continue;
            }
            var corners = Geometry.BoxCorners(o.Center!.Value, o.Width!.Value, o.Depth!.Value, o.RotationDeg ?? 0);
            _boxes.Add(corners);
            for (int i = 0; i < 4; i++) _segments.Add((corners[i], corners[(i + 1) % 4], PuttRules.ObstacleRestitution));
        }
    }

    public bool IsValidPosition(double x, double z)
    {
        var p = new Vec2(x, z);
        if (!Geometry.PointInPolygon(p, _boundary)) return false;
        foreach (var box in _boxes) if (Geometry.PointInPolygon(p, box)) return false;
        foreach (var (c, r) in _circles) if (Geometry.Distance(p, c) < r) return false;
        return true;
    }

    /// <summary>Advances one ball by <paramref name="dt"/> seconds; returns true if it dropped into the cup.</summary>
    public bool Step(Ball ball, double dt)
    {
        if (ball.Sunk || !ball.Moving) return false;
        const double r = PuttRules.BallRadius;
        // Sub-steps keep travel per step under half a radius, so fast shots cannot cross a wall.
        int steps = Math.Max(1, (int)Math.Ceiling(ball.Speed * dt / (r * 0.5)));
        double h = dt / steps;
        for (int s = 0; s < steps; s++)
        {
            double px = ball.X, pz = ball.Z;
            ball.X += ball.VX * h;
            ball.Z += ball.VZ * h;
            foreach (var (a, b, e) in _segments) Resolve(ball, Geometry.ClosestOnSegment(new Vec2(ball.X, ball.Z), a, b), r, e);
            foreach (var (c, radius) in _circles)
            {
                double dx = ball.X - c.X, dz = ball.Z - c.Z, d = Math.Sqrt(dx * dx + dz * dz);
                if (d <= 1e-9) continue;
                var surface = new Vec2(c.X + dx / d * radius, c.Z + dz / d * radius);
                if (d < radius + r) Resolve(ball, surface, r, PuttRules.ObstacleRestitution, inside: d < radius);
            }
            if (!IsValidPosition(ball.X, ball.Z))
            {
                ball.X = px;
                ball.Z = pz;
                ball.VX = ball.VZ = 0;
                return false;
            }
            if (Geometry.Distance(new Vec2(ball.X, ball.Z), Cup) < PuttRules.CupRadius && ball.Speed < PuttRules.CaptureSpeed)
            {
                ball.PlaceAt(Cup);
                ball.Sunk = true;
                return true;
            }
        }

        double speed = ball.Speed;
        double next = speed - (PuttRules.RollDeceleration + PuttRules.RollDrag * speed) * dt;
        if (next < PuttRules.StopSpeed) ball.VX = ball.VZ = 0;
        else
        {
            ball.VX *= next / speed;
            ball.VZ *= next / speed;
        }
        return false;
    }

    private static void Resolve(Ball ball, Vec2 contact, double r, double restitution, bool inside = false)
    {
        double dx = ball.X - contact.X, dz = ball.Z - contact.Z, d = Math.Sqrt(dx * dx + dz * dz);
        if (d >= r && !inside) return;
        double nx, nz;
        if (d > 1e-9)
        {
            nx = dx / d;
            nz = dz / d;
            if (inside)
            {
                nx = -nx;
                nz = -nz;
            }
        }
        else
        {
            double s = ball.Speed;
            if (s <= 0) return;
            nx = -ball.VX / s;
            nz = -ball.VZ / s;
        }
        double push = inside ? r + d : r - d;
        ball.X += nx * push;
        ball.Z += nz * push;
        double vn = ball.VX * nx + ball.VZ * nz;
        if (vn < 0)
        {
            ball.VX -= (1 + restitution) * vn * nx;
            ball.VZ -= (1 + restitution) * vn * nz;
        }
    }
}
