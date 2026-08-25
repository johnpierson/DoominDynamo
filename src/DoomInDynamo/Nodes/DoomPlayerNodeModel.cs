using System.Collections.Generic;
using System.Xml;
using Dynamo.Graph;
using Dynamo.Graph.Nodes;
using ProtoCore.AST.AssociativeAST;

namespace DoomInDynamo.Nodes
{
    /// <summary>
    /// The Dynamo-facing half of the node: registers its one output port and
    /// persists <see cref="WadPath"/> across save/reload. All the actual game
    /// hosting lives in the custom view (see UI/DoomPlayerNodeViewCustomization
    /// and UI/DoomPlayerView) - this class has no dependency on ManagedDoom at all,
    /// so it stays usable (if inert) even in a headless/no-UI Dynamo host.
    /// </summary>
    [NodeName("Doom Player")]
    [NodeCategory("DoomInDynamo")]
    [NodeDescription("Plays Doom on the node face. Pick a WAD file with the Browse "
        + "button, click into the screen to give it keyboard focus, then WASD/arrows "
        + "to move, Alt to fire, Space to use, Esc for the menu.")]
    [IsDesignScriptCompatible]
    public class DoomPlayerNodeModel : NodeModel
    {
        private string wadPath;

        public DoomPlayerNodeModel()
        {
            OutPorts.Add(new PortModel(PortType.Output, this,
                new PortData("status", "\"running\" once a WAD is loaded and the game loop has started, otherwise \"idle\".")));
            RegisterAllPorts();

            // Default size on first placement - big enough to actually see the
            // 320x200 screen plus the toolbar, but the user can drag-resize from
            // here (see IsResizable below).
            Width = 360;
            Height = 320;
        }

        /// <summary>Lets the user drag-resize the node so the Doom screen isn't
        /// stuck at whatever size it happened to start at.</summary>
        public override bool IsResizable => true;

        /// <summary>Path to the .wad the player picked via the node's Browse button.
        /// Nothing is bundled with this package - see the README for where to get one
        /// you're legally entitled to use.</summary>
        public string WadPath
        {
            get => wadPath;
            set
            {
                if (wadPath == value)
                {
                    return;
                }

                wadPath = value;
                OnNodeModified(true);
            }
        }

        public override IEnumerable<AssociativeNode> BuildOutputAst(List<AssociativeNode> inputAstNodes)
        {
            var status = string.IsNullOrEmpty(WadPath) ? "idle" : "running";
            return new[]
            {
                AstFactory.BuildAssignment(GetAstIdentifierForOutputIndex(0), AstFactory.BuildStringNode(status))
            };
        }

        // SerializeCore/DeserializeCore are marked obsolete in this Dynamo version -
        // the doc comment on the base member says they're now only exercised for
        // undo/redo and copy/paste, with full .dyn (JSON) persistence handled
        // separately via the public WadPath property. Kept (with the warning
        // suppressed rather than silenced by marking our own method obsolete, which
        // would be misleading) so WadPath still survives undo/redo and copy/paste.
#pragma warning disable CS0672, CS0618
        protected override void SerializeCore(XmlElement element, SaveContext context)
        {
            base.SerializeCore(element, context);
            element.SetAttribute("wadPath", WadPath ?? string.Empty);
        }

        protected override void DeserializeCore(XmlElement nodeElement, SaveContext context)
        {
            base.DeserializeCore(nodeElement, context);
            var attr = nodeElement.Attributes["wadPath"];
            if (attr != null)
            {
                wadPath = attr.Value;
            }
        }
#pragma warning restore CS0672, CS0618
    }
}
