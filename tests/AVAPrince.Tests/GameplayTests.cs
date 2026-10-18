using Avalonia.Input;
using AVAPrince.Engine;

namespace AVAPrince.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avaprince-tests-" + Guid.NewGuid().ToString("N"));

    private static GameEngine Engine() => new(HighScoreTable.Load(TempFolder())) { Silent = true };

    /// <summary>
    /// A one-room level: floor all along the bottom row (row 2) from column 1 to 8 (walls at the ends), whatever the
    /// build changes, and the prince at column 2 with his sword.
    /// </summary>
    private static World In(Action<LevelDef>? build = null, int col = 2, int row = 2, bool sword = true)
    {
        var def = new LevelDef { Number = 1, Grid = new Cell[Field.RoomCols, Field.Rows], Start = (col, row) };
        for (int c = 1; c <= 8; c++) def.Grid[c, 2] = Cell.Floor;
        build?.Invoke(def);
        var w = new World();
        Sim.Start(w, def, new Body { Hp = 3, MaxHp = 3, Sword = sword });
        return w;
    }

    private static void Run(World w, int steps, Controls c = default)
    {
        for (int i = 0; i < steps; i++) Sim.Step(w, c);
    }

    /// <summary>Holds the controls until the act wanted starts, then lets go until the prince is standing again.</summary>
    private static void Do(World w, Controls c, Act act)
    {
        for (int i = 0; i < 120 && w.Prince.Act != act; i++) Sim.Step(w, c);
        Assert.Equal(act, w.Prince.Act);
        for (int i = 0; i < 300 && w.Prince.Alive && w.Prince.Act != Act.Stand && w.Prince.Act != Act.Hang; i++) Sim.Step(w, default);
    }

    // ───────────────────────────── the levels ─────────────────────────────

    [Fact]
    public void A_level_is_the_same_from_the_same_seed_and_longer_level_by_level()
    {
        var a = PalaceBook.Build(5, 123);
        var b = PalaceBook.Build(5, 123);
        Assert.Equal(a.Grid.Cast<Cell>(), b.Grid.Cast<Cell>());
        for (int n = 2; n <= PalaceBook.Count; n++) Assert.True(PalaceBook.Level(n).Rooms >= PalaceBook.Level(n - 1).Rooms);
    }

    [Fact]
    public void Every_level_has_its_door_and_the_door_plate_and_the_door_can_be_reached()
    {
        for (int n = 1; n <= PalaceBook.Count; n++)
        {
            var def = PalaceBook.Level(n);
            Assert.Equal(1, def.Grid.Cast<Cell>().Count(c => c == Cell.Exit));
            Assert.Equal(1, def.Grid.Cast<Cell>().Count(c => c == Cell.ExitPlate));
            Assert.Contains(PalaceBook.Exit(def), Graph.Build(def).Reach(def.Start));
            foreach (var (plate, gate) in def.Links)
            {
                Assert.Equal(Cell.Plate, def.Grid[plate.Item1, plate.Item2]);
                Assert.Equal(Cell.Gate, def.Grid[gate.Item1, gate.Item2]);
            }
        }
        Assert.Equal(1, PalaceBook.Level(1).Grid.Cast<Cell>().Count(c => c == Cell.Sword));
    }

    // ───────────────────────────── moving ─────────────────────────────

    [Fact]
    public void Running_goes_a_square_at_a_time_and_bumps_into_walls()
    {
        var w = In();
        Run(w, 60, new Controls(1));
        Assert.True(w.Prince.Col >= 6);
        Run(w, 60, new Controls(1));
        Assert.Equal(8, w.Prince.Col);
        Assert.Contains(Sfx.Bump, w.Heard);
    }

    [Fact]
    public void A_careful_step_stops_at_an_edge_but_a_run_goes_over_it()
    {
        var w = In(d => d.Grid[5, 2] = Cell.Empty);
        Do(w, new Controls(1, Shift: true), Act.Step);
        Do(w, new Controls(1, Shift: true), Act.Step);
        Run(w, 60, new Controls(1, Shift: true));
        Assert.Equal(4, w.Prince.Col);
        Assert.True(w.Prince.Alive);
        Run(w, 120, new Controls(1));
        Assert.False(w.Prince.Alive);
        Assert.Equal("a fall", w.Cause);
    }

    [Fact]
    public void A_running_jump_clears_two_squares_a_standing_jump_one()
    {
        var w = In(d => { d.Grid[4, 2] = d.Grid[5, 2] = Cell.Empty; for (int r = 0; r < 2; r++) for (int c = 1; c <= 8; c++) d.Grid[c, r] = Cell.Empty; }, col: 2);
        // Running, then up while still running: a running jump from the next square.
        for (int i = 0; i < 30 && w.Prince.Act != Act.Run; i++) Sim.Step(w, new Controls(1));
        for (int i = 0; i < 60 && w.Prince.Act != Act.RunJump; i++) Sim.Step(w, new Controls(1, Up: true));
        Assert.Equal(Act.RunJump, w.Prince.Act);
        Run(w, 60);
        Assert.True(w.Prince.Alive);
        Assert.Equal(6, w.Prince.Col);

        var v = In(d => d.Grid[4, 2] = Cell.Empty, col: 3);
        Do(v, new Controls(1, Up: true), Act.StandJump);
        Assert.True(v.Prince.Alive);
        Assert.Equal(5, v.Prince.Col);
    }

    [Fact]
    public void Up_to_a_ledge_hang_and_climb_and_down_again()
    {
        var w = In(d => { d.Grid[4, 1] = Cell.Floor; d.Grid[5, 1] = Cell.Floor; d.Grid[3, 1] = Cell.Empty; d.Grid[4, 2] = Cell.Wall; d.Grid[5, 2] = Cell.Wall; }, col: 3);
        Do(w, new Controls(Up: true), Act.JumpUp);
        Assert.Equal(Act.Hang, w.Prince.Act);
        Do(w, new Controls(Up: true), Act.Climb);
        Assert.Equal((4, 1), (w.Prince.Col, w.Prince.Row));
        Do(w, new Controls(-1), Act.Turn);
        Do(w, new Controls(Down: true), Act.ClimbDown);
        Assert.Equal((3, 2), (w.Prince.Col, w.Prince.Row));
        Assert.Equal(3, w.Prince.Hp);
    }

    [Fact]
    public void A_fall_of_one_storey_is_nothing_two_hurts_three_kills()
    {
        var w = In(d => { for (int c = 1; c <= 8; c++) d.Grid[c, 0] = Cell.Floor; d.Grid[4, 0] = Cell.Empty; d.Grid[4, 1] = Cell.Empty; }, col: 3, row: 0);
        Run(w, 60, new Controls(1));
        Assert.True(w.Prince.Alive);
        Assert.Equal(2, w.Prince.Hp);
        Assert.Equal(2, w.Prince.Row);

        var v = In(d => { for (int c = 1; c <= 8; c++) d.Grid[c, 0] = Cell.Floor; d.Grid[4, 0] = d.Grid[4, 1] = d.Grid[4, 2] = Cell.Empty; }, col: 3, row: 0);
        Run(v, 90, new Controls(1));
        Assert.False(v.Prince.Alive);
    }

    // ───────────────────────────── traps ─────────────────────────────

    [Fact]
    public void Spikes_kill_a_runner_but_not_a_careful_step()
    {
        var w = In(d => d.Grid[4, 2] = Cell.Spikes);
        Run(w, 60, new Controls(1));
        Assert.False(w.Prince.Alive);
        Assert.Equal("the spikes", w.Cause);

        var v = In(d => d.Grid[4, 2] = Cell.Spikes);
        for (int i = 0; i < 3; i++) Do(v, new Controls(1, Shift: true), Act.Step);
        Assert.True(v.Prince.Alive);
        Assert.Equal(5, v.Prince.Col);
    }

    [Fact]
    public void A_loose_floor_falls_after_a_moment_taking_anyone_on_it()
    {
        var w = In(d => { for (int c = 1; c <= 8; c++) d.Grid[c, 1] = Cell.Floor; d.Grid[4, 1] = Cell.Loose; }, col: 3, row: 1);
        Do(w, new Controls(1, Shift: true), Act.Step);
        Run(w, 80);
        Assert.True(w.Prince.Alive);
        Assert.Equal(Cell.Empty, w.At(4, 1));
        Assert.Equal(2, w.Prince.Row);

        var v = In(d => d.Grid[4, 2] = Cell.Loose);
        Run(v, 50, new Controls(1));
        Assert.True(v.Prince.Alive);
        Assert.True(v.Prince.Col > 4);
    }

    [Fact]
    public void A_chopper_cuts_down_whoever_is_in_it_as_it_shuts()
    {
        var w = In(d => d.Grid[4, 2] = Cell.Chopper, col: 4);
        Run(w, (int)(Sim.ChopperCycle * 60) + 2);
        Assert.False(w.Prince.Alive);
        Assert.Equal("the chopper", w.Cause);
    }

    [Fact]
    public void A_plate_opens_its_gate_for_a_while()
    {
        // Shut, the gate stops him; the plate behind him opens it.
        var w = In(d => { d.Grid[1, 2] = Cell.Plate; d.Grid[6, 2] = Cell.Gate; d.Links[(1, 2)] = (6, 2); });
        Run(w, 120, new Controls(1));
        Assert.Equal(5, w.Prince.Col);
        var v = In(d => { d.Grid[3, 2] = Cell.Plate; d.Grid[6, 2] = Cell.Gate; d.Links[(3, 2)] = (6, 2); });
        Do(v, new Controls(1, Shift: true), Act.Step);
        Assert.True(v.GateOpen[6, 2] > 0);
        Run(v, 120, new Controls(1));
        Assert.True(v.Prince.Col >= 7);
        Run(v, (int)(Sim.GateSeconds * 60));
        Assert.True(v.GateOpen[6, 2] <= 0);
    }

    [Fact]
    public void The_door_opens_from_its_plate_and_up_goes_through_it()
    {
        var w = In(d => { d.Grid[3, 2] = Cell.ExitPlate; d.Grid[5, 2] = Cell.Exit; });
        Do(w, new Controls(1, Shift: true), Act.Step);
        Assert.True(w.ExitOpen);
        Do(w, new Controls(1, Shift: true), Act.Step);
        Do(w, new Controls(1, Shift: true), Act.Step);
        Do(w, new Controls(Up: true), Act.Enter);
        Assert.True(w.Done);
    }

    [Fact]
    public void Potions_heal_and_the_big_one_adds_strength_and_the_sword_is_picked_up()
    {
        var w = In(d => { d.Grid[2, 2] = Cell.Potion; d.Grid[3, 2] = Cell.BigPotion; d.Grid[4, 2] = Cell.Sword; }, sword: false);
        w.Prince.Hp = 1;
        Do(w, new Controls(Shift: true), Act.Drink);
        Assert.Equal(2, w.Prince.Hp);
        Do(w, new Controls(1, Shift: true), Act.Step);
        Do(w, new Controls(Shift: true), Act.Drink);
        Assert.Equal(4, w.Prince.MaxHp);
        Assert.Equal(4, w.Prince.Hp);
        Do(w, new Controls(1, Shift: true), Act.Step);
        Assert.False(w.Prince.Sword);
        Do(w, new Controls(Shift: true), Act.Pick);
        Assert.True(w.Prince.Sword);
    }

    // ───────────────────────────── guards ─────────────────────────────

    private static World Duel(double skill = 0.3, int hp = 2, bool sword = true) =>
        In(d => d.Guards.Add(new GuardDef(6, 2, -1, hp, skill)), col: 3, sword: sword);

    [Fact]
    public void A_guard_comes_on_and_strikes_and_a_parry_stops_it()
    {
        var w = Duel();
        Run(w, 60 * 5);
        Assert.True(w.Prince.Hp < 3);

        var v = Duel();
        for (int i = 0; i < 60 * 5; i++)
        {
            var g = v.Guards[0];
            bool parry = g.Act == Act.Strike && g.T < Sim.GuardHit;
            Sim.Step(v, new Controls(Up: parry));
        }
        Assert.Equal(3, v.Prince.Hp);
        Assert.Contains(Sfx.Clash, v.Heard);
    }

    [Fact]
    public void Strikes_wear_a_guard_down_and_without_a_sword_a_guard_kills_at_once()
    {
        var w = Duel(skill: 0);
        var p = new Pilot();
        for (int i = 0; i < 60 * 20 && w.Guards[0].Alive; i++) Sim.Step(w, p.Decide(w));
        Assert.False(w.Guards[0].Alive);
        Assert.True(w.Prince.Alive);

        var v = Duel(sword: false);
        Run(v, 60 * 6);
        Assert.False(v.Prince.Alive);
    }

    // ───────────────────────────── the game ─────────────────────────────

    [Fact]
    public void A_death_starts_the_level_again_and_the_clock_runs_on()
    {
        var g = Engine();
        g.Go(2);
        g.Run(60 * 3, new Controls(1));
        double clock = g.Clock;
        Sim.Die(g.World, "test");
        g.Run((int)(60 * GameEngine.DyingSeconds) + 5);
        Assert.Equal(2, g.Level);
        Assert.Equal(g.World.Def.Start, (g.World.Prince.Col, g.World.Prince.Row));
        Assert.True(g.Clock > clock);
    }

    [Fact]
    public void Through_the_door_is_the_next_level_and_the_hour_running_out_is_the_end()
    {
        var g = Engine();
        g.Go(1);
        g.World.Done = true;
        g.Run(5);
        g.Run((int)(60 * GameEngine.DoneSeconds) + 5);
        Assert.Equal(2, g.Level);
        Assert.Equal(GameEngine.LevelPoints, g.Score);
        g.SetClock(GameEngine.GameSeconds - 0.5);
        g.Run(60);
        Assert.Equal(GameState.GameOver, g.State);
    }

    [Fact]
    public void Left_alone_the_title_shows_the_computer_playing_then_the_high_scores()
    {
        var g = Engine();
        g.Run((int)(60 * GameEngine.TitleIdleSeconds) + 5);
        Assert.Equal(GameState.Attract, g.State);
        for (int i = 0; i < 60 * (GameEngine.AttractSeconds + 5) && g.State == GameState.Attract; i++) g.Tick(default);
        Assert.Equal(GameState.HighScores, g.State);
    }

    [Fact]
    public void Pause_stops_the_clock()
    {
        var g = Engine();
        g.Go(2);
        g.Run(30);
        double t = g.Clock;
        g.Pause();
        g.Run(200);
        Assert.Equal(t, g.Clock);
        g.Resume();
        g.Run(10);
        Assert.True(g.Clock > t);
    }

    [Fact]
    public void A_copy_of_the_world_goes_its_own_way()
    {
        var w = In(d => d.Grid[4, 2] = Cell.Loose);
        var c = w.Copy();
        Run(c, 60, new Controls(1));
        Assert.Equal(Cell.Loose, w.At(4, 2));
        Assert.Equal(2, w.Prince.Col);
        Assert.Equal(Cell.Empty, c.At(4, 2));
    }

    [Fact]
    public void The_keys_reach_the_controls()
    {
        var input = new InputHandler();
        input.OnKeyDown(Key.Left);
        Assert.Equal(-1, input.Player.Dx);
        input.OnKeyUp(Key.Left);
        input.OnKeyDown(Key.Up);
        Assert.True(input.Player.Up);
        input.OnKeyUp(Key.Up);
        input.OnKeyDown(Key.Down);
        Assert.True(input.Player.Down);
        input.OnKeyUp(Key.Down);
        input.OnKeyDown(Key.LeftShift);
        Assert.True(input.Player.Shift);
        input.OnKeyUp(Key.LeftShift);
        Assert.Equal(default, input.Player);
        input.Dispose();

        var g = Engine();
        g.Go(1);
        int col = g.World.Prince.Col;
        g.Run(40, new Controls(1));
        Assert.True(g.World.Prince.Col > col);
    }

    // ───────────────────────────── the computer prince ─────────────────────────────

    [Fact]
    public void The_computer_gets_through_every_level_and_just_running_and_jumping_does_not()
    {
        Body? carry = null;
        for (int n = 1; n <= PalaceBook.Count; n++)
        {
            var w = new World();
            Sim.Start(w, PalaceBook.Level(n), carry);
            var p = new Pilot();
            for (int t = 0; t < 60 * 200 && !w.Done && w.Prince.Alive; t++) Sim.Step(w, p.Decide(w));
            Assert.True(w.Done, $"level {n}: {w.Cause} at {w.Prince.Col},{w.Prince.Row} [{p.Doing}]");
            carry = w.Prince;
        }
        var lazy = new World();
        Sim.Start(lazy, PalaceBook.Level(1));
        for (int t = 0; t < 60 * 200 && !lazy.Done && lazy.Prince.Alive; t++) Sim.Step(lazy, new Controls(1, t % 50 == 0));
        Assert.False(lazy.Done);
    }
}
