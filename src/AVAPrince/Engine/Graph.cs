namespace AVAPrince.Engine;

/// <summary>A move the prince can make from where he stands, in a direction.</summary>
public enum Move { Run, Step, StandJump, RunJump, Climb, ClimbDown }

/// <summary>One part of a move: the controls to hold until the act wanted starts.</summary>
public readonly record struct Intent(Controls Controls, Act Act);

/// <summary>
/// Every square the prince can stand in, and where each move takes him from there, found by trying the moves out in
/// the game's own rules (gates open, no guards, no choppers). The levels are kept only if this shows the door can be
/// reached from the start; the computer prince plans its way by it.
/// </summary>
public sealed class Graph
{
    public static readonly Move[] Moves = Enum.GetValues<Move>();
    public readonly Dictionary<(int Col, int Row), List<(Move Move, int Dir, (int Col, int Row) To, double Seconds, int Hurt)>> Edges = [];

    /// <summary>The parts of a move, in order (a turn first if he's facing the wrong way).</summary>
    public static List<Intent> Intents(Move m, int dir, int facing)
    {
        var list = new List<Intent>();
        if (facing != dir) list.Add(new Intent(new Controls(dir), Act.Turn));
        switch (m)
        {
            case Move.Run: list.Add(new Intent(new Controls(dir), Act.Run)); break;
            case Move.Step: list.Add(new Intent(new Controls(dir, Shift: true), Act.Step)); break;
            case Move.StandJump: list.Add(new Intent(new Controls(dir, Up: true), Act.StandJump)); break;
            case Move.RunJump:
                list.Add(new Intent(new Controls(dir), Act.Run));
                list.Add(new Intent(new Controls(dir, Up: true), Act.RunJump));
                break;
            case Move.Climb:
                list.Add(new Intent(new Controls(Up: true), Act.JumpUp));
                list.Add(new Intent(new Controls(Up: true), Act.Climb));
                break;
            case Move.ClimbDown: list.Add(new Intent(new Controls(Down: true), Act.ClimbDown)); break;
        }
        return list;
    }

    public static Graph Build(LevelDef def, bool openGates = true, World? state = null)
    {
        var g = new Graph();
        var probe = new World { Probe = openGates, Def = def };
        Sim.Start(probe, def);
        if (state != null) probe.Grid = (Cell[,])state.Grid.Clone();
        probe.Guards.Clear();
        probe.ExitOpen = true;
        var grid = probe.Grid;
        for (int c = 0; c < def.Cols; c++)
            for (int r = 0; r < Field.Rows; r++)
            {
                if (!Sim.Stand(probe, c, r)) continue;
                var list = new List<(Move, int, (int, int), double, int)>();
                foreach (var m in Moves)
                    foreach (int d in new[] { -1, 1 })
                        if (Try(probe, grid, (c, r), m, d) is { } to) list.Add((m, d, to.To, to.Seconds, to.Hurt));
                g.Edges[(c, r)] = list;
            }
        return g;
    }

    /// <summary>A move tried from a square: where it ends (standing, alive), how long it took, and any hurt.</summary>
    public static ((int, int) To, double Seconds, int Hurt)? Try(World probe, Cell[,] grid, (int Col, int Row) from, Move m, int d)
    {
        var w = probe;
        w.Grid = (Cell[,])grid.Clone();
        w.Shaking = new double[w.Cols, Field.Rows];
        w.Prince = new Body { Col = from.Col, Row = from.Row, Facing = d, Hp = 9, MaxHp = 9, Sword = true };
        w.Prince.FromCol = w.Prince.ToCol = from.Col;
        w.Prince.FromRow = w.Prince.ToRow = from.Row;
        var run = Play(w, Intents(m, d, d), 300);
        if (run is not { } steps) return null;
        var p = w.Prince;
        if (!p.Alive || p.Act != Act.Stand || (p.Col, p.Row) == from) return null;
        return ((p.Col, p.Row), steps * Field.Step, 9 - p.Hp);
    }

    /// <summary>Plays a move's parts out until the prince is standing again: how many steps it took, or null if it never got going.</summary>
    public static int? Play(World w, List<Intent> intents, int limit, Func<World, bool>? stop = null)
    {
        int k = 0, steps = 0;
        var p = w.Prince;
        for (; steps < limit; steps++)
        {
            var c = k < intents.Count ? intents[k].Controls : default;
            var before = p.Act;
            Sim.Step(w, c);
            if (!p.Alive || stop?.Invoke(w) == true) return steps + 1;
            if (k < intents.Count && p.Act == intents[k].Act && (before != p.Act || p.T <= Field.Step + 1e-9)) k++;
            else if (k < intents.Count && p.Act == Act.Stand && steps > 2 && before == Act.Stand) return null;
            if (k >= intents.Count && p.Act == Act.Stand) return steps + 1;
        }
        return null;
    }

    /// <summary>Every square that can be reached from one.</summary>
    public HashSet<(int, int)> Reach((int, int) from)
    {
        var seen = new HashSet<(int, int)> { from };
        var q = new Queue<(int, int)>(seen);
        while (q.Count > 0)
        {
            var s = q.Dequeue();
            if (!Edges.TryGetValue(s, out var list)) continue;
            foreach (var e in list)
                if (seen.Add(e.To)) q.Enqueue(e.To);
        }
        return seen;
    }
}
