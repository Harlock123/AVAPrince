namespace AVAPrince.Engine;

/// <summary>A small random number generator whose state can be copied.</summary>
public struct Dice
{
    private ulong _s;
    public Dice(int seed) => _s = (ulong)seed * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL;

    public double Next()
    {
        _s ^= _s << 13;
        _s ^= _s >> 7;
        _s ^= _s << 17;
        return (_s >> 11) * (1.0 / (1UL << 53));
    }

    public int Next(int n) => (int)(Next() * n);
}

/// <summary>
/// The controls: left or right, up (jump; parry), down (climb down; crouch), and Shift (a careful step; strike; pick up
/// or drink).
/// </summary>
public readonly record struct Controls(int Dx = 0, bool Up = false, bool Down = false, bool Shift = false);

/// <summary>What the prince (or a guard) is doing. Each takes a set time, then the next is chosen.</summary>
public enum Act
{
    Stand, Turn, Step, Run, Bump, StandJump, RunJump, JumpUp, Hang, Climb, Drop, ClimbDown, Fall, Land, Crouch,
    Drink, Pick, Enter,
    // Fighting.
    Draw, Advance, Retreat, Strike, Parry, Hurt,
    Dead,
}

/// <summary>Someone in the palace: the prince, or a guard. Where, which way, doing what, and how strong.</summary>
public sealed class Body
{
    public int Col, Row, Facing = 1;
    public Act Act = Act.Stand;
    /// <summary>How far into the act, and how long it takes.</summary>
    public double T, Dur;
    /// <summary>Where the act started and where it ends (the screen draws the way between).</summary>
    public int FromCol, FromRow, ToCol, ToRow;
    public int Hp, MaxHp;
    public bool Sword, Running;
    public int Fallen;
    public double Skill;
    /// <summary>A strike has landed (or been parried) this act.</summary>
    public bool Struck;
    public Body Copy() => (Body)MemberwiseClone();
    public bool Alive => Act != Act.Dead;
    public bool Idle => Act is Act.Stand or Act.Hang || T >= Dur;
}

/// <summary>
/// Everything in the level that moves or changes, kept together so it can be copied whole: the palace as it is now
/// (floors fallen, potions drunk), the gates, the prince and the guards, the time.
/// </summary>
public sealed class World
{
    public LevelDef Def = null!;
    public Cell[,] Grid = new Cell[1, 1];
    /// <summary>How long each gate stays open; how long each loose floor has before it falls.</summary>
    public double[,] GateOpen = new double[1, 1], Shaking = new double[1, 1];
    public bool ExitOpen;
    public Body Prince = new();
    public List<Body> Guards = [];
    public double Clock;
    public Dice Dice = new(1);
    public bool Done;
    /// <summary>A world for trying moves out: gates count as open, choppers and loose floors do nothing.</summary>
    public bool Probe;
    public string Cause = "";
    public List<Sfx> Heard = [];

    public World Copy() => new()
    {
        Def = Def, Grid = (Cell[,])Grid.Clone(), GateOpen = (double[,])GateOpen.Clone(), Shaking = (double[,])Shaking.Clone(), ExitOpen = ExitOpen,
        Prince = Prince.Copy(), Guards = Guards.Select(g => g.Copy()).ToList(), Clock = Clock, Dice = Dice, Done = Done, Probe = Probe, Cause = Cause,
    };

    public int Cols => Grid.GetLength(0);
    public Cell At(int c, int r) => c < 0 || c >= Cols || r < 0 ? Cell.Wall : r >= Field.Rows ? Cell.Empty : Grid[c, r];
    public int Room => Math.Clamp(Prince.Col / Field.RoomCols, 0, Def.Rooms - 1);
}
