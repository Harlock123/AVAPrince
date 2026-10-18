using Avalonia.Input;

namespace AVAPrince.Engine;

/// <summary>
/// Keyboard and gamepad boiled down to the controls: run or step, jump, climb down, and Shift.
/// </summary>
public sealed class InputHandler : IDisposable
{
    private readonly List<Key> _held = [];
    private readonly ControllerManager _pad = new(0);

    public void OnKeyDown(Key key)
    {
        _held.Remove(key);
        _held.Add(key);
    }

    public void OnKeyUp(Key key) => _held.Remove(key);
    public bool Has(Key key) => _held.Contains(key);
    private bool Any(params Key[] keys) => keys.Any(_held.Contains);

    public void Update()
    {
        _pad.Update();
    }

    /// <summary>
    /// Left and right to run (with Shift, a careful step); Up to jump (and climb, and parry); Down to crouch or climb
    /// down; Shift (or Space or Ctrl) for a careful step, to strike, to drink or pick up. On a gamepad: the stick or
    /// D-pad, A to jump, X or B for Shift.
    /// </summary>
    public Controls Player
    {
        get
        {
            var pad = _pad;
            int dx = (Any(Key.Right, Key.D, Key.NumPad6) ? 1 : 0) - (Any(Key.Left, Key.A, Key.NumPad4) ? 1 : 0);
            if (dx == 0) dx = pad.DPadLeft || pad.LeftStickX < -0.4f ? -1 : pad.DPadRight || pad.LeftStickX > 0.4f ? 1 : 0;
            bool up = Any(Key.Up, Key.W, Key.NumPad8) || pad.DPadUp || pad.LeftStickY < -0.5f || pad.ButtonA;
            bool down = Any(Key.Down, Key.S, Key.NumPad2) || pad.DPadDown || pad.LeftStickY > 0.5f;
            bool shift = Any(Key.LeftShift, Key.RightShift, Key.Space, Key.LeftCtrl, Key.RightCtrl) || pad.ButtonX || pad.ButtonB;
            return new Controls(dx, up, down, shift);
        }
    }

    /// <summary>Enter or 1.</summary>
    public bool Start => Any(Key.Enter, Key.D1, Key.NumPad1) || _pad.ButtonStart;

    public bool PadUp => _pad.DPadUp || _pad.LeftStickY < -0.5f;
    public bool PadDown => _pad.DPadDown || _pad.LeftStickY > 0.5f;
    public bool PadLeft => _pad.DPadLeft || _pad.LeftStickX < -0.5f;
    public bool PadRight => _pad.DPadRight || _pad.LeftStickX > 0.5f;
    public bool PadAccept => _pad.ButtonA || _pad.ButtonX || _pad.ButtonB;
    public bool PadDone => _pad.ButtonStart;
    public bool PadPause => _pad.ButtonStart;
    public bool PadBack => _pad.ButtonBack;

    public void Dispose() => _pad.Dispose();
}
