using ManagedDoom;
using ManagedDoom.Video;

namespace DoomInDynamo.Engine
{
    /// <summary>
    /// IVideo backend that renders into a plain byte buffer instead of an OpenGL
    /// texture (compare to ManagedDoom's own SilkVideo). DoomPlayerView copies
    /// <see cref="FrameBuffer"/> into a WPF WriteableBitmap after each Render call.
    /// </summary>
    public sealed class WpfVideo : IVideo
    {
        private readonly Renderer renderer;
        private readonly byte[] frameBuffer;

        public WpfVideo(Config config, GameContent content)
        {
            renderer = new Renderer(config, content);
            frameBuffer = new byte[4 * renderer.Width * renderer.Height];
        }

        public void Render(Doom doom, Fixed frameFrac)
        {
            renderer.Render(doom, frameBuffer, frameFrac);
        }

        public void InitializeWipe()
        {
            renderer.InitializeWipe();
        }

        public bool HasFocus()
        {
            return true;
        }

        /// <summary>
        /// RGBA pixels, one byte buffer per <see cref="Renderer.Render"/> call.
        /// Stored column-major (index = ScreenHeight * x + y, per DrawScreen.Data),
        /// i.e. transposed relative to a normal row-major bitmap - the consumer
        /// must transpose while copying it into a WriteableBitmap.
        /// </summary>
        public byte[] FrameBuffer => frameBuffer;

        public int ScreenWidth => renderer.Width;
        public int ScreenHeight => renderer.Height;

        public int MaxWindowSize => renderer.MaxWindowSize;

        public int WindowSize
        {
            get => renderer.WindowSize;
            set => renderer.WindowSize = value;
        }

        public bool DisplayMessage
        {
            get => renderer.DisplayMessage;
            set => renderer.DisplayMessage = value;
        }

        public int MaxGammaCorrectionLevel => renderer.MaxGammaCorrectionLevel;

        public int GammaCorrectionLevel
        {
            get => renderer.GammaCorrectionLevel;
            set => renderer.GammaCorrectionLevel = value;
        }

        public int WipeBandCount => renderer.WipeBandCount;
        public int WipeHeight => renderer.WipeHeight;
    }
}
