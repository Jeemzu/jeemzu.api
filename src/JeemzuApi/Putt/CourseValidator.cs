using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace JeemzuApi.Putt;

/// <summary>Parses and validates hand-off course files. Errors use JSON-path-like prefixes.</summary>
public static partial class CourseValidator
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex IdPattern();

    public static (CourseFile? Course, IReadOnlyList<string> Errors) Parse(string json)
    {
        CourseFile? course;
        try
        {
            course = JsonSerializer.Deserialize<CourseFile>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            return (null, [$"$: invalid JSON ({ex.Message})"]);
        }
        if (course is null) return (null, ["$: file is empty"]);
        var errors = Validate(course);
        return (errors.Count == 0 ? course : null, errors);
    }

    public static List<string> Validate(CourseFile course)
    {
        var errors = new List<string>();
        if (course.Format != CourseFile.FormatName) errors.Add($"format: must be \"{CourseFile.FormatName}\"");
        if (course.Version != CourseFile.CurrentVersion) errors.Add($"version: must be {CourseFile.CurrentVersion}");
        if (course.Id is null || course.Id.Length is < 3 or > 48 || !IdPattern().IsMatch(course.Id))
            errors.Add("id: 3-48 lowercase letters, digits and single hyphens");
        CheckText(errors, "name", course.Name);
        CheckText(errors, "author", course.Author);

        if (course.Holes is null || course.Holes.Count != PuttRules.HoleCount)
        {
            errors.Add($"holes: exactly {PuttRules.HoleCount} holes are required");
            return errors;
        }

        var bounds = new (double MinX, double MinZ, double MaxX, double MaxZ)?[course.Holes.Count];
        for (int i = 0; i < course.Holes.Count; i++)
            bounds[i] = ValidateHole(errors, $"holes[{i}]", course.Holes[i]);

        for (int i = 0; i < bounds.Length; i++)
            for (int j = i + 1; j < bounds.Length; j++)
                if (bounds[i] is { } a && bounds[j] is { } b
                    && a.MinX < b.MaxX && b.MinX < a.MaxX && a.MinZ < b.MaxZ && b.MinZ < a.MaxZ)
                    errors.Add($"holes[{j}].boundary: bounding box overlaps holes[{i}]");
        return errors;
    }

    private static void CheckText(List<string> errors, string path, string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > 48 || value.Any(char.IsControl))
            errors.Add($"{path}: 1-48 printable characters");
    }

    private static bool Finite(Vec2 p) =>
        double.IsFinite(p.X) && double.IsFinite(p.Z)
        && Math.Abs(p.X) <= PuttRules.CoordinateLimit && Math.Abs(p.Z) <= PuttRules.CoordinateLimit;

    private static (double, double, double, double)? ValidateHole(List<string> errors, string path, HoleDefinition? hole)
    {
        if (hole is null)
        {
            errors.Add($"{path}: missing");
            return null;
        }
        if (hole.StrokeLimit is < 1 or > PuttRules.MaxStrokeLimit)
            errors.Add($"{path}.strokeLimit: 1-{PuttRules.MaxStrokeLimit}");
        if (hole.Par < 1 || hole.Par > Math.Max(1, hole.StrokeLimit))
            errors.Add($"{path}.par: 1 to strokeLimit");

        var boundary = hole.Boundary;
        if (boundary is null || boundary.Count < 3 || boundary.Count > PuttRules.MaxBoundaryPoints)
        {
            errors.Add($"{path}.boundary: 3-{PuttRules.MaxBoundaryPoints} points");
            return null;
        }
        if (!boundary.All(Finite))
        {
            errors.Add($"{path}.boundary: coordinates must be within ±{PuttRules.CoordinateLimit}");
            return null;
        }
        if (!Geometry.IsSimplePolygon(boundary))
        {
            errors.Add($"{path}.boundary: edges must not cross, touch or repeat points");
            return null;
        }
        if (Math.Abs(Geometry.SignedArea(boundary)) < 1)
            errors.Add($"{path}.boundary: area must be at least 1");

        var obstacles = hole.Obstacles ?? [];
        if (obstacles.Count > PuttRules.MaxObstacles)
            errors.Add($"{path}.obstacles: at most {PuttRules.MaxObstacles}");
        var solids = new List<(Vec2[]? Box, Vec2 Center, double Radius)>();
        for (int i = 0; i < obstacles.Count; i++)
        {
            var solid = ValidateObstacle(errors, $"{path}.obstacles[{i}]", obstacles[i], boundary);
            if (solid is { } s) solids.Add(s);
        }

        CheckMarker(errors, $"{path}.tee", hole.Tee, PuttRules.BallRadius, boundary, solids);
        CheckMarker(errors, $"{path}.cup", hole.Cup, PuttRules.CupRadius, boundary, solids);
        if (hole.Tee is { } tee && hole.Cup is { } cup && Geometry.Distance(tee, cup) < 1)
            errors.Add($"{path}.cup: must be at least 1 unit from the tee");

        return (boundary.Min(p => p.X), boundary.Min(p => p.Z), boundary.Max(p => p.X), boundary.Max(p => p.Z));
    }

    private static (Vec2[]?, Vec2, double)? ValidateObstacle(List<string> errors, string path, ObstacleDefinition? o, List<Vec2> boundary)
    {
        if (o?.Center is not { } center || !Finite(center))
        {
            errors.Add($"{path}.center: required");
            return null;
        }
        if (!Geometry.PointInPolygon(center, boundary)) errors.Add($"{path}.center: must be inside the boundary");
        if (o.Height is { } h && (!double.IsFinite(h) || h < 0.1 || h > 5)) errors.Add($"{path}.height: 0.1-5");

        switch (o.Type)
        {
            case ObstacleTypes.Box:
                if (o.Radius is not null) errors.Add($"{path}.radius: not used by boxes");
                if (o.Width is not { } w || !double.IsFinite(w) || w < 0.1 || w > 100) errors.Add($"{path}.width: 0.1-100");
                else if (o.Depth is not { } d || !double.IsFinite(d) || d < 0.1 || d > 100) errors.Add($"{path}.depth: 0.1-100");
                else if (o.RotationDeg is { } r && !double.IsFinite(r)) errors.Add($"{path}.rotationDeg: must be a number");
                else return (Geometry.BoxCorners(center, w, d, o.RotationDeg ?? 0), center, 0);
                return null;
            case ObstacleTypes.Cylinder:
                if (o.Width is not null || o.Depth is not null || o.RotationDeg is not null)
                    errors.Add($"{path}: width, depth and rotationDeg are not used by cylinders");
                if (o.Radius is not { } radius || !double.IsFinite(radius) || radius < 0.1 || radius > 50)
                {
                    errors.Add($"{path}.radius: 0.1-50");
                    return null;
                }
                return (null, center, radius);
            default:
                errors.Add($"{path}.type: must be \"{ObstacleTypes.Box}\" or \"{ObstacleTypes.Cylinder}\"");
                return null;
        }
    }

    private static void CheckMarker(List<string> errors, string path, Vec2? marker, double clearance,
        List<Vec2> boundary, List<(Vec2[]? Box, Vec2 Center, double Radius)> solids)
    {
        if (marker is not { } p || !Finite(p))
        {
            errors.Add($"{path}: required");
            return;
        }
        if (!Geometry.PointInPolygon(p, boundary) || Geometry.DistanceToEdges(p, boundary) < clearance)
        {
            errors.Add($"{path}: must be inside the boundary, at least {clearance} from walls");
            return;
        }
        foreach (var (box, center, radius) in solids)
        {
            bool blocked = box is null
                ? Geometry.Distance(p, center) < radius + clearance
                : Geometry.PointInPolygon(p, box) || Geometry.DistanceToEdges(p, box) < clearance;
            if (blocked)
            {
                errors.Add($"{path}: overlaps an obstacle");
                return;
            }
        }
    }
}
