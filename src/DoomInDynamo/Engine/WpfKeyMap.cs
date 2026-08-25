using ManagedDoom;
using WpfKey = System.Windows.Input.Key;

namespace DoomInDynamo.Engine
{
    /// <summary>
    /// Maps WPF's <see cref="WpfKey"/> to ManagedDoom's <see cref="DoomKey"/>.
    /// Equivalent to ManagedDoom.Silk.SilkUserInput's Silk-to-DoomKey table, ported
    /// to WPF's key enum so input can come from a UserControl's own KeyDown/KeyUp
    /// events rather than a global keyboard hook.
    /// </summary>
    internal static class WpfKeyMap
    {
        public static DoomKey ToDoomKey(WpfKey key)
        {
            switch (key)
            {
                case WpfKey.Space: return DoomKey.Space;
                case WpfKey.OemComma: return DoomKey.Comma;
                case WpfKey.OemMinus: return DoomKey.Subtract;
                case WpfKey.OemPeriod: return DoomKey.Period;
                case WpfKey.OemQuestion: return DoomKey.Slash;
                case WpfKey.D0: return DoomKey.Num0;
                case WpfKey.D1: return DoomKey.Num1;
                case WpfKey.D2: return DoomKey.Num2;
                case WpfKey.D3: return DoomKey.Num3;
                case WpfKey.D4: return DoomKey.Num4;
                case WpfKey.D5: return DoomKey.Num5;
                case WpfKey.D6: return DoomKey.Num6;
                case WpfKey.D7: return DoomKey.Num7;
                case WpfKey.D8: return DoomKey.Num8;
                case WpfKey.D9: return DoomKey.Num9;
                case WpfKey.OemSemicolon: return DoomKey.Semicolon;
                case WpfKey.OemPlus: return DoomKey.Equal;
                case WpfKey.A: return DoomKey.A;
                case WpfKey.B: return DoomKey.B;
                case WpfKey.C: return DoomKey.C;
                case WpfKey.D: return DoomKey.D;
                case WpfKey.E: return DoomKey.E;
                case WpfKey.F: return DoomKey.F;
                case WpfKey.G: return DoomKey.G;
                case WpfKey.H: return DoomKey.H;
                case WpfKey.I: return DoomKey.I;
                case WpfKey.J: return DoomKey.J;
                case WpfKey.K: return DoomKey.K;
                case WpfKey.L: return DoomKey.L;
                case WpfKey.M: return DoomKey.M;
                case WpfKey.N: return DoomKey.N;
                case WpfKey.O: return DoomKey.O;
                case WpfKey.P: return DoomKey.P;
                case WpfKey.Q: return DoomKey.Q;
                case WpfKey.R: return DoomKey.R;
                case WpfKey.S: return DoomKey.S;
                case WpfKey.T: return DoomKey.T;
                case WpfKey.U: return DoomKey.U;
                case WpfKey.V: return DoomKey.V;
                case WpfKey.W: return DoomKey.W;
                case WpfKey.X: return DoomKey.X;
                case WpfKey.Y: return DoomKey.Y;
                case WpfKey.Z: return DoomKey.Z;
                case WpfKey.OemOpenBrackets: return DoomKey.LBracket;
                case WpfKey.OemBackslash: return DoomKey.Backslash;
                case WpfKey.OemCloseBrackets: return DoomKey.RBracket;
                case WpfKey.Escape: return DoomKey.Escape;
                case WpfKey.Enter: return DoomKey.Enter;
                case WpfKey.Tab: return DoomKey.Tab;
                case WpfKey.Back: return DoomKey.Backspace;
                case WpfKey.Insert: return DoomKey.Insert;
                case WpfKey.Delete: return DoomKey.Delete;
                case WpfKey.Right: return DoomKey.Right;
                case WpfKey.Left: return DoomKey.Left;
                case WpfKey.Down: return DoomKey.Down;
                case WpfKey.Up: return DoomKey.Up;
                case WpfKey.PageUp: return DoomKey.PageUp;
                case WpfKey.PageDown: return DoomKey.PageDown;
                case WpfKey.Home: return DoomKey.Home;
                case WpfKey.End: return DoomKey.End;
                case WpfKey.Pause: return DoomKey.Pause;
                case WpfKey.F1: return DoomKey.F1;
                case WpfKey.F2: return DoomKey.F2;
                case WpfKey.F3: return DoomKey.F3;
                case WpfKey.F4: return DoomKey.F4;
                case WpfKey.F5: return DoomKey.F5;
                case WpfKey.F6: return DoomKey.F6;
                case WpfKey.F7: return DoomKey.F7;
                case WpfKey.F8: return DoomKey.F8;
                case WpfKey.F9: return DoomKey.F9;
                case WpfKey.F10: return DoomKey.F10;
                case WpfKey.F11: return DoomKey.F11;
                case WpfKey.F12: return DoomKey.F12;
                case WpfKey.NumPad0: return DoomKey.Numpad0;
                case WpfKey.NumPad1: return DoomKey.Numpad1;
                case WpfKey.NumPad2: return DoomKey.Numpad2;
                case WpfKey.NumPad3: return DoomKey.Numpad3;
                case WpfKey.NumPad4: return DoomKey.Numpad4;
                case WpfKey.NumPad5: return DoomKey.Numpad5;
                case WpfKey.NumPad6: return DoomKey.Numpad6;
                case WpfKey.NumPad7: return DoomKey.Numpad7;
                case WpfKey.NumPad8: return DoomKey.Numpad8;
                case WpfKey.NumPad9: return DoomKey.Numpad9;
                case WpfKey.Divide: return DoomKey.Divide;
                case WpfKey.Multiply: return DoomKey.Multiply;
                case WpfKey.Subtract: return DoomKey.Subtract;
                case WpfKey.Add: return DoomKey.Add;
                case WpfKey.LeftShift: return DoomKey.LShift;
                case WpfKey.LeftCtrl: return DoomKey.LControl;
                case WpfKey.LeftAlt: return DoomKey.LAlt;
                case WpfKey.RightShift: return DoomKey.RShift;
                case WpfKey.RightCtrl: return DoomKey.RControl;
                case WpfKey.RightAlt: return DoomKey.RAlt;
                default: return DoomKey.Unknown;
            }
        }

        /// <summary>
        /// True for keys the node should swallow (PreviewKeyDown e.Handled = true) so
        /// Dynamo's canvas-level shortcuts (search focus, pan, delete, ...) don't fire
        /// while the player is aiming those same keys at Doom.
        /// </summary>
        public static bool IsGameplayKey(WpfKey key)
        {
            return ToDoomKey(key) != DoomKey.Unknown;
        }
    }
}
