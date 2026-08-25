using System;
using System.Collections.Generic;
using ManagedDoom;
using ManagedDoom.UserInput;

namespace DoomInDynamo.Engine
{
    /// <summary>
    /// IUserInput backend driven by WPF keyboard/mouse events instead of a global
    /// hook. DoomPlayerView calls <see cref="KeyDown"/>/<see cref="KeyUp"/> from its
    /// own PreviewKeyDown/PreviewKeyUp handlers (only while it has keyboard focus)
    /// and <see cref="AddMouseDelta"/> from PreviewMouseMove (only while
    /// <see cref="IsMouseGrabbed"/>). <see cref="BuildTicCmd"/> is called once per
    /// game tic to turn all of that into movement, mirroring
    /// ManagedDoom.Silk.SilkUserInput.BuildTicCmd.
    /// </summary>
    public sealed class WpfUserInput : IUserInput
    {
        private readonly Config config;
        private readonly HashSet<DoomKey> pressed = new HashSet<DoomKey>();
        private readonly bool[] weaponKeys = new bool[7];
        private int turnHeld;

        private double mouseDeltaX;
        private double mouseDeltaY;

        public WpfUserInput(Config config)
        {
            this.config = config;
        }

        public bool IsMouseGrabbed { get; private set; }

        /// <summary>Raised when ManagedDoom decides the mouse should be grabbed
        /// (in a level, menu closed) or released (menu open, not in a level) - see
        /// Doom.CheckMouseState(). DoomPlayerView hides/warps the cursor in response.</summary>
        public event Action MouseGrabChanged;

        public void KeyDown(DoomKey key)
        {
            pressed.Add(key);
        }

        public void KeyUp(DoomKey key)
        {
            pressed.Remove(key);
        }

        public void ReleaseAll()
        {
            pressed.Clear();
        }

        /// <summary>Accumulates a mouse-move delta (in pixels, control-relative)
        /// to be consumed by the next <see cref="BuildTicCmd"/> call.</summary>
        public void AddMouseDelta(double dx, double dy)
        {
            mouseDeltaX += dx;
            mouseDeltaY += dy;
        }

        public void BuildTicCmd(TicCmd cmd)
        {
            var keyForward = IsPressed(config.key_forward);
            var keyBackward = IsPressed(config.key_backward);
            var keyStrafeLeft = IsPressed(config.key_strafeleft);
            var keyStrafeRight = IsPressed(config.key_straferight);
            var keyTurnLeft = IsPressed(config.key_turnleft);
            var keyTurnRight = IsPressed(config.key_turnright);
            var keyFire = IsPressed(config.key_fire);
            var keyUse = IsPressed(config.key_use);
            var keyRun = IsPressed(config.key_run);
            var keyStrafe = IsPressed(config.key_strafe);

            weaponKeys[0] = pressed.Contains(DoomKey.Num1);
            weaponKeys[1] = pressed.Contains(DoomKey.Num2);
            weaponKeys[2] = pressed.Contains(DoomKey.Num3);
            weaponKeys[3] = pressed.Contains(DoomKey.Num4);
            weaponKeys[4] = pressed.Contains(DoomKey.Num5);
            weaponKeys[5] = pressed.Contains(DoomKey.Num6);
            weaponKeys[6] = pressed.Contains(DoomKey.Num7);

            cmd.Clear();

            var strafe = keyStrafe;
            var speed = keyRun ? 1 : 0;
            var forward = 0;
            var side = 0;

            if (config.game_alwaysrun)
            {
                speed = 1 - speed;
            }

            if (keyTurnLeft || keyTurnRight)
            {
                turnHeld++;
            }
            else
            {
                turnHeld = 0;
            }

            int turnSpeed;
            if (turnHeld < PlayerBehavior.SlowTurnTics)
            {
                turnSpeed = 2;
            }
            else
            {
                turnSpeed = speed;
            }

            if (strafe)
            {
                if (keyTurnRight)
                {
                    side += PlayerBehavior.SideMove[speed];
                }
                if (keyTurnLeft)
                {
                    side -= PlayerBehavior.SideMove[speed];
                }
            }
            else
            {
                if (keyTurnRight)
                {
                    cmd.AngleTurn -= (short)PlayerBehavior.AngleTurn[turnSpeed];
                }
                if (keyTurnLeft)
                {
                    cmd.AngleTurn += (short)PlayerBehavior.AngleTurn[turnSpeed];
                }
            }

            if (keyForward)
            {
                forward += PlayerBehavior.ForwardMove[speed];
            }
            if (keyBackward)
            {
                forward -= PlayerBehavior.ForwardMove[speed];
            }

            if (keyStrafeLeft)
            {
                side -= PlayerBehavior.SideMove[speed];
            }
            if (keyStrafeRight)
            {
                side += PlayerBehavior.SideMove[speed];
            }

            if (keyFire)
            {
                cmd.Buttons |= TicCmdButtons.Attack;
            }

            if (keyUse)
            {
                cmd.Buttons |= TicCmdButtons.Use;
            }

            for (var i = 0; i < weaponKeys.Length; i++)
            {
                if (weaponKeys[i])
                {
                    cmd.Buttons |= TicCmdButtons.Change;
                    cmd.Buttons |= (byte)(i << TicCmdButtons.WeaponShift);
                    break;
                }
            }

            // Mouse: horizontal delta turns (or strafes, while the strafe key is
            // held) exactly like SilkUserInput. Vertical delta is deliberately NOT
            // wired to anything - vanilla Doom's engine has no true vertical camera
            // pitch at all (fixed-height software rasterizer), and mapping mouse-Y to
            // forward/backward walking (what the original game actually did with it)
            // is a confusing legacy behavior most players don't expect. See README.
            var ms = 0.5 * config.mouse_sensitivity;
            var mx = (int)Math.Round(ms * mouseDeltaX);
            mouseDeltaX = 0;
            mouseDeltaY = 0;

            if (strafe)
            {
                side += mx * 2;
            }
            else
            {
                cmd.AngleTurn -= (short)(mx * 0x8);
            }

            if (forward > PlayerBehavior.MaxMove)
            {
                forward = PlayerBehavior.MaxMove;
            }
            else if (forward < -PlayerBehavior.MaxMove)
            {
                forward = -PlayerBehavior.MaxMove;
            }
            if (side > PlayerBehavior.MaxMove)
            {
                side = PlayerBehavior.MaxMove;
            }
            else if (side < -PlayerBehavior.MaxMove)
            {
                side = -PlayerBehavior.MaxMove;
            }

            cmd.ForwardMove += (sbyte)forward;
            cmd.SideMove += (sbyte)side;
        }

        private bool IsPressed(KeyBinding keyBinding)
        {
            foreach (var key in keyBinding.Keys)
            {
                if (pressed.Contains(key))
                {
                    return true;
                }
            }

            return false;
        }

        public void Reset()
        {
            mouseDeltaX = 0;
            mouseDeltaY = 0;
        }

        public void GrabMouse()
        {
            if (!IsMouseGrabbed)
            {
                IsMouseGrabbed = true;
                mouseDeltaX = 0;
                mouseDeltaY = 0;
                MouseGrabChanged?.Invoke();
            }
        }

        public void ReleaseMouse()
        {
            if (IsMouseGrabbed)
            {
                IsMouseGrabbed = false;
                MouseGrabChanged?.Invoke();
            }
        }

        public int MaxMouseSensitivity => 15;

        public int MouseSensitivity
        {
            get => config.mouse_sensitivity;
            set => config.mouse_sensitivity = value;
        }
    }
}
