namespace AVAPrince.Engine;

public enum GameState { Title, Attract, Playing, Paused, GameOver, EnterInitials, HighScores }

public enum Phase
{
    /// <summary>The level's number and the time left, a moment before it starts.</summary>
    Start,
    Play,
    /// <summary>Killed: a moment, then the level from its start again (the clock keeps running).</summary>
    Dying,
    /// <summary>Through the door: on to the next level.</summary>
    Done,
}

/// <summary>Sounds the engine asks for; the host decides how they sound.</summary>
public enum Sfx
{
    Jump, Land, Bump, Grab, Climb, Shake, Crash, Gate, ExitOpen, Enter, Drink, Sword, Swing, Clash, Hit, Hurt, GuardDies, Die, Chop,
    Music, MusicStop, TitleTune, GameOver, Victory,
}

/// <summary>
/// The whole game with no UI. Call <see cref="Update"/> with the elapsed time and the controls; it runs in fixed
/// 60 Hz steps, so it plays the same at any frame rate. There's an hour to get through all eight levels; a death
/// starts the level again, but the clock doesn't stop.
/// </summary>
public sealed class GameEngine
{
    public const double TitleIdleSeconds = 16;
    public const double AttractSeconds = 55;
    public const double HighScoreSeconds = 9;
    public const double StartSeconds = 2.2, DyingSeconds = 2.4, DoneSeconds = 1.6, OverSeconds = 5;
    public const double GameSeconds = 60 * 60;
    public const int LevelPoints = 1000, GuardPoints = 250, MinutePoints = 100;

    private double _accumulator, _idle;
    private bool _startPrev, _tunePending = true, _music;
    private int _attractLevel;
    private Body? _carry;

    public GameEngine(HighScoreTable? table = null)
    {
        HighScores = table ?? HighScoreTable.Load();
        NewGame(1);
        State = GameState.Title;
    }

    public event Action<Sfx>? Sound;

    public GameState State { get; private set; } = GameState.Title;
    public Phase Phase { get; private set; } = Phase.Start;
    public double StateTime { get; private set; }
    public double PhaseTime { get; private set; }
    public HighScoreTable HighScores { get; }
    public InitialsEntry? Initials { get; private set; }
    public int NewEntryIndex { get; private set; } = -1;
    public bool Silent { get; set; }
    public World World { get; private set; } = new();
    public Pilot Pilot { get; } = new();
    public int Level { get; private set; } = 1;
    public int Score { get; private set; }
    /// <summary>Seconds of the hour used.</summary>
    public double Clock { get; private set; }
    public double TimeLeft => Math.Max(0, GameSeconds - Clock);
    public bool Won { get; private set; }
    public string? Message { get; private set; }
    public double MessageTime { get; private set; }

    private void Play(Sfx s)
    {
        if (!Silent) Sound?.Invoke(s);
    }

    public void Update(double dt, Controls c, bool start)
    {
        _accumulator += Math.Min(dt, 0.25);
        while (_accumulator >= Field.Step)
        {
            _accumulator -= Field.Step;
            Tick(c, start);
        }
    }

    public void Tick(Controls c, bool start = false)
    {
        double dt = Field.Step;
        StateTime += dt;
        bool startPressed = start && !_startPrev;
        _startPrev = start;
        if (MessageTime > 0) { MessageTime -= dt; if (MessageTime <= 0) Message = null; }
        switch (State)
        {
            case GameState.Title:
                if (_tunePending) { _tunePending = false; Play(Sfx.TitleTune); }
                _idle += dt;
                if (startPressed || c.Shift && StateTime > 0.5) StartGame();
                else if (_idle > TitleIdleSeconds) StartAttract();
                break;
            case GameState.Attract:
                Simulate(dt, Pilot.Decide(this));
                if (startPressed) { StartGame(); break; }
                if (State == GameState.Attract && StateTime > AttractSeconds) { Music(false); ShowHighScores(-1); }
                break;
            case GameState.Playing:
                Simulate(dt, c);
                break;
            case GameState.GameOver:
                if (StateTime > OverSeconds)
                {
                    if (HighScores.Qualifies(Score))
                    {
                        State = GameState.EnterInitials;
                        StateTime = 0;
                        Initials = new InitialsEntry(HighScores.LastInitials);
                    }
                    else ShowHighScores(-1);
                }
                break;
            case GameState.EnterInitials:
                if (Initials is { IsDone: true } done)
                {
                    int place = HighScores.Insert(done.Initials, Score, Level);
                    HighScores.Save();
                    ShowHighScores(place);
                }
                break;
            case GameState.HighScores:
                if (startPressed) StartGame();
                else if (StateTime > HighScoreSeconds) ReturnToTitle();
                break;
        }
    }

    private void NewGame(int level)
    {
        Score = 0;
        Clock = 0;
        Won = false;
        _carry = null;
        Load(level);
    }

    /// <summary>A level from its start; the prince brings his sword and strength from the last one.</summary>
    private void Load(int level)
    {
        Level = level;
        World = new World();
        Sim.Start(World, PalaceBook.Level(level), _carry);
        SetPhase(Phase.Start);
        int minutes = (int)Math.Ceiling(TimeLeft / 60);
        Message = $"LEVEL {level}   {minutes} MINUTES LEFT";
        MessageTime = StartSeconds;
        Pilot.Reset();
    }

    public void StartGame(int level = 1)
    {
        NewGame(level);
        State = GameState.Playing;
        StateTime = 0;
        NewEntryIndex = -1;
    }

    private void StartAttract()
    {
        NewGame(1 + _attractLevel++ % 3);
        if (Level > 1) _carry = new Body { Hp = Sim.Hp0, MaxHp = Sim.Hp0, Sword = true };
        Load(Level);
        State = GameState.Attract;
        StateTime = 0;
    }

    public void ReturnToTitle()
    {
        State = GameState.Title;
        StateTime = 0;
        _idle = 0;
        _tunePending = true;
        Music(false);
        Message = null;
        NewGame(1);
    }

    private void ShowHighScores(int place)
    {
        State = GameState.HighScores;
        StateTime = 0;
        NewEntryIndex = place;
    }

    public void Pause()
    {
        if (State != GameState.Playing) return;
        State = GameState.Paused;
        Music(false);
    }

    public void Resume()
    {
        if (State != GameState.Paused) return;
        State = GameState.Playing;
        if (Phase == Phase.Play) Music(true);
    }

    private void Music(bool on)
    {
        if (on == _music) return;
        _music = on;
        Play(on ? Sfx.Music : Sfx.MusicStop);
    }

    private void SetPhase(Phase p)
    {
        Phase = p;
        PhaseTime = 0;
    }

    private void Simulate(double dt, Controls c)
    {
        PhaseTime += dt;
        var w = World;
        if (Phase != Phase.Start || Level > 1) Clock += dt;
        if (TimeLeft <= 0 && State == GameState.Playing)
        {
            Message = "TIME HAS RUN OUT";
            MessageTime = 4;
            End();
            return;
        }
        switch (Phase)
        {
            case Phase.Start:
                if (PhaseTime > StartSeconds) { SetPhase(Phase.Play); Music(true); }
                break;
            case Phase.Play:
            {
                int guards = w.Guards.Count(g => g.Alive);
                Sim.Step(w, c);
                Score += (guards - w.Guards.Count(g => g.Alive)) * GuardPoints;
                if (w.Done)
                {
                    SetPhase(Phase.Done);
                    Music(false);
                    Score += LevelPoints;
                }
                else if (!w.Prince.Alive) { SetPhase(Phase.Dying); Music(false); }
                break;
            }
            case Phase.Dying:
                if (PhaseTime > DyingSeconds)
                {
                    if (State == GameState.Attract) { ShowHighScores(-1); return; }
                    Load(Level);
                    return;
                }
                break;
            case Phase.Done:
                if (PhaseTime > DoneSeconds)
                {
                    if (State == GameState.Attract) { ShowHighScores(-1); return; }
                    _carry = w.Prince;
                    if (Level >= PalaceBook.Count)
                    {
                        Won = true;
                        Score += (int)(TimeLeft / 60) * MinutePoints;
                        Play(Sfx.Victory);
                        State = GameState.GameOver;
                        StateTime = 0;
                        return;
                    }
                    Load(Level + 1);
                    return;
                }
                break;
        }
        foreach (var s in w.Heard) Play(s);
        w.Heard.Clear();
    }

    private void End()
    {
        Play(Sfx.GameOver);
        Music(false);
        if (State == GameState.Attract) { ShowHighScores(-1); return; }
        State = GameState.GameOver;
        StateTime = 0;
    }

    // ───────────────────────────── for tests ─────────────────────────────

    public void Run(int steps, Controls c = default)
    {
        for (int i = 0; i < steps; i++) Tick(c);
    }

    public void Idle(int steps) => Run(steps);

    /// <summary>Straight into a level, past the opening moment (with the sword, after level 1).</summary>
    public void Go(int level = 1)
    {
        StartGame(level);
        if (level > 1) { _carry = new Body { Hp = Sim.Hp0, MaxHp = Sim.Hp0, Sword = true }; Load(level); }
        SetPhase(Phase.Play);
        Message = null;
    }

    public void SetClock(double seconds) => Clock = seconds;
}
