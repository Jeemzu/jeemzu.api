namespace JeemzuApi.Putt;

/// <summary>A point on the ground plane. Three.js renders it as (X, 0, Z).</summary>
public readonly record struct Vec2(double X, double Z);

/// <summary>
/// A hand-off course file (<c>better-putt-party-course</c>, version 1). The same shape is
/// stored in <c>Putt/Courses/*.json</c>, sent to clients, and produced by the future editor.
/// Field reference: docs/putt-party-courses.md in the frontend repository.
/// </summary>
public sealed class CourseFile
{
    public const string FormatName = "better-putt-party-course";
    public const int CurrentVersion = 1;

    public string? Format { get; init; }
    public int Version { get; init; }
    public string? Id { get; init; }
    public string? Name { get; init; }
    public string? Author { get; init; }
    public List<HoleDefinition>? Holes { get; init; }
}

public sealed class HoleDefinition
{
    public int Par { get; init; }
    public int StrokeLimit { get; init; }
    public Vec2? Tee { get; init; }
    public Vec2? Cup { get; init; }
    /// <summary>Simple polygon (world coordinates) enclosing the green; walls run along every edge.</summary>
    public List<Vec2>? Boundary { get; init; }
    public List<ObstacleDefinition>? Obstacles { get; init; }
}

/// <summary>
/// One obstacle. Fields not used by its <see cref="Type"/> must be omitted.
/// A single class (rather than polymorphic JSON) keeps hand-written files order-independent.
/// </summary>
public sealed class ObstacleDefinition
{
    public string? Type { get; init; }
    public Vec2? Center { get; init; }
    /// <summary>box: size along local X.</summary>
    public double? Width { get; init; }
    /// <summary>box: size along local Z.</summary>
    public double? Depth { get; init; }
    /// <summary>box: rotation about the vertical axis in degrees (counter-clockwise in X/Z).</summary>
    public double? RotationDeg { get; init; }
    /// <summary>cylinder: radius.</summary>
    public double? Radius { get; init; }
    /// <summary>Visual height only; collisions are 2D.</summary>
    public double? Height { get; init; }
}

public static class ObstacleTypes
{
    public const string Box = "box";
    public const string Cylinder = "cylinder";
}
