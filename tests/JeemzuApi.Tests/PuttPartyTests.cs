using System.Text.Json.Nodes;
using JeemzuApi.Putt;

namespace JeemzuApi.Tests;

public class PuttCourseTests
{
    internal static string ShippedPath => Path.Combine(CourseCatalog.DefaultDirectory, "meadow-loop.json");

    internal static CourseCatalog Shipped() => CourseCatalog.LoadDirectory(CourseCatalog.DefaultDirectory);

    private static JsonNode Mutable() => JsonNode.Parse(File.ReadAllText(ShippedPath))!;

    private static IReadOnlyList<string> Errors(JsonNode node) => CourseValidator.Parse(node.ToJsonString()).Errors;

    [Fact]
    public void ShippedCoursesLoadAndValidate()
    {
        var catalog = Shipped();
        Assert.NotNull(catalog.Find("meadow-loop"));
        Assert.All(catalog.Courses, c => Assert.Empty(CourseValidator.Validate(c)));
    }

    [Fact]
    public void RejectsWrongHoleCount()
    {
        var node = Mutable();
        node["holes"]!.AsArray().RemoveAt(5);
        Assert.Contains(Errors(node), e => e.StartsWith("holes:"));
    }

    [Fact]
    public void RejectsUnknownFieldsSoTyposAreCaught()
    {
        var node = Mutable();
        node["holes"]![0]!["strokeLimt"] = 5;
        Assert.Contains(Errors(node), e => e.StartsWith("$: invalid JSON"));
    }

    [Fact]
    public void RejectsCupOutsideBoundaryAndStrokeLimitAboveTwelve()
    {
        var node = Mutable();
        node["holes"]![0]!["cup"] = new JsonObject { ["x"] = 50, ["z"] = 50 };
        node["holes"]![1]!["strokeLimit"] = 13;
        var errors = Errors(node);
        Assert.Contains(errors, e => e.StartsWith("holes[0].cup"));
        Assert.Contains(errors, e => e.StartsWith("holes[1].strokeLimit"));
    }

    [Fact]
    public void RejectsSelfIntersectingBoundary()
    {
        var node = Mutable();
        node["holes"]![0]!["boundary"] = JsonNode.Parse("""[{"x":-2,"z":0},{"x":2,"z":14},{"x":2,"z":0},{"x":-2,"z":14}]""");
        Assert.Contains(Errors(node), e => e.StartsWith("holes[0].boundary"));
    }

    [Fact]
    public void RejectsOverlappingHolesAndTeeInsideObstacle()
    {
        var node = Mutable();
        node["holes"]![1]!["boundary"] = node["holes"]![0]!["boundary"]!.DeepClone();
        node["holes"]![1]!["tee"] = new JsonObject { ["x"] = 0, ["z"] = 1.5 };
        node["holes"]![1]!["cup"] = new JsonObject { ["x"] = 0, ["z"] = 12 };
        node["holes"]![1]!["obstacles"] = new JsonArray();
        node["holes"]![2]!["tee"] = new JsonObject { ["x"] = 29.25, ["z"] = 7 };
        var errors = Errors(node);
        Assert.Contains(errors, e => e.Contains("overlaps holes[0]"));
        Assert.Contains(errors, e => e.StartsWith("holes[2].tee: overlaps an obstacle"));
    }

    [Fact]
    public void RejectsObstacleFieldsThatDoNotMatchItsType()
    {
        var node = Mutable();
        node["holes"]![1]!["obstacles"]![0]!["width"] = 2;
        node["holes"]![2]!["obstacles"]![0]!["type"] = "ramp";
        var errors = Errors(node);
        Assert.Contains(errors, e => e.StartsWith("holes[1].obstacles[0]"));
        Assert.Contains(errors, e => e.StartsWith("holes[2].obstacles[0].type"));
    }

    [Fact]
    public void CatalogRejectsDuplicateIds()
    {
        var course = Shipped().Courses[0];
        Assert.Throws<InvalidOperationException>(() => new CourseCatalog([course, course]));
    }
}

public class PuttPhysicsTests
{
    private static HoleDefinition Hole(int index) => PuttCourseTests.Shipped().Courses[0].Holes![index];

    private static void Run(HoleCollider collider, Ball ball, double seconds = 20)
    {
        for (double t = 0; t < seconds && ball.Moving; t += PuttRules.FixedStep) collider.Step(ball, PuttRules.FixedStep);
    }

    [Fact]
    public void SlowBallRollingOverCupDrops()
    {
        var collider = new HoleCollider(Hole(0));
        var ball = new Ball();
        ball.PlaceAt(new Vec2(0, 10.5));
        ball.VZ = 3;
        Run(collider, ball);
        Assert.True(ball.Sunk);
    }

    [Fact]
    public void FastBallSkipsOverCup()
    {
        var collider = new HoleCollider(Hole(0));
        var ball = new Ball();
        ball.PlaceAt(new Vec2(0, 11.5));
        ball.VZ = PuttRules.CaptureSpeed + 4;
        collider.Step(ball, PuttRules.FixedStep * 12);
        Assert.False(ball.Sunk);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(-1, 0)]
    [InlineData(0.3, 1)]
    [InlineData(0.7, -0.7)]
    public void MaxPowerShotsNeverLeaveTheGreenOrEnterObstacles(double dx, double dz)
    {
        for (int h = 0; h < PuttRules.HoleCount; h++)
        {
            var hole = Hole(h);
            var collider = new HoleCollider(hole);
            var ball = new Ball();
            ball.PlaceAt(hole.Tee!.Value);
            double len = Math.Sqrt(dx * dx + dz * dz);
            ball.VX = dx / len * PuttRules.MaxShotSpeed;
            ball.VZ = dz / len * PuttRules.MaxShotSpeed;
            for (int i = 0; i < 2400 && ball.Moving; i++)
            {
                collider.Step(ball, PuttRules.FixedStep);
                Assert.True(collider.IsValidPosition(ball.X, ball.Z), $"hole {h} left the green at ({ball.X}, {ball.Z})");
            }
            Assert.False(ball.Moving);
        }
    }

    [Fact]
    public void WallBounceReversesAndDampsVelocity()
    {
        var collider = new HoleCollider(Hole(0));
        var ball = new Ball();
        ball.PlaceAt(new Vec2(1.5, 5));
        ball.VX = 5;
        for (int i = 0; i < 20; i++) collider.Step(ball, PuttRules.FixedStep);
        Assert.True(ball.VX < 0);
        Assert.True(Math.Abs(ball.VX) < 5 * PuttRules.WallRestitution);
    }
}

public class PuttSessionTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static PuttSession NewSession() => new("ABCDEF", PuttCourseTests.Shipped(), T0);

    private static PuttPlayer Add(PuttSession s, string name = "Ann") => s.AddPlayer(name, "frog", T0).Player!;

    private static void Settle(PuttSession s, DateTimeOffset now, double seconds = 15)
    {
        for (double t = 0; t < seconds; t += 0.05) s.Advance(0.05, now);
    }

    /// <summary>Uses up every stroke with taps that cannot reach the cup.</summary>
    private static void Exhaust(PuttSession s, PuttPlayer p, DateTimeOffset now)
    {
        while (!p.HoleDone)
        {
            Assert.Null(s.Shoot(p.Id, -1, 0, 0.05, now));
            Settle(s, now, 2);
        }
    }

    [Fact]
    public void RejectsSeventhPlayerAndBadProfiles()
    {
        var s = NewSession();
        for (int i = 0; i < PuttRules.MaxPlayers; i++) Add(s, $"P{i}");
        Assert.Equal(PuttErrors.SessionFull, s.AddPlayer("Late", "frog", T0).Error);
        Assert.Equal(PuttErrors.InvalidName, NewSession().AddPlayer("   ", "frog", T0).Error);
        Assert.Equal(PuttErrors.InvalidIcon, NewSession().AddPlayer("Ann", "dragon", T0).Error);
        Assert.Equal(Enumerable.Range(0, 6), s.Players.Select(p => p.Color));
    }

    [Fact]
    public void OnlyHostCanStartAndLateJoinIsRejected()
    {
        var s = NewSession();
        var host = Add(s);
        var guest = Add(s, "Bob");
        Assert.Equal(PuttErrors.NotHost, s.Start(guest.Id, T0));
        Assert.Null(s.Start(host.Id, T0));
        Assert.Equal(SessionPhase.Playing, s.Phase);
        Assert.Equal(PuttErrors.GameInProgress, s.AddPlayer("Cy", "owl", T0).Error);
        Assert.Equal(s.Course!.Holes![0].Tee!.Value, new Vec2(host.Ball.X, host.Ball.Z));
    }

    [Fact]
    public void ShotsRequireARestingBall()
    {
        var s = NewSession();
        var p = Add(s);
        s.Start(p.Id, T0);
        Assert.Equal(PuttErrors.InvalidShot, s.Shoot(p.Id, 0, 1, 0.01, T0));
        Assert.Null(s.Shoot(p.Id, 0, 1, 0.5, T0));
        Assert.Equal(PuttErrors.BallMoving, s.Shoot(p.Id, 0, 1, 0.5, T0));
        Assert.Equal(1, p.Strokes);
    }

    [Fact]
    public void ReachingTheStrokeLimitScoresTwelveAndHoleWaitsForEveryConnectedPlayer()
    {
        var s = NewSession();
        var a = Add(s);
        var b = Add(s, "Bob");
        s.Start(a.Id, T0);
        Exhaust(s, a, T0);
        Assert.Equal([PuttRules.MaxStrokeLimit], a.Scores);
        Assert.Equal(SessionPhase.Playing, s.Phase);
        Exhaust(s, b, T0);
        Assert.Equal(SessionPhase.Transition, s.Phase);
        Settle(s, T0, PuttRules.TransitionSeconds + 0.1);
        Assert.Equal(SessionPhase.Playing, s.Phase);
        Assert.Equal(1, s.HoleIndex);
        Assert.Equal(0, a.Strokes);
    }

    [Fact]
    public void FullGameCrownsAllTiedLowestPlayers()
    {
        var s = NewSession();
        var a = Add(s);
        var b = Add(s, "Bob");
        s.Start(a.Id, T0);
        for (int hole = 0; hole < PuttRules.HoleCount; hole++)
        {
            Exhaust(s, a, T0);
            Exhaust(s, b, T0);
            Settle(s, T0, PuttRules.TransitionSeconds + 0.1);
        }
        Assert.Equal(SessionPhase.Finished, s.Phase);
        Assert.Equal(72, a.Total);
        Assert.Equal(new[] { a.Id, b.Id }.Order(), s.Winners.Order());
        Assert.Null(s.ReturnToLobby(a.Id, T0));
        Assert.Equal(SessionPhase.Lobby, s.Phase);
        Assert.Empty(a.Scores);
    }

    [Fact]
    public void SinkingScoresStrokes()
    {
        var s = NewSession();
        var p = Add(s);
        s.Start(p.Id, T0);
        p.Ball.PlaceAt(new Vec2(0, 10.5));
        Assert.Null(s.Shoot(p.Id, 0, 1, 3 / PuttRules.MaxShotSpeed, T0));
        Settle(s, T0, 5);
        Assert.Equal([1], p.Scores);
        Assert.Equal(SessionPhase.Transition, s.Phase);
    }

    [Fact]
    public void DisconnectedPlayerCanResumeWithinWindowOtherwiseIsDisqualified()
    {
        var s = NewSession();
        var a = Add(s);
        var b = Add(s, "Bob");
        s.Start(a.Id, T0);

        s.Disconnect(b.Id, T0);
        Assert.Null(s.Resume(b.Id, "wrong", T0));
        Assert.Same(b, s.Resume(b.Id, b.Token, T0.AddMinutes(4)));
        Assert.True(b.Connected);

        s.Disconnect(a.Id, T0.AddMinutes(4));
        Assert.Equal(b.Id, s.HostId);
        s.Advance(0.05, T0.AddMinutes(9.1));
        Assert.True(a.Disqualified);
        Assert.Null(s.Resume(a.Id, a.Token, T0.AddMinutes(9.2)));
    }

    [Fact]
    public void DisconnectedPlayersDoNotHoldUpTheHoleAndAreExcludedFromWinners()
    {
        var s = NewSession();
        var a = Add(s);
        var b = Add(s, "Bob");
        s.Start(a.Id, T0);
        s.Disconnect(b.Id, T0);
        for (int hole = 0; hole < PuttRules.HoleCount; hole++)
        {
            Exhaust(s, a, T0);
            Settle(s, T0, PuttRules.TransitionSeconds + 0.1);
        }
        Assert.Equal(SessionPhase.Finished, s.Phase);
        Assert.Equal(72, b.Total);
        Assert.Equal([a.Id], s.Winners);
    }

    [Fact]
    public void LeavingTheLobbyFreesTheSlotAndPassesHost()
    {
        var s = NewSession();
        var a = Add(s);
        var b = Add(s, "Bob");
        s.Leave(a.Id, T0);
        Assert.Single(s.Players);
        Assert.Equal(b.Id, s.HostId);
    }
}
