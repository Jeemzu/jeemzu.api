namespace JeemzuApi.Putt;

/// <summary>Gameplay constants shared by validation, simulation and the client protocol.</summary>
public static class PuttRules
{
    public const int HoleCount = 6;
    public const int MaxStrokeLimit = 12;
    public const int MaxPlayers = 6;
    public static readonly TimeSpan ReconnectWindow = TimeSpan.FromMinutes(5);

    public const double BallRadius = 0.2;
    public const double CupRadius = 0.32;
    public const double MaxShotSpeed = 18;
    /// <summary>Shots below this power are ignored so a mis-click never costs a stroke.</summary>
    public const double MinShotPower = 0.03;
    public const double RollDeceleration = 2.0;
    public const double RollDrag = 0.5;
    public const double StopSpeed = 0.08;
    public const double CaptureSpeed = 7.0;
    public const double WallRestitution = 0.75;
    public const double ObstacleRestitution = 0.8;

    public const double FixedStep = 1.0 / 120;
    public const double TransitionSeconds = 4.5;
    public const double FinalHoleSeconds = 2.0;

    public const double CoordinateLimit = 1000;
    public const int MaxBoundaryPoints = 64;
    public const int MaxObstacles = 48;

    public static readonly IReadOnlySet<string> Icons = new HashSet<string>
    {
        "frog", "fox", "owl", "cat", "bear", "rabbit", "penguin", "turtle", "dog", "unicorn", "octopus", "dino",
    };
}

public static class Geometry
{
    public static double Cross(Vec2 a, Vec2 b, Vec2 c) => (b.X - a.X) * (c.Z - a.Z) - (b.Z - a.Z) * (c.X - a.X);

    public static Vec2 ClosestOnSegment(Vec2 p, Vec2 a, Vec2 b)
    {
        double dx = b.X - a.X, dz = b.Z - a.Z;
        double len2 = dx * dx + dz * dz;
        if (len2 <= 0) return a;
        double t = Math.Clamp(((p.X - a.X) * dx + (p.Z - a.Z) * dz) / len2, 0, 1);
        return new Vec2(a.X + t * dx, a.Z + t * dz);
    }

    public static double Distance(Vec2 a, Vec2 b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));

    public static double DistanceToSegment(Vec2 p, Vec2 a, Vec2 b) => Distance(p, ClosestOnSegment(p, a, b));

    public static double DistanceToEdges(Vec2 p, IReadOnlyList<Vec2> polygon)
    {
        double min = double.MaxValue;
        for (int i = 0; i < polygon.Count; i++)
            min = Math.Min(min, DistanceToSegment(p, polygon[i], polygon[(i + 1) % polygon.Count]));
        return min;
    }

    public static bool PointInPolygon(Vec2 p, IReadOnlyList<Vec2> polygon)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            Vec2 a = polygon[i], b = polygon[j];
            if ((a.Z > p.Z) != (b.Z > p.Z) && p.X < (b.X - a.X) * (p.Z - a.Z) / (b.Z - a.Z) + a.X)
                inside = !inside;
        }
        return inside;
    }

    public static double SignedArea(IReadOnlyList<Vec2> polygon)
    {
        double area = 0;
        for (int i = 0; i < polygon.Count; i++)
        {
            Vec2 a = polygon[i], b = polygon[(i + 1) % polygon.Count];
            area += a.X * b.Z - b.X * a.Z;
        }
        return area / 2;
    }

    public static bool SegmentsIntersect(Vec2 a, Vec2 b, Vec2 c, Vec2 d)
    {
        double d1 = Cross(c, d, a), d2 = Cross(c, d, b), d3 = Cross(a, b, c), d4 = Cross(a, b, d);
        if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0))) return true;
        const double eps = 1e-9;
        return (Math.Abs(d1) < eps && OnSegment(c, d, a)) || (Math.Abs(d2) < eps && OnSegment(c, d, b))
            || (Math.Abs(d3) < eps && OnSegment(a, b, c)) || (Math.Abs(d4) < eps && OnSegment(a, b, d));
    }

    private static bool OnSegment(Vec2 a, Vec2 b, Vec2 p) =>
        p.X >= Math.Min(a.X, b.X) - 1e-9 && p.X <= Math.Max(a.X, b.X) + 1e-9
        && p.Z >= Math.Min(a.Z, b.Z) - 1e-9 && p.Z <= Math.Max(a.Z, b.Z) + 1e-9;

    /// <summary>True when no two non-adjacent edges touch and no edge has zero length.</summary>
    public static bool IsSimplePolygon(IReadOnlyList<Vec2> polygon)
    {
        int n = polygon.Count;
        for (int i = 0; i < n; i++)
        {
            if (Distance(polygon[i], polygon[(i + 1) % n]) < 1e-6) return false;
            for (int j = i + 1; j < n; j++)
            {
                if (j == i + 1 || (i == 0 && j == n - 1)) continue;
                if (SegmentsIntersect(polygon[i], polygon[(i + 1) % n], polygon[j], polygon[(j + 1) % n])) return false;
            }
        }
        return true;
    }

    /// <summary>Corners of a box obstacle, counter-clockwise.</summary>
    public static Vec2[] BoxCorners(Vec2 center, double width, double depth, double rotationDeg)
    {
        double r = rotationDeg * Math.PI / 180, c = Math.Cos(r), s = Math.Sin(r);
        double hw = width / 2, hd = depth / 2;
        Vec2 Corner(double x, double z) => new(center.X + x * c - z * s, center.Z + x * s + z * c);
        return [Corner(-hw, -hd), Corner(hw, -hd), Corner(hw, hd), Corner(-hw, hd)];
    }
}
