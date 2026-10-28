namespace JeemzuApi.Putt;

/// <summary>
/// Built-in courses loaded from <c>Putt/Courses/*.json</c>. A handed-off course file is added
/// to the collection by copying it into that folder; invalid files stop startup with their errors.
/// </summary>
public sealed class CourseCatalog
{
    private readonly Dictionary<string, CourseFile> _byId = new(StringComparer.Ordinal);

    public IReadOnlyList<CourseFile> Courses { get; }

    public CourseCatalog(IEnumerable<CourseFile> courses)
    {
        var list = new List<CourseFile>();
        foreach (var course in courses)
        {
            if (!_byId.TryAdd(course.Id!, course))
                throw new InvalidOperationException($"Duplicate Putt Party course id '{course.Id}'.");
            list.Add(course);
        }
        if (list.Count == 0) throw new InvalidOperationException("No Putt Party courses were found.");
        Courses = list.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static string DefaultDirectory => Path.Combine(AppContext.BaseDirectory, "Putt", "Courses");

    public static CourseCatalog LoadDirectory(string directory)
    {
        var courses = new List<CourseFile>();
        var problems = new List<string>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.json").Order(StringComparer.Ordinal))
        {
            var (course, errors) = CourseValidator.Parse(File.ReadAllText(file));
            if (course is not null) courses.Add(course);
            else problems.AddRange(errors.Select(e => $"{Path.GetFileName(file)}: {e}"));
        }
        if (problems.Count > 0)
            throw new InvalidOperationException("Invalid Putt Party course files:\n" + string.Join('\n', problems));
        return new CourseCatalog(courses);
    }

    public CourseFile? Find(string? id) => id is not null && _byId.TryGetValue(id, out var c) ? c : null;
}
