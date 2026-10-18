namespace AVAPrince.Engine;

/// <summary>
/// The screen: a room of the palace at a time, ten squares across and three floors high, with the strip along the
/// bottom for the prince's strength, the guard's, and the time.
/// </summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768;
    public const int RoomCols = 10, Rows = 3;
    public const double ColWidth = 100, RowHeight = 200, Left = 12, Top = 30;
    public const double PlayBottom = Top + Rows * RowHeight;
}

/// <summary>What a square of the palace is. Everything but a wall or empty air has a floor along its bottom.</summary>
public enum Cell : byte
{
    Wall,
    /// <summary>Open air: whatever enters falls through to the square below.</summary>
    Empty,
    Floor,
    /// <summary>A loose floor: it shakes when trodden on, then falls.</summary>
    Loose,
    /// <summary>Spikes: walked onto carefully they're safe; run, jump or fall onto them and they're the end.</summary>
    Spikes,
    /// <summary>A chopper: its blades shut every couple of seconds.</summary>
    Chopper,
    /// <summary>A gate: shut, it can't be passed; a plate opens it for a while.</summary>
    Gate,
    /// <summary>A pressure plate that opens a gate.</summary>
    Plate,
    /// <summary>The plate that opens the way out.</summary>
    ExitPlate,
    Potion,
    BigPotion,
    Sword,
    /// <summary>The door out of the level.</summary>
    Exit,
    /// <summary>Rubble from a fallen floor.</summary>
    Debris,
}

public sealed record GuardDef(int Col, int Row, int Facing, int Hp, double Skill);

/// <summary>A level: its squares (all its rooms side by side), where the prince starts, its guards, which plate opens which gate.</summary>
public sealed class LevelDef
{
    public int Number;
    public Cell[,] Grid = new Cell[1, Field.Rows];
    public (int Col, int Row) Start;
    public List<GuardDef> Guards = [];
    public Dictionary<(int, int), (int, int)> Links = [];
    public int Cols => Grid.GetLength(0);
    public int Rooms => Cols / Field.RoomCols;
    /// <summary>The path the level was built along, from the start to the door.</summary>
    public List<(int Col, int Row)> Path = [];
}

/// <summary>
/// The eight levels. Each is built along a path from the start to the door: runs of floor (some loose, some with spikes,
/// choppers, gates and their plates, potions, guards), gaps to jump, ledges up to climb and drops down. The plate that
/// opens the door is somewhere along the way. A level is kept only if the computer prince can get from the start to the
/// door on the map of its moves.
/// </summary>
public static class PalaceBook
{
    public const int Count = 8;

    private static readonly LevelDef?[] Built = new LevelDef?[Count];
    private static readonly Lock Gate = new();

    public static LevelDef Level(int n)
    {
        lock (Gate) return Built[n - 1] ??= Prove(n);
    }

    private static LevelDef Prove(int n)
    {
        for (int attempt = 0; attempt < 60; attempt++)
        {
            var def = Build(n, 1989 + n * 37 + attempt * 11);
            var g = Graph.Build(def, openGates: true);
            if (g.Reach(def.Start).Contains(Exit(def))) return def;
        }
        throw new InvalidOperationException($"level {n} could not be built");
    }

    public static (int Col, int Row) Exit(LevelDef def)
    {
        for (int c = 0; c < def.Cols; c++)
            for (int r = 0; r < Field.Rows; r++)
                if (def.Grid[c, r] == Cell.Exit) return (c, r);
        return (-1, -1);
    }

    public static bool Standable(Cell c) => c is not (Cell.Wall or Cell.Empty);

    public static LevelDef Build(int n, int seed)
    {
        var dice = new Dice(seed);
        int rooms = 4 + (n - 1) / 2, cols = rooms * Field.RoomCols;
        var def = new LevelDef { Number = n, Grid = new Cell[cols, Field.Rows] };
        var g = def.Grid;
        var path = def.Path;
        int r = n % 2 == 0 ? 1 : 2, c = 1;
        void Put(int col, int row, Cell cell)
        {
            g[col, row] = cell;
            path.Add((col, row));
        }
        // The gap's columns: open air all the way down and out of the bottom.
        void Gap(int col)
        {
            for (int row = r; row < Field.Rows; row++) g[col, row] = Cell.Empty;
        }
        def.Start = (1, r);
        Put(1, r, Cell.Floor);
        Put(2, r, Cell.Floor);
        c = 3;
        bool sword = n > 1;
        if (!sword)
        {
            Put(3, r, Cell.Floor);
            Put(4, r, Cell.Sword);
            Put(5, r, Cell.Floor);
            c = 6;
            sword = true;
        }
        double hard = 0.3 + n * 0.08;
        int guards = 0;
        while (c < cols - 8)
        {
            int roll = dice.Next(100);
            if (roll < 18 && c > 5)
            {
                // A gap to jump: one square or two, with floor both sides for the run-up and landing.
                int wide = 1 + dice.Next(2);
                for (int k = 0; k < wide; k++) Gap(c + k);
                c += wide;
                Put(c, r, Cell.Floor);
                Put(c + 1, r, Cell.Floor);
                c += 2;
            }
            else if (roll < 30 && r > 0)
            {
                // A ledge up: open above the last square, the floor of the one beyond a storey up.
                var (lc, lr) = path[^1];
                g[lc, lr - 1] = Cell.Empty;
                r--;
                Put(c, r, Cell.Floor);
                Put(c + 1, r, Cell.Floor);
                c += 2;
            }
            else if (roll < 40 && r < Field.Rows - 1)
            {
                // A drop down a storey.
                g[c, r] = Cell.Empty;
                r++;
                Put(c, r, Cell.Floor);
                Put(c + 1, r, Cell.Floor);
                c += 2;
            }
            else
            {
                // A run of floor, with something on it.
                int len = 3 + dice.Next(3);
                for (int k = 0; k < len; k++) Put(c + k, r, Cell.Floor);
                int mid = c + 1 + dice.Next(len - 2);
                int what = dice.Next(100);
                if (what < 22 * hard) g[mid, r] = Cell.Loose;
                else if (what < 40 * hard && n >= 2) g[mid, r] = Cell.Spikes;
                else if (what < 55 * hard && n >= 3) g[mid, r] = Cell.Chopper;
                else if (what < 70 * hard && len >= 4 && n >= 2)
                {
                    // A plate, then the gate it opens.
                    g[c, r] = Cell.Plate;
                    g[c + len - 1, r] = Cell.Gate;
                    def.Links[(c, r)] = (c + len - 1, r);
                }
                else if (what < 80 && len >= 4 && guards < 1 + n / 3)
                {
                    def.Guards.Add(new GuardDef(c + len - 1, r, -1, 2 + n / 3, Math.Min(0.75, 0.25 + n * 0.06)));
                    guards++;
                }
                else if (what < 88) g[mid, r] = dice.Next(4) == 0 ? Cell.BigPotion : Cell.Potion;
                c += len;
            }
        }
        // The last stretch, and the door; the plate for it somewhere back along the way.
        for (int k = 0; k < 5; k++) Put(c + k, r, Cell.Floor);
        g[c + 3, r] = Cell.Exit;
        var plain = path.Where(p => p.Col > 3 && p.Col < c && g[p.Col, p.Row] == Cell.Floor && !def.Guards.Any(gd => gd.Col == p.Col && gd.Row == p.Row)).ToList();
        var ep = plain[dice.Next(plain.Count)];
        g[ep.Col, ep.Row] = Cell.ExitPlate;
        return def;
    }
}
