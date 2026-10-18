using AVAPrince.Engine;

namespace AVAPrince.Tests;

/// <summary>How the computer prince gets on, level by level (set PRINCE_PROBE to run it; PRINCE_PROBE=lazy runs and jumps).</summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_prince_does()
    {
        if (Environment.GetEnvironmentVariable("PRINCE_PROBE") is not { } mode) return;
        Body? carry = null;
        double total = 0;
        for (int n = 1; n <= PalaceBook.Count; n++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var def = PalaceBook.Level(n);
            var deaths = new List<string>();
            World w = new();
            int t = 0;
            for (int life = 0; life < 6; life++)
            {
                w = new World();
                Sim.Start(w, def, carry);
                var p = new Pilot();
                for (t = 0; t < 60 * 300 && !w.Done && w.Prince.Alive; t++)
                {
                    var c = mode == "lazy" ? new Controls(1, t % 50 == 0, false, false) : p.Decide(w);
                    Sim.Step(w, c);
                }
                total += t / 60.0;
                if (w.Done) break;
                deaths.Add(w.Prince.Alive ? $"stuck at {w.Prince.Col},{w.Prince.Row} [{p.Doing}]" : $"{w.Cause} at {w.Prince.Col},{w.Prince.Row} [{p.Doing}]");
            }
            if (w.Done) carry = w.Prince;
            output.WriteLine($"{n} ({def.Rooms} rooms, {def.Guards.Count} guards): {(w.Done ? "THROUGH" : "not through")} in {t / 60}s, hp {w.Prince.Hp}/{w.Prince.MaxHp}, {deaths.Count} deaths: {string.Join("; ", deaths.Take(4))} {sw.ElapsedMilliseconds} ms");
            if (!w.Done) break;
        }
        output.WriteLine($"{total / 60:0.0} minutes in all");
    }
}
