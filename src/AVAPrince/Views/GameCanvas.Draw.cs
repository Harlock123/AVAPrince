using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVAPrince.Engine;

namespace AVAPrince.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVAPrince/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2F2FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF8A9AC8, Cyan = 0xFF6AE8FF, Orange = 0xFFFF9A30;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);


    private bool InGame => Engine.State is GameState.Attract or GameState.Playing or GameState.Paused;

    private const double CW = Field.ColWidth, RH = Field.RowHeight, L = Field.Left, TOP = Field.Top, Slab = 22;

    /// <summary>The stone's colours: the dungeon's blue-grey, then the palace's sandstone.</summary>
    private static (uint Back, uint Block, uint Edge, uint Floor) Stone(int level) => level % 2 == 1
        ? (0xFF1A2232, 0xFF4A5A78, 0xFF2A3448, 0xFF6A7A98)
        : (0xFF2A1E14, 0xFF9A7A50, 0xFF5A4428, 0xFFC0A070);

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawBack(dc, 1); DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawBack(dc, 2); DrawInitials(dc); break;
            case GameState.GameOver: DrawGameOver(dc); break;
            default:
                DrawRoom(dc, g.World);
                DrawStrip(dc, g.World);
                if (g.Message is { } msg && g.Phase is Phase.Start)
                {
                    dc.FillRectangle(Brush(0xC0000000), new Rect(0, 280, Field.Width, 70));
                    Text(dc, msg, new Point(512, 315), 48, 0xFFF0E0B0, center: true);
                }
                break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0xD0000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(512, 300), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(512, 360), 26, White, center: true);
        }
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(512, 14), 22, Yellow, center: true);
    }

    // ───────────────────────────── the room ─────────────────────────────

    /// <summary>The back wall: rows of blocks, and torches here and there.</summary>
    private void DrawBack(DrawingContext dc, int level, int room = 0)
    {
        var st = Stone(level);
        dc.FillRectangle(Brush(st.Back), new Rect(0, 0, Field.Width, Field.Height));
        for (int y = 0; y < 22; y++)
            for (int x = 0; x < 18; x++)
            {
                double bx = x * 64 - (y % 2) * 32, by = TOP + y * 30;
                if (by > Field.PlayBottom) continue;
                uint h = Hash(x + room * 18, y, level);
                dc.FillRectangle(Brush(h % 4 == 0 ? Blend(st.Back, st.Edge, 0.5) : Blend(st.Back, st.Edge, 0.25)), new Rect(bx + 1, by + 1, 62, 28));
            }
        for (int k = 0; k < 3; k++)
        {
            uint h = Hash(k, room, level + 7);
            if (h % 3 == 0) continue;
            double tx = 150 + k * 340 + h % 80, ty = TOP + 70 + (h / 5 % 3) * RH;
            DrawTorch(dc, tx, ty, k + room);
        }
    }

    private static uint Blend(uint a, uint b, double t)
    {
        uint Ch(int shift) => (uint)Math.Round(((a >> shift) & 0xFF) * (1 - t) + ((b >> shift) & 0xFF) * t);
        return 0xFF000000 | Ch(16) << 16 | Ch(8) << 8 | Ch(0);
    }

    private void DrawTorch(DrawingContext dc, double x, double y, int salt)
    {
        dc.FillRectangle(Brush(0xFF4A3420), new Rect(x - 4, y, 8, 26), 2);
        dc.FillRectangle(Brush(0xFF2A1A10), new Rect(x - 8, y - 2, 16, 6), 2);
        double f = Math.Sin(_clock * 13 + salt) * 3, h = 22 + Math.Sin(_clock * 9 + salt * 2) * 4;
        dc.FillEllipse(x, y - 12, 26, 0x18FFB040);
        Poly(dc, 0xFFFF7020, null, 0, new Point(x - 8, y - 2), new Point(x + f, y - 2 - h), new Point(x + 8, y - 2));
        Poly(dc, 0xFFFFE070, null, 0, new Point(x - 4, y - 2), new Point(x + f * 0.5, y - 2 - h * 0.55), new Point(x + 4, y - 2));
    }

    private static double CellX(int col, int room) => L + (col - room * Field.RoomCols) * CW;
    private static double FloorY(int row) => TOP + (row + 1) * RH - Slab;

    private void DrawRoom(DrawingContext dc, World w)
    {
        int room = w.Room;
        var st = Stone(w.Def.Number);
        DrawBack(dc, w.Def.Number, room);
        int c0 = room * Field.RoomCols;
        // The door out, set into the back wall.
        for (int c = c0; c < c0 + Field.RoomCols; c++)
            for (int r = 0; r < Field.Rows; r++)
                if (w.At(c, r) == Cell.Exit) DrawExit(dc, CellX(c, room), FloorY(r), w.ExitOpen);
        for (int c = c0 - 1; c <= c0 + Field.RoomCols; c++)
            for (int r = 0; r < Field.Rows; r++)
                DrawCell(dc, w, c, r, room, st);
        foreach (var gd in w.Guards)
            if (gd.Col / Field.RoomCols == room || gd.ToCol / Field.RoomCols == room) DrawBody(dc, gd, room, guard: true, w.Def.Number);
        DrawBody(dc, w.Prince, room, guard: false, w.Def.Number);
        // Gates and choppers stand in front.
        for (int c = c0; c < c0 + Field.RoomCols; c++)
            for (int r = 0; r < Field.Rows; r++)
            {
                if (w.At(c, r) == Cell.Gate) DrawGate(dc, CellX(c, room), FloorY(r), w.GateOpen[c, r]);
                if (w.At(c, r) == Cell.Chopper) DrawChopper(dc, CellX(c, room), FloorY(r), Sim.ChopperShutAt(w, c), (w.Clock + c * 0.37) % Sim.ChopperCycle);
            }
    }

    private void DrawCell(DrawingContext dc, World w, int c, int r, int room, (uint Back, uint Block, uint Edge, uint Floor) st)
    {
        var cell = w.At(c, r);
        double x = CellX(c, room), fy = FloorY(r), top = TOP + r * RH;
        if (x < -CW || x > Field.Width) return;
        if (cell == Cell.Wall)
        {
            dc.FillRectangle(Brush(st.Block), new Rect(x, top, CW, RH));
            for (int k = 0; k < 5; k++)
            {
                double by = top + k * 40;
                dc.DrawLine(Pen(st.Edge, 3), new Point(x, by), new Point(x + CW, by));
                double off = (k + c) % 2 == 0 ? 30 : 70;
                dc.DrawLine(Pen(st.Edge, 3), new Point(x + off, by), new Point(x + off, by + 40));
            }
            return;
        }
        if (cell == Cell.Empty) return;
        // The floor slab (shaking, if it's about to go).
        double jig = cell == Cell.Loose && w.Shaking[Math.Clamp(c, 0, w.Cols - 1), r] > 0 ? Math.Sin(_clock * 60) * 3 : 0;
        dc.FillRectangle(Brush(st.Floor), new Rect(x, fy + jig, CW, 8));
        dc.FillRectangle(Brush(st.Block), new Rect(x, fy + 8 + jig, CW, Slab - 8));
        dc.DrawLine(Pen(st.Edge, 2), new Point(x + CW, fy + jig), new Point(x + CW, fy + Slab + jig));
        if (cell == Cell.Loose) dc.DrawLine(Pen(st.Edge, 2), new Point(x + 10, fy + 4 + jig), new Point(x + CW - 10, fy + 4 + jig));
        switch (cell)
        {
            case Cell.Spikes:
                for (int k = 0; k < 5; k++)
                {
                    double sx = x + 12 + k * 19;
                    Poly(dc, 0xFFD0D0D8, 0xFF707080, 1, new Point(sx - 6, fy), new Point(sx, fy - 30), new Point(sx + 6, fy));
                }
                break;
            case Cell.Plate or Cell.ExitPlate:
            {
                var p = w.Prince;
                bool pressed = p.Col == c && p.Row == r;
                dc.FillRectangle(Brush(cell == Cell.ExitPlate ? 0xFFE0C060 : Blend(st.Floor, 0xFFFFFFFF, 0.3)), new Rect(x + 18, fy - (pressed ? 1 : 6), CW - 36, pressed ? 2 : 7), 2);
                break;
            }
            case Cell.Potion or Cell.BigPotion:
            {
                uint liquid = cell == Cell.BigPotion ? 0xFF40E060 : 0xFFE03040;
                double size = cell == Cell.BigPotion ? 1.4 : 1;
                dc.DrawEllipse(Brush(0x80C0C0D0), Pen(0xFFD0D0E0, 1.5), new Point(x + CW / 2, fy - 10 * size), 9 * size, 10 * size);
                dc.DrawEllipse(Brush(liquid), null, new Point(x + CW / 2, fy - 8 * size), 7 * size, 7 * size);
                dc.FillRectangle(Brush(0xFFD0D0E0), new Rect(x + CW / 2 - 3, fy - 26 * size, 6, 8 * size));
                double b = (_clock * 30 + c * 7) % 16;
                dc.FillEllipse(x + CW / 2 + Math.Sin(_clock * 5 + c) * 3, fy - 8 * size - b, 2, Alpha(liquid, 1 - b / 16));
                break;
            }
            case Cell.Sword:
                dc.DrawLine(Pen(0xFFE0E0F0, 3), new Point(x + 20, fy - 4), new Point(x + 76, fy - 4));
                dc.DrawLine(Pen(0xFFE0C040, 4), new Point(x + 20, fy - 10), new Point(x + 20, fy + 2));
                dc.DrawLine(Pen(0xFF6A4020, 5), new Point(x + 8, fy - 4), new Point(x + 18, fy - 4));
                if ((int)(_clock * 2) % 3 == 0) dc.FillEllipse(x + 60, fy - 5, 3, 0xFFFFFFFF);
                break;
            case Cell.Debris:
                for (int k = 0; k < 6; k++) dc.FillRectangle(Brush(st.Edge), new Rect(x + 10 + Hash(c, k, 3) % 70, fy - 6 - Hash(c, k, 4) % 6, 10, 6), 2);
                break;
        }
    }

    private void DrawExit(DrawingContext dc, double x, double floor, bool open)
    {
        double w = 150, h = 150, x0 = x + CW / 2 - w / 2;
        dc.FillRectangle(Brush(0xFF3A2A1A), new Rect(x0 - 10, floor - h - 14, w + 20, h + 14), 6);
        dc.FillRectangle(Brush(0xFF0A0604), new Rect(x0, floor - h, w, h));
        for (int k = 0; k < 4; k++) dc.FillRectangle(Brush(0xFF2A2018), new Rect(x0 + 10 + k * 8, floor - 18 - k * 16, w - 20 - k * 16, 14));
        if (!open)
        {
            dc.FillRectangle(Brush(0xFF7A5A30), new Rect(x0 + 4, floor - h + 4, w / 2 - 6, h - 4));
            dc.FillRectangle(Brush(0xFF7A5A30), new Rect(x0 + w / 2 + 2, floor - h + 4, w / 2 - 6, h - 4));
            dc.FillEllipse(x0 + w / 2 - 12, floor - h / 2, 4, 0xFFE0C040);
            dc.FillEllipse(x0 + w / 2 + 12, floor - h / 2, 4, 0xFFE0C040);
        }
        else dc.FillEllipse(x0 + w / 2, floor - 60, 50, 0x20FFE0A0);
    }

    private static void DrawGate(DrawingContext dc, double x, double floor, double open)
    {
        // A portcullis: raised while it's open, coming down as the time runs out.
        double lift = open > 1 ? 1 : Math.Max(0, open);
        double top = floor - RH + Slab + 4, bottom = floor - lift * (RH - Slab - 30);
        double gx = x + CW - 16;
        for (int k = 0; k < 3; k++) dc.DrawLine(Pen(0xFF303438, 5), new Point(gx - 4 + k * 6, top), new Point(gx - 4 + k * 6, bottom));
        for (double y = top + 10; y < bottom; y += 26) dc.DrawLine(Pen(0xFF303438, 4), new Point(gx - 8, y), new Point(gx + 12, y));
    }

    private static void DrawChopper(DrawingContext dc, double x, double floor, bool shut, double phase)
    {
        double gx = x + CW - 22, top = floor - RH + Slab;
        double gap = shut ? 0 : Math.Min(1, phase * 3) * 60;
        dc.FillRectangle(Brush(0xFF2A2A30), new Rect(gx - 6, top, 12, 8));
        Poly(dc, 0xFFC8D0D8, 0xFF606870, 1.5, new Point(gx - 18, top + 8), new Point(gx + 18, top + 8), new Point(gx + 18, floor - 100 - gap), new Point(gx - 18, floor - 90 - gap));
        Poly(dc, 0xFFC8D0D8, 0xFF606870, 1.5, new Point(gx - 18, floor), new Point(gx + 18, floor), new Point(gx + 18, floor - 90 + gap * 0.4), new Point(gx - 18, floor - 80 + gap * 0.4));
        if (shut) dc.DrawLine(Pen(0xFFA02020, 3), new Point(gx - 18, floor - 92), new Point(gx + 18, floor - 92));
    }

    /// <summary>The prince (white) or a guard (in colour): where the act has him, in its pose.</summary>
    private void DrawBody(DrawingContext dc, Body b, int room, bool guard, int level)
    {
        double k = b.Dur > 0 ? Math.Clamp(b.T / b.Dur, 0, 1) : 1;
        double x = CellX(b.FromCol, room) + CW / 2 + (b.ToCol - b.FromCol) * CW * k;
        double y = FloorY(b.FromRow) + (b.ToRow - b.FromRow) * RH * k;
        switch (b.Act)
        {
            case Act.StandJump or Act.RunJump: y -= Math.Sin(Math.PI * k) * (b.Act == Act.RunJump ? 60 : 45); break;
            case Act.JumpUp: y -= Math.Sin(Math.PI * k) * 60; break;
            case Act.Hang: x = CellX(b.Col, room) + CW / 2 + b.Facing * 30; y = FloorY(b.Row - 1) + 125; break;
            case Act.Climb:
            {
                double sx = CellX(b.FromCol, room) + CW / 2 + b.Facing * 30, sy = FloorY(b.FromRow - 1) + 125;
                x = sx + (CellX(b.ToCol, room) + CW / 2 - sx) * Math.Max(0, k * 2 - 1);
                y = sy + (FloorY(b.ToRow) - sy) * Math.Min(1, k * 2);
                break;
            }
            case Act.ClimbDown:
                x = CellX(b.FromCol, room) + CW / 2 + (b.ToCol - b.FromCol) * CW * Math.Min(1, k * 2);
                y = FloorY(b.FromRow) + (b.ToRow - b.FromRow) * RH * Math.Max(0, k * 2 - 1);
                break;
            case Act.Fall: y = FloorY(b.FromRow) + (b.ToRow - b.FromRow) * RH * k * k; break;
            case Act.Enter: if (k > 0.6) return; break;
        }
        if (x < -60 || x > Field.Width + 60) return;
        uint skin = 0xFFE8B888, cloth = guard ? (level % 2 == 1 ? 0xFF3A60C0 : 0xFFC03030) : 0xFFF0F0F0, dark = guard ? 0xFF202020 : 0xFFC0C0C8;
        uint hair = guard ? 0xFFE0C040 : 0xFF3A2010;
        int f = b.Facing;
        var limb = Pen(cloth, 9);
        if (b.Act == Act.Dead)
        {
            dc.FillRectangle(Brush(cloth), new Rect(x - 50, y - 16, 90, 16), 6);
            dc.FillEllipse(x + 52 * f, y - 10, 10, skin);
            return;
        }
        bool crouch = b.Act is Act.Land or Act.Crouch or Act.Drink or Act.Pick;
        bool air = b.Act is Act.StandJump or Act.RunJump or Act.Fall or Act.JumpUp;
        bool hang = b.Act is Act.Hang || b.Act == Act.Climb && k < 0.5;
        double hip = y - (crouch ? 34 : 56), shoulder = hip - 44, head = shoulder - 16;
        // Legs.
        if (hang)
        {
            dc.DrawLine(limb, new Point(x - 4, hip), new Point(x - 6, hip + 50));
            dc.DrawLine(limb, new Point(x + 4, hip), new Point(x + 6, hip + 50));
        }
        else if (crouch)
        {
            dc.DrawLine(limb, new Point(x, hip), new Point(x + 18 * f, hip + 14));
            dc.DrawLine(limb, new Point(x + 18 * f, hip + 14), new Point(x + 8 * f, y));
            dc.DrawLine(limb, new Point(x, hip), new Point(x - 10 * f, y));
        }
        else
        {
            double stride = b.Act is Act.Run ? Math.Sin(k * Math.PI * 2) * 22 : b.Act is Act.Step or Act.Advance or Act.Retreat ? Math.Sin(k * Math.PI) * 14 : air ? 18 : 0;
            double knee = air ? 16 : 6;
            dc.DrawLine(limb, new Point(x, hip), new Point(x + stride * f * 0.6 + knee * f, hip + 28));
            dc.DrawLine(limb, new Point(x + stride * f * 0.6 + knee * f, hip + 28), new Point(x + stride * f, y - 2));
            dc.DrawLine(limb, new Point(x, hip), new Point(x - stride * f * 0.6, hip + 28));
            dc.DrawLine(limb, new Point(x - stride * f * 0.6, hip + 28), new Point(x - stride * f, y - 2));
        }
        // Body and head.
        dc.DrawLine(Pen(cloth, 16), new Point(x, hip), new Point(x + (b.Act == Act.Run ? 6 * f : 0), shoulder));
        dc.DrawLine(Pen(dark, 4), new Point(x - 8, hip - 2), new Point(x + 8, hip - 2));
        dc.FillEllipse(x + 2 * f, head, 11, skin);
        if (guard)
        {
            // A turban.
            dc.DrawEllipse(Brush(cloth), Pen(dark, 1.5), new Point(x, head - 9), 13, 8);
            dc.FillEllipse(x, head - 16, 4, hair);
        }
        else dc.DrawEllipse(Brush(hair), null, new Point(x - 2 * f, head - 6), 11, 7);
        dc.FillEllipse(x + 6 * f, head - 1, 1.8, 0xFF000000);
        // Arms (and a sword in a fight).
        bool fighting = b.Act is Act.Draw or Act.Advance or Act.Retreat or Act.Strike or Act.Parry or Act.Hurt || guard;
        var arm = Pen(skin, 6);
        if (hang || b.Act == Act.JumpUp || b.Act == Act.Fall)
        {
            dc.DrawLine(arm, new Point(x - 6, shoulder), new Point(x - 4 + (hang ? 4 * f : 0), shoulder - 40));
            dc.DrawLine(arm, new Point(x + 6, shoulder), new Point(x + 4 + (hang ? 4 * f : 0), shoulder - 40));
        }
        else if (fighting && (b.Sword || guard))
        {
            double reach = b.Act == Act.Strike ? Math.Sin(Math.PI * Math.Min(1, k * 1.4)) : 0;
            var hand = b.Act == Act.Parry ? new Point(x + 14 * f, shoulder - 24) : new Point(x + (18 + 22 * reach) * f, shoulder + 10);
            dc.DrawLine(arm, new Point(x, shoulder + 4), hand);
            var tip = b.Act == Act.Parry ? new Point(hand.X + 10 * f, hand.Y - 46) : new Point(hand.X + 62 * f, hand.Y - 8 + reach * 6);
            dc.DrawLine(Pen(0xFFE8E8F0, 3), hand, tip);
            dc.DrawLine(Pen(0xFFE0C040, 4), new Point(hand.X - 2 * f, hand.Y - 8), new Point(hand.X - 2 * f, hand.Y + 8));
        }
        else if (b.Act == Act.Drink)
            dc.DrawLine(arm, new Point(x, shoulder + 4), new Point(x + 8 * f, head + 4));
        else
        {
            double swing = b.Act == Act.Run ? -Math.Sin(k * Math.PI * 2) * 20 : 0;
            dc.DrawLine(arm, new Point(x, shoulder + 4), new Point(x + swing * f * 0.7, shoulder + 40));
        }
        if (b.Act == Act.Hurt) dc.FillEllipse(x, shoulder, 30, 0x40FF2020);
    }

    // ───────────────────────────── the strip along the bottom ─────────────────────────────

    private void DrawStrip(DrawingContext dc, World w)
    {
        var g = Engine;
        double top = Field.PlayBottom + 6;
        dc.FillRectangle(Brush(0xFF000000), new Rect(0, top, Field.Width, Field.Height - top));
        var p = w.Prince;
        for (int i = 0; i < p.MaxHp; i++)
        {
            double x = 26 + i * 30;
            bool full = i < p.Hp;
            bool blink = p.Hp == 1 && i == 0 && (int)(_clock * 4) % 2 == 0;
            Poly(dc, full && !blink ? 0xFFE02020 : 0, 0xFFE02020, 2, new Point(x - 10, top + 30), new Point(x + 10, top + 30), new Point(x, top + 12));
        }
        if (Sim.Foe(w) is { } foe)
            for (int i = 0; i < foe.MaxHp; i++)
            {
                double x = Field.Width - 26 - i * 30;
                Poly(dc, i < foe.Hp ? 0xFF4070F0 : 0, 0xFF4070F0, 2, new Point(x - 10, top + 30), new Point(x + 10, top + 30), new Point(x, top + 12));
            }
        int minutes = (int)Math.Ceiling(g.TimeLeft / 60);
        Text(dc, $"LEVEL {g.Level}", new Point(330, top + 22), 30, 0xFFF0E0B0, center: true);
        Text(dc, minutes > 1 ? $"{minutes} MINUTES LEFT" : $"{(int)g.TimeLeft} SECONDS LEFT", new Point(600, top + 22), 30, minutes <= 5 && (int)(_clock * 2) % 2 == 0 ? Red : 0xFFF0E0B0, center: true);
        Text(dc, $"SCORE {g.Score}", new Point(512, top + 70), 30, Dim, center: true);
        if (p.Sword) dc.DrawLine(Pen(0xFFE8E8F0, 3), new Point(140, top + 66), new Point(200, top + 66));
        if (w.ExitOpen) Text(dc, "THE DOOR IS OPEN", new Point(830, top + 70), 24, 0xFFE0C060, center: true);
    }

    // ───────────────────────────── title, game over and scores ─────────────────────────────

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        DrawBack(dc, 2);
        var st = Stone(2);
        double floor = 640;
        dc.FillRectangle(Brush(st.Floor), new Rect(0, floor, Field.Width, 8));
        dc.FillRectangle(Brush(st.Block), new Rect(0, floor + 8, Field.Width, 120));
        // The prince running and leaping along.
        double t = _clock % 6;
        var b = new Body { Facing = 1, Act = Act.Run, Dur = 1, T = (_clock * 4) % 1 };
        double px = -60 + t * 200;
        if (t is > 2.5 and < 3.2) { b.Act = Act.RunJump; b.T = (t - 2.5) / 0.7; }
        double y0 = floor - (b.Act == Act.RunJump ? Math.Sin(Math.PI * b.T) * 60 : 0);
        DrawFigure(dc, b, px, y0);
        string title = "AVA PRINCE OF PERSIA";
        for (int i = 0; i < title.Length; i++)
        {
            double tx = cx - title.Length * 24 + 24 + i * 48, ty = 110 + Math.Sin(_clock * 2.4 + i * 0.45) * 4;
            Text(dc, title[i].ToString(), new Point(tx, ty), 92, i < 3 ? 0xFFE0C060 : 0xFFF0E0B0, center: true);
        }
        Text(dc, "AFTER JORDAN MECHNER'S 1989 GAME", new Point(cx, 176), 26, White, center: true);
        dc.FillRectangle(Brush(0xC0000000), new Rect(cx - 410, 206, 820, 160), 10);
        Text(dc, "THE VIZIER HAS SEIZED THE THRONE AND SHUT THE PRINCESS IN THE TOWER.", new Point(cx, 230), 23, White, center: true);
        Text(dc, "YOU HAVE ONE HOUR TO ESCAPE THE DUNGEONS AND CLIMB THE PALACE TO HER:", new Point(cx, 258), 23, White, center: true);
        Text(dc, "RUN, LEAP THE GAPS, CLIMB THE LEDGES, MIND THE SPIKES AND CHOPPERS,", new Point(cx, 286), 23, White, center: true);
        Text(dc, "FIND THE PLATES THAT OPEN THE GATES AND DOORS, AND FIGHT THE GUARDS.", new Point(cx, 314), 23, White, center: true);
        Text(dc, "EIGHT LEVELS. EACH DEATH STARTS THE LEVEL AGAIN, BUT THE HOUR GOES ON.", new Point(cx, 342), 21, Dim, center: true);
        if ((int)(_clock * 2) % 2 == 0) Text(dc, "PRESS ENTER TO START", new Point(cx, 420), 44, Yellow, center: true);
        dc.FillRectangle(Brush(0xD0000000), new Rect(0, 700, Field.Width, 68));
        Text(dc, "ARROWS: RUN   SHIFT+ARROW: CAREFUL STEP   UP: JUMP/CLIMB/PARRY   DOWN: CLIMB DOWN   SHIFT: STRIKE/DRINK   P: PAUSE", new Point(cx, 718), 17, 0xFFE0D0B0, center: true);
        Text(dc, "MUSIC: TUNES OF ITS OWN IN THE HIJAZ MODE", new Point(cx, 746), 18, Dim, center: true);
    }

    /// <summary>A figure drawn at a point, outside any room (for the title and the end).</summary>
    private void DrawFigure(DrawingContext dc, Body b, double x, double feet)
    {
        // Put the body in a room of its own whose first square is where it should be drawn.
        b.FromCol = b.ToCol = b.Col = 0;
        b.FromRow = b.ToRow = b.Row = 2;
        using (dc.PushTransform(Matrix.CreateTranslation(x - (L + CW / 2), feet - FloorY(2))))
            DrawBody(dc, b, 0, guard: false, 1);
    }

    private void DrawGameOver(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        DrawBack(dc, g.Won ? 2 : 1);
        dc.FillRectangle(Brush(0xC0000000), new Rect(cx - 380, 120, 760, 260), 12);
        if (g.Won)
        {
            Text(dc, "THE PRINCESS IS SAFE", new Point(cx, 190), 66, 0xFFE0C060, center: true);
            Text(dc, $"ALL EIGHT LEVELS WITH {(int)(g.TimeLeft / 60)} MINUTES TO SPARE", new Point(cx, 260), 30, White, center: true);
            // The two of them, side by side.
            DrawFigure(dc, new Body { Facing = 1 }, cx - 40, 620);
            dc.FillEllipse(cx + 40, 520, 12, 0xFFE8B888);
            Poly(dc, 0xFFE070C0, null, 0, new Point(cx + 18, 620), new Point(cx + 40, 532), new Point(cx + 62, 620));
            dc.DrawEllipse(Brush(0xFF3A2010), null, new Point(cx + 40, 510), 13, 9);
        }
        else
        {
            Text(dc, g.TimeLeft <= 0 ? "TIME HAS RUN OUT" : "GAME OVER", new Point(cx, 200), 80, (int)(_clock * 4) % 2 == 0 ? Red : Yellow, center: true);
            Text(dc, $"YOU REACHED LEVEL {g.Level}", new Point(cx, 270), 32, White, center: true);
        }
        Text(dc, $"SCORE {g.Score}", new Point(cx, 330), 40, Cyan, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xD0000000), new Rect(cx - 330, 70, 660, 640), 12);
        Text(dc, "THE BRAVEST PRINCES", new Point(cx, 120), 60, 0xFFE0C060, center: true);
        Text(dc, "RANK NAME    SCORE   LEVEL", new Point(cx, 190), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,4}", new Point(cx, 236 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 680), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xD0000000), new Rect(cx - 330, 140, 660, 460), 12);
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}

internal static class DrawingExtensions
{
    public static void FillEllipse(this DrawingContext dc, double x, double y, double r, uint colour) =>
        dc.DrawEllipse(GameCanvas.Brush(colour), null, new Point(x, y), r, r);
    public static void FillRectangle(this DrawingContext dc, IBrush brush, Rect rect, double radius) => dc.FillRectangle(brush, rect, (float)radius);
}
