using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVAPrince.Engine;
using AVAPrince.Views;
using Controls = AVAPrince.Engine.Controls;

namespace AVAPrince.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set PRINCE_SHOTS to a folder to
/// save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open()
    {
        var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avaprince-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("PRINCE_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVAPrince-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer prince play until something worth a picture is on screen.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 200)
    {
        for (int t = 0; t < 60 * maxSeconds && g.State == GameState.Playing; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            g.Tick(g.Pilot.Decide(g));
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }

    private static void Moment(int level, string name, Func<GameEngine, bool> moment)
    {
        var (w, c, g) = Open();
        g.Go(level);
        Assert.True(PlayUntil(g, c, moment), $"never saw the moment for {name}");
        Shot(w, c, name);
    }

    private static bool Is(GameEngine g, Act a, double from, double to) => g.World.Prince.Act == a && g.World.Prince.T >= from * g.World.Prince.Dur && g.World.Prince.T <= to * g.World.Prince.Dur;

    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.1);
        Shot(w, c, "title");
        g.HighScores.Insert("AVA", 7350, 6);
        g.Idle((int)(60 * GameEngine.TitleIdleSeconds) + 10);
        for (int i = 0; i < 60 * 200 && g.State != GameState.HighScores; i++) g.Idle(1);
        Assert.Equal(GameState.HighScores, g.State);
        Shot(w, c, "high-scores");
    }

    [AvaloniaFact]
    public void The_sword() => Moment(1, "sword", g => Is(g, Act.Pick, 0.4, 0.6));

    [AvaloniaFact]
    public void A_running_jump() => Moment(3, "jump", g => Is(g, Act.RunJump, 0.4, 0.6));

    [AvaloniaFact]
    public void Climbing() => Moment(2, "climb", g => Is(g, Act.Climb, 0.3, 0.45));

    [AvaloniaFact]
    public void A_sword_fight() => Moment(4, "fight", g => Is(g, Act.Strike, 0.45, 0.7) && Sim.Foe(g.World) is { } f && Math.Abs(f.Col - g.World.Prince.Col) == 1);

    [AvaloniaFact]
    public void A_chopper() => Moment(5, "chopper", g => g.World.Prince.Idle && Enumerable.Range(g.World.Prince.Col - 2, 5).Any(c => g.World.At(c, g.World.Prince.Row) == Cell.Chopper));

    [AvaloniaFact]
    public void A_gate() => Moment(6, "gate", g => Enumerable.Range(g.World.Prince.Col, 4).Any(c => g.World.At(c, g.World.Prince.Row) == Cell.Gate && g.World.GateOpen[c, g.World.Prince.Row] > 0));

    [AvaloniaFact]
    public void The_door() => Moment(2, "door", g => Is(g, Act.Enter, 0.3, 0.5));

    [AvaloniaFact]
    public void The_princess()
    {
        var (w, c, g) = Open();
        g.Go(PalaceBook.Count);
        g.SetClock(41 * 60);
        Assert.True(PlayUntil(g, c, g => g.Phase == Phase.Done, 300));
        for (int i = 0; i < 60 * 10 && !(g.State == GameState.GameOver && g.StateTime > 1); i++) { g.Tick(default); if (i % 3 == 0) c.Advance(0.05); }
        Assert.True(g.Won);
        Shot(w, c, "princess");
    }
}
