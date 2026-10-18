namespace AVAPrince.Engine;

/// <summary>
/// The rules, one 60th of a second at a time. The prince and the guards move an act at a time, each act taking its
/// set time; when one ends, the controls (or the guard's mind) choose the next.
/// </summary>
public static class Sim
{
    public const double GateSeconds = 12, ShakeSeconds = 0.5, ChopperCycle = 2.2, ChopperShut = 0.3;
    public const int Hp0 = 3;
    /// <summary>Strikes land this far into the act.</summary>
    public const double PrinceHit = 0.3, GuardHit = 0.42;

    public static double Duration(Act a) => a switch
    {
        Act.Turn => 0.18, Act.Step => 0.42, Act.Run => 0.22, Act.Bump => 0.25, Act.StandJump => 0.6, Act.RunJump => 0.7,
        Act.JumpUp => 0.45, Act.Climb => 0.55, Act.Drop => 0.3, Act.ClimbDown => 0.75, Act.Fall => 0.2, Act.Land => 0.3,
        Act.Crouch => 0.3, Act.Drink => 0.7, Act.Pick => 0.7, Act.Enter => 1.2,
        Act.Draw => 0.3, Act.Advance => 0.35, Act.Retreat => 0.35, Act.Strike => 0.5, Act.Parry => 0.4, Act.Hurt => 0.4,
        _ => 0,
    };

    public static void Start(World w, LevelDef def, Body? carry = null)
    {
        w.Def = def;
        w.Grid = (Cell[,])def.Grid.Clone();
        w.GateOpen = new double[def.Cols, Field.Rows];
        w.Shaking = new double[def.Cols, Field.Rows];
        w.ExitOpen = false;
        w.Done = false;
        w.Dice = new Dice(def.Number * 13);
        w.Prince = new Body
        {
            Col = def.Start.Col, Row = def.Start.Row, Facing = 1, Hp = carry?.Hp ?? Hp0, MaxHp = carry?.MaxHp ?? Hp0, Sword = carry?.Sword ?? false,
        };
        w.Guards = def.Guards.Select(g => new Body { Col = g.Col, Row = g.Row, Facing = g.Facing, Hp = g.Hp, MaxHp = g.Hp, Skill = g.Skill, Sword = true }).ToList();
        Settle(w.Prince);
    }

    private static void Settle(Body b)
    {
        b.FromCol = b.ToCol = b.Col;
        b.FromRow = b.ToRow = b.Row;
    }

    public static bool Open(World w, int c, int r)
    {
        var cell = w.At(c, r);
        return cell != Cell.Wall && !(cell == Cell.Gate && w.GateOpen[c, r] <= 0 && !w.Probe);
    }

    public static bool Stand(World w, int c, int r) => PalaceBook.Standable(w.At(c, r)) && Open(w, c, r);

    public static bool ChopperShutAt(World w, int c) => (w.Clock + c * 0.37) % ChopperCycle < ChopperShut;

    /// <summary>The guard in a square, if there is one standing there.</summary>
    public static Body? GuardAt(World w, int c, int r) => w.Guards.FirstOrDefault(g => g.Alive && g.Row == r && (g.Col == c || g.ToCol == c));

    /// <summary>The guard the prince is facing up to: alive, on his floor, close, with nothing between.</summary>
    public static Body? Foe(World w)
    {
        var p = w.Prince;
        foreach (var g in w.Guards)
        {
            if (!g.Alive || g.Row != p.Row || Math.Abs(g.Col - p.Col) > 3 || g.Col == p.Col) continue;
            int d = Math.Sign(g.Col - p.Col);
            bool clear = true;
            for (int c = p.Col + d; c != g.Col; c += d) clear &= Stand(w, c, p.Row);
            if (clear) return g;
        }
        return null;
    }

    public static void Step(World w, Controls c)
    {
        double dt = Field.Step;
        w.Clock += dt;
        // Gates close; loose floors fall.
        for (int x = 0; x < w.Cols; x++)
            for (int y = 0; y < Field.Rows; y++)
            {
                if (w.GateOpen[x, y] > 0 && (w.GateOpen[x, y] -= dt) <= 0) w.Heard.Add(Sfx.Gate);
                if (w.Shaking[x, y] > 0 && (w.Shaking[x, y] -= dt) <= 0 && !w.Probe) Collapse(w, x, y);
            }
        var p = w.Prince;
        // The choppers in the prince's room, as their blades shut.
        if (!w.Probe)
            for (int x = w.Room * Field.RoomCols; x < Math.Min(w.Cols, (w.Room + 1) * Field.RoomCols); x++)
                for (int y = 0; y < Field.Rows; y++)
                    if (w.Grid[x, y] == Cell.Chopper && (w.Clock + x * 0.37) % ChopperCycle < dt) w.Heard.Add(Sfx.Chop);
        if (p.Alive && !w.Done)
        {
            p.T += dt;
            if (p.T >= p.Dur && p.Act != Act.Hang) Arrive(w, p);
            if (p.Alive && (p.T >= p.Dur || p.Act == Act.Hang)) Next(w, p, c);
            if (p.Alive) Chop(w, p);
        }
        foreach (var g in w.Guards)
            if (g.Alive) GuardStep(w, g);
        Blows(w);
    }

    // ───────────────────────────── the prince's acts ─────────────────────────────

    private static void Begin(Body b, Act a, int toCol, int toRow)
    {
        b.FromCol = b.Col;
        b.FromRow = b.Row;
        b.ToCol = toCol;
        b.ToRow = toRow;
        b.Act = a;
        b.T = 0;
        b.Dur = Duration(a);
        b.Struck = false;
    }

    /// <summary>An act's end: where the prince now is, and what that square does to him.</summary>
    private static void Arrive(World w, Body p)
    {
        var a = p.Act;
        p.Col = p.ToCol;
        p.Row = p.ToRow;
        p.FromCol = p.Col;
        p.FromRow = p.Row;
        switch (a)
        {
            case Act.Enter:
                w.Done = true;
                return;
            case Act.Drink:
                Drink(w, p);
                Idle(p);
                return;
            case Act.Pick:
                if (w.At(p.Col, p.Row) == Cell.Sword) { w.Grid[p.Col, p.Row] = Cell.Floor; p.Sword = true; w.Heard.Add(Sfx.Sword); }
                Idle(p);
                return;
            case Act.JumpUp:
                // Up, and a ledge there to catch hold of: he hangs from it.
                if (HasLedge(w, p)) { p.Act = Act.Hang; p.T = p.Dur = 0; w.Heard.Add(Sfx.Grab); }
                else Idle(p);
                return;
            case Act.Stand or Act.Turn or Act.Bump or Act.Crouch or Act.Land or Act.Draw or Act.Parry or Act.Strike or Act.Hurt:
                Idle(p);
                return;
        }
        // Into empty air: a fall, a storey at a time.
        if (!Stand(w, p.Col, p.Row))
        {
            if (p.Row + 1 >= Field.Rows) { Die(w, "a fall"); return; }
            if (a != Act.Fall) p.Fallen = 0;
            p.Fallen++;
            Begin(p, Act.Fall, p.Col, p.Row + 1);
            return;
        }
        var cell = w.At(p.Col, p.Row);
        if (a == Act.Fall)
        {
            if (cell == Cell.Spikes || p.Fallen >= 3) { Die(w, cell == Cell.Spikes ? "the spikes" : "a fall"); return; }
            w.Heard.Add(Sfx.Land);
            if (p.Fallen == 2) { w.Cause = "a fall"; Hurt(w, p, 1); }
            p.Fallen = 0;
            if (!p.Alive) return;
            Tread(w, p.Col, p.Row);
            Begin(p, Act.Land, p.Col, p.Row);
            return;
        }
        if (cell == Cell.Spikes && a is Act.Run or Act.StandJump or Act.RunJump) { Die(w, "the spikes"); return; }
        if (a is Act.StandJump or Act.RunJump or Act.ClimbDown or Act.Drop) w.Heard.Add(Sfx.Land);
        Tread(w, p.Col, p.Row);
        Idle(p);
    }

    private static void Idle(Body p)
    {
        if (p.Act != Act.Run) p.Running = false;
        p.Act = Act.Stand;
        p.T = 0;
        p.Dur = 0;
    }

    /// <summary>What a square does when it's trodden on: plates press, loose floors start to shake.</summary>
    public static void Tread(World w, int c, int r)
    {
        switch (w.At(c, r))
        {
            case Cell.Plate:
                if (w.Def.Links.TryGetValue((c, r), out var gate))
                {
                    if (w.GateOpen[gate.Item1, gate.Item2] <= 0) w.Heard.Add(Sfx.Gate);
                    w.GateOpen[gate.Item1, gate.Item2] = GateSeconds;
                }
                break;
            case Cell.ExitPlate:
                if (!w.ExitOpen) w.Heard.Add(Sfx.ExitOpen);
                w.ExitOpen = true;
                break;
            case Cell.Loose:
                if (w.Shaking[c, r] <= 0) { w.Shaking[c, r] = ShakeSeconds; w.Heard.Add(Sfx.Shake); }
                break;
        }
    }

    /// <summary>A loose floor gives way: the square is open air, and the rubble lands on the floor below.</summary>
    private static void Collapse(World w, int c, int r)
    {
        w.Grid[c, r] = Cell.Empty;
        w.Shaking[c, r] = 0;
        w.Heard.Add(Sfx.Crash);
        for (int y = r + 1; y < Field.Rows; y++)
            if (PalaceBook.Standable(w.At(c, y)))
            {
                if (w.At(c, y) == Cell.Floor) w.Grid[c, y] = Cell.Debris;
                break;
            }
        var p = w.Prince;
        if (p.Alive && p.Col == c && p.Row == r && p.Act is Act.Stand or Act.Turn or Act.Crouch or Act.Land or Act.Drink or Act.Pick)
        {
            p.Fallen = 0;
            Begin(p, Act.Fall, c, r + 1);
            p.Fallen = 1;
        }
    }

    private static void Drink(World w, Body p)
    {
        var cell = w.At(p.Col, p.Row);
        if (cell is not (Cell.Potion or Cell.BigPotion)) return;
        w.Grid[p.Col, p.Row] = Cell.Floor;
        if (cell == Cell.BigPotion) { p.MaxHp = Math.Min(10, p.MaxHp + 1); p.Hp = p.MaxHp; }
        else p.Hp = Math.Min(p.MaxHp, p.Hp + 1);
        w.Heard.Add(Sfx.Drink);
    }

    private static bool HasLedge(World w, Body p) =>
        p.Row > 0 && w.At(p.Col, p.Row - 1) == Cell.Empty && Stand(w, p.Col + p.Facing, p.Row - 1);

    /// <summary>The next act, chosen by the controls.</summary>
    private static void Next(World w, Body p, Controls c)
    {
        int f = p.Facing;
        if (p.Act == Act.Hang)
        {
            if (c.Up) { Begin(p, Act.Climb, p.Col + f, p.Row - 1); w.Heard.Add(Sfx.Climb); }
            else if (c.Down) Begin(p, Act.Drop, p.Col, p.Row);
            return;
        }
        bool wasRunning = p.Running;
        p.Running = false;
        if (Foe(w) is { } foe && p.Sword)
        {
            Fight(w, p, foe, c);
            return;
        }
        if (c.Dx != 0 && c.Dx != f)
        {
            p.Facing = c.Dx;
            Begin(p, Act.Turn, p.Col, p.Row);
            return;
        }
        if (c.Up && c.Dx == f)
        {
            if (wasRunning) Jump(w, p, Act.RunJump, 3);
            else Jump(w, p, Act.StandJump, 2);
            return;
        }
        if (c.Up)
        {
            if (w.At(p.Col, p.Row) == Cell.Exit && w.ExitOpen) { Begin(p, Act.Enter, p.Col, p.Row); w.Heard.Add(Sfx.Enter); return; }
            Begin(p, Act.JumpUp, p.Col, p.Row);
            return;
        }
        if (c.Down)
        {
            // At an edge with floor a storey below: over it, hang, and down.
            if (w.At(p.Col + f, p.Row) == Cell.Empty && Stand(w, p.Col + f, p.Row + 1)) Begin(p, Act.ClimbDown, p.Col + f, p.Row + 1);
            else Begin(p, Act.Crouch, p.Col, p.Row);
            return;
        }
        if (c.Shift && c.Dx == 0)
        {
            var cell = w.At(p.Col, p.Row);
            if (cell is Cell.Potion or Cell.BigPotion) Begin(p, Act.Drink, p.Col, p.Row);
            else if (cell == Cell.Sword) Begin(p, Act.Pick, p.Col, p.Row);
            return;
        }
        if (c.Dx == f)
        {
            int to = p.Col + f;
            if (!Open(w, to, p.Row) || GuardAt(w, to, p.Row) != null) { Begin(p, Act.Bump, p.Col, p.Row); w.Heard.Add(Sfx.Bump); return; }
            if (c.Shift)
            {
                // A careful step: never over an edge.
                if (Stand(w, to, p.Row)) Begin(p, Act.Step, to, p.Row);
                else Begin(p, Act.Stand, p.Col, p.Row);
                return;
            }
            Begin(p, Act.Run, to, p.Row);
            p.Running = true;
        }
    }

    /// <summary>A jump of so many squares: as far as nothing's in the way.</summary>
    private static void Jump(World w, Body p, Act a, int squares)
    {
        int f = p.Facing, to = p.Col;
        for (int k = 1; k <= squares; k++)
        {
            if (!Open(w, p.Col + f * k, p.Row) || GuardAt(w, p.Col + f * k, p.Row) != null) break;
            to = p.Col + f * k;
        }
        Begin(p, a, to, p.Row);
        w.Heard.Add(Sfx.Jump);
    }

    private static void Chop(World w, Body p)
    {
        int c = p.T < p.Dur / 2 ? p.FromCol : p.ToCol;
        int r = p.T < p.Dur / 2 ? p.FromRow : p.ToRow;
        if (!w.Probe && w.At(c, r) == Cell.Chopper && ChopperShutAt(w, c) && p.Act is not (Act.Fall or Act.Hang)) Die(w, "the chopper");
    }

    public static void Die(World w, string cause)
    {
        var p = w.Prince;
        p.Act = Act.Dead;
        p.Hp = 0;
        w.Cause = cause;
        w.Heard.Add(Sfx.Die);
    }

    public static void Hurt(World w, Body b, int n)
    {
        b.Hp -= n;
        if (b == w.Prince)
        {
            w.Heard.Add(Sfx.Hurt);
            if (b.Hp <= 0) Die(w, w.Cause == "" ? "a guard" : w.Cause);
        }
        else if (b.Hp <= 0)
        {
            b.Act = Act.Dead;
            w.Heard.Add(Sfx.GuardDies);
        }
    }

    // ───────────────────────────── fighting ─────────────────────────────

    private static void Fight(World w, Body p, Body foe, Controls c)
    {
        int toward = Math.Sign(foe.Col - p.Col);
        if (p.Facing != toward) { p.Facing = toward; Begin(p, Act.Draw, p.Col, p.Row); w.Heard.Add(Sfx.Sword); return; }
        if (c.Shift) { Begin(p, Act.Strike, p.Col, p.Row); w.Heard.Add(Sfx.Swing); return; }
        if (c.Up) { Begin(p, Act.Parry, p.Col, p.Row); return; }
        int ahead = p.Col + toward;
        if (c.Dx == toward && foe.Col != ahead && foe.ToCol != ahead && Stand(w, ahead, p.Row)) { Begin(p, Act.Advance, ahead, p.Row); return; }
        if (c.Dx == -toward && Open(w, p.Col - toward, p.Row)) { Begin(p, Act.Retreat, p.Col - toward, p.Row); return; }
    }

    private static void GuardStep(World w, Body g)
    {
        g.T += Field.Step;
        if (g.T < g.Dur) return;
        if (g.Act is Act.Advance or Act.Retreat) { g.Col = g.ToCol; g.Row = g.ToRow; }
        g.Act = Act.Stand;
        g.FromCol = g.ToCol = g.Col;
        g.FromRow = g.ToRow = g.Row;
        var p = w.Prince;
        if (!p.Alive || p.Row != g.Row || Math.Abs(p.Col - g.Col) > 3 || Foe(w) != g) { g.T = g.Dur = 0; return; }
        int toward = Math.Sign(p.Col - g.Col);
        g.Facing = toward;
        int d = Math.Abs(p.Col - g.Col);
        // A strike coming: parry it, if he's quick enough.
        if (p.Act == Act.Strike && p.T < PrinceHit && w.Dice.Next() < g.Skill) { Begin(g, Act.Parry, g.Col, g.Row); return; }
        int next = g.Col + toward;
        if (d > 1 && Stand(w, next, g.Row) && p.Col != next && p.ToCol != next) { Begin(g, Act.Advance, next, g.Row); return; }
        if (d == 1 && w.Dice.Next() < 0.55) { Begin(g, Act.Strike, g.Col, g.Row); w.Heard.Add(Sfx.Swing); return; }
        Begin(g, Act.Stand, g.Col, g.Row);
        g.Dur = 0.25;
    }

    /// <summary>Strikes landing: a hit unless the other is parrying (or out of reach).</summary>
    private static void Blows(World w)
    {
        var p = w.Prince;
        if (!p.Alive) return;
        foreach (var g in w.Guards)
        {
            if (!g.Alive || g.Row != p.Row) continue;
            int d = Math.Abs(g.Col - p.Col);
            if (p.Act == Act.Strike && !p.Struck && p.T >= PrinceHit)
            {
                p.Struck = true;
                if (d == 1 && Math.Sign(g.Col - p.Col) == p.Facing)
                {
                    if (g.Act == Act.Parry) w.Heard.Add(Sfx.Clash);
                    else
                    {
                        Hurt(w, g, 1);
                        if (g.Alive) { Begin(g, Act.Hurt, g.Col, g.Row); w.Heard.Add(Sfx.Hit); }
                    }
                }
            }
            if (g.Act == Act.Strike && !g.Struck && g.T >= GuardHit)
            {
                g.Struck = true;
                if (d == 1 && Math.Sign(p.Col - g.Col) == g.Facing)
                {
                    if (p.Act == Act.Parry) w.Heard.Add(Sfx.Clash);
                    else
                    {
                        w.Cause = "a guard";
                        Hurt(w, p, p.Sword ? 1 : p.Hp);
                        if (p.Alive) Begin(p, Act.Hurt, p.Col, p.Row);
                    }
                }
            }
        }
    }
}
