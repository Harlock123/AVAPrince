namespace AVAPrince.Engine;

/// <summary>
/// The computer prince, for the demonstration and the tests. It knows every square it can stand in and where each
/// move takes it (the level's <see cref="Graph"/>, worked out with the gates open), and makes for the sword, then the
/// plate that opens the door, then the door. If a shut gate is in the way it goes to the gate's plate first. Before each
/// move it plays it out on a copy of the level, choppers, loose floors and all, with the move after it, and waits for
/// the moment it's safe. Facing a guard, it parries his strikes as they come and strikes back when he's open.
/// </summary>
public sealed class Pilot
{
    private Graph? _graph;
    private LevelDef? _for;
    private int _changes = -1;
    private readonly List<Intent> _intents = [];
    private Act _lastAct;
    private double _waited;

    public string Doing { get; private set; } = "";

    public void Reset()
    {
        _intents.Clear();
        _waited = 0;
        Doing = "";
    }

    public Controls Decide(GameEngine g) => g.Phase != Phase.Play ? default : Decide(g.World);

    public Controls Decide(World w)
    {
        var p = w.Prince;
        if (!p.Alive || w.Done) return default;
        if (Sim.Foe(w) is { } foe && p.Sword) { _lastAct = p.Act; return Fight(w, p, foe); }
        // A move under way: hold each part's controls until its act starts.
        if (_intents.Count > 0)
        {
            if (p.Act == _intents[0].Act && (_lastAct != p.Act || p.T <= Field.Step + 1e-9)) _intents.RemoveAt(0);
            else if (p.Act == Act.Stand && _lastAct == Act.Stand) _intents.Clear();
            _lastAct = p.Act;
            if (_intents.Count > 0) return _intents[0].Controls;
        }
        _lastAct = p.Act;
        if (!p.Idle) return default;
        // Something to do right here.
        var cell = w.At(p.Col, p.Row);
        if (cell == Cell.Sword && !p.Sword) { Doing = "taking the sword"; return Once(new Intent(new Controls(Shift: true), Act.Pick)); }
        if (cell == Cell.BigPotion || cell == Cell.Potion && p.Hp < p.MaxHp) { Doing = "drinking"; return Once(new Intent(new Controls(Shift: true), Act.Drink)); }
        if (cell == Cell.Exit && w.ExitOpen) { Doing = "through the door"; return Once(new Intent(new Controls(Up: true), Act.Enter)); }
        Map(w);
        var goal = Goal(w);
        var route = Route(w, (p.Col, p.Row), goal);
        if (route == null || route.Count == 0) { Doing = "stuck"; return default; }
        // A shut gate on the way: its plate first.
        if (ShutGate(w, route) is { } plate && plate != (p.Col, p.Row))
        {
            route = Route(w, (p.Col, p.Row), plate);
            if (route == null || route.Count == 0) { Doing = "stuck at a gate"; return default; }
            Doing = "to the gate's plate";
        }
        else Doing = goal == Sword(w) ? "after the sword" : w.ExitOpen ? "to the door" : "to the door's plate";
        var first = route[0];
        var second = route.Count > 1 ? route[1] : ((Move, int, (int, int), double, int)?)null;
        if (!Safe(w, first, second) && _waited < 8)
        {
            _waited += Field.Step;
            Doing += ": waiting";
            return default;
        }
        _waited = 0;
        _intents.AddRange(Graph.Intents(first.Move, first.Dir, p.Facing));
        return _intents[0].Controls;
    }

    private Controls Once(Intent i)
    {
        _intents.Clear();
        _intents.Add(i);
        return i.Controls;
    }

    // ───────────────────────────── where to ─────────────────────────────

    private static (int, int)? Sword(World w)
    {
        for (int c = 0; c < w.Cols; c++)
            for (int r = 0; r < Field.Rows; r++)
                if (w.At(c, r) == Cell.Sword) return (c, r);
        return null;
    }

    private static (int, int) Goal(World w)
    {
        if (!w.Prince.Sword && Sword(w) is { } s) return s;
        if (!w.ExitOpen)
            for (int c = 0; c < w.Cols; c++)
                for (int r = 0; r < Field.Rows; r++)
                    if (w.At(c, r) == Cell.ExitPlate) return (c, r);
        return PalaceBook.Exit(w.Def);
    }

    /// <summary>The map, made again when a floor has fallen.</summary>
    private void Map(World w)
    {
        int changes = 0;
        foreach (var c in w.Grid) if (c is Cell.Empty or Cell.Debris) changes++;
        if (_graph != null && _for == w.Def && changes == _changes) return;
        _for = w.Def;
        _changes = changes;
        _graph = Graph.Build(w.Def, openGates: true, state: w);
    }

    private List<(Move Move, int Dir, (int, int) To, double Seconds, int Hurt)>? Route(World w, (int, int) from, (int, int) to)
    {
        var dist = new Dictionary<(int, int), double> { [from] = 0 };
        var back = new Dictionary<(int, int), ((int, int) From, (Move, int, (int, int), double, int) Edge)>();
        var q = new PriorityQueue<(int, int), double>();
        q.Enqueue(from, 0);
        while (q.TryDequeue(out var at, out double d))
        {
            if (at == to) break;
            if (d > dist[at] || !_graph!.Edges.TryGetValue(at, out var edges)) continue;
            foreach (var e in edges)
            {
                // A fall that hurts costs a lot, and one that would kill isn't taken while hurt.
                if (e.Hurt >= w.Prince.Hp) continue;
                double nd = d + e.Seconds + e.Hurt * 20;
                if (dist.TryGetValue(e.To, out double old) && old <= nd) continue;
                dist[e.To] = nd;
                back[e.To] = (at, e);
                q.Enqueue(e.To, nd);
            }
        }
        if (!dist.ContainsKey(to)) return null;
        var route = new List<(Move, int, (int, int), double, int)>();
        for (var at = to; at != from; at = back[at].From) route.Insert(0, back[at].Edge);
        return route;
    }

    /// <summary>The plate for the first shut gate the route goes through (or past), if any.</summary>
    private static (int, int)? ShutGate(World w, List<(Move Move, int Dir, (int, int) To, double Seconds, int Hurt)> route)
    {
        var at = (w.Prince.Col, w.Prince.Row);
        foreach (var e in route)
        {
            int c0 = at.Col, c1 = e.To.Item1;
            for (int c = Math.Min(c0, c1); c <= Math.Max(c0, c1); c++)
                foreach (int r in new[] { at.Row, e.To.Item2 })
                    if (w.At(c, r) == Cell.Gate && w.GateOpen[c, r] < 3)
                        foreach (var (plate, gate) in w.Def.Links)
                            if (gate == (c, r)) return plate;
            at = (e.To.Item1, e.To.Item2);
        }
        return null;
    }

    /// <summary>Plays the next move out on a copy of the level (and the move after, so as not to stop on a loose floor).</summary>
    private static bool Safe(World w, (Move Move, int Dir, (int, int) To, double, int) first, (Move Move, int Dir, (int, int), double, int)? second)
    {
        var c = w.Copy();
        var p = c.Prince;
        var moves = Graph.Intents(first.Move, first.Dir, p.Facing);
        // Coming face to face with a guard is the fight's business, not this move's.
        static bool Engaged(World x) => x.Prince.Sword && Sim.Foe(x) != null;
        if (Graph.Play(c, moves, 300, Engaged) == null || !p.Alive) return false;
        if (Engaged(c)) return true;
        if (second is { } s)
        {
            if (Graph.Play(c, Graph.Intents(s.Move, s.Dir, p.Facing), 300) == null) return p.Alive;
            if (!p.Alive) return false;
        }
        for (int i = 0; i < 20 && p.Alive; i++) Sim.Step(c, default);
        return p.Alive || c.Cause is "a guard";
    }

    // ───────────────────────────── a sword fight ─────────────────────────────

    private Controls Fight(World w, Body p, Body foe)
    {
        int toward = Math.Sign(foe.Col - p.Col), d = Math.Abs(foe.Col - p.Col);
        Doing = "fighting a guard";
        _intents.Clear();
        if (foe.Act == Act.Strike && foe.T < Sim.GuardHit && d == 1) return new Controls(Up: true);
        if (d > 1) return new Controls(toward);
        if (foe.Act != Act.Parry && foe.Act != Act.Strike) return new Controls(Shift: true);
        return default;
    }
}
