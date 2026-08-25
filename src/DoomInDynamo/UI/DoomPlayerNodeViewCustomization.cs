using Dynamo.Controls;
using Dynamo.Wpf;
using DoomInDynamo.Nodes;

namespace DoomInDynamo.UI
{
    /// <summary>
    /// Discovered automatically by Dynamo (it scans the package assembly for
    /// INodeViewCustomization&lt;T&gt; implementations) and instantiated once per
    /// DoomPlayerNodeModel placed on the canvas.
    /// </summary>
    public class DoomPlayerNodeViewCustomization : INodeViewCustomization<DoomPlayerNodeModel>
    {
        private DoomPlayerView view;

        public void CustomizeView(DoomPlayerNodeModel model, NodeView nodeView)
        {
            view = new DoomPlayerView(model);
            nodeView.ContentGrid.Children.Add(view);
        }

        /// <summary>
        /// Called when the node is removed from the canvas or the workspace closes.
        /// Without this, DoomPlayerView's CompositionTarget.Rendering subscription
        /// (a static WPF event) would keep ticking the engine and holding the WAD
        /// file handle open forever, even after the node is gone.
        /// </summary>
        public void Dispose()
        {
            view?.Dispose();
            view = null;
        }
    }
}
