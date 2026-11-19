using System;
using Grasshopper.Kernel;

namespace Lamcp.Bridge.R7
{
    /// <summary>
    /// Grasshopper component that starts/stops the loopback LAMCP bridge in Rhino 7.
    /// Inputs: enable (bool), port (int, default 8765). Output: status text.
    /// </summary>
    public sealed class LamcpBridgeComponent : GH_Component
    {
        public LamcpBridgeComponent()
            : base("LAMCP Bridge R7", "LAMCP7",
                "Loopback HTTP bridge (127.0.0.1 only) that lets the lamcp MCP server run IronPython inside this Rhino 7 instance. Never expose beyond localhost.",
                "LAMCP", "Bridge")
        {
        }

        public override Guid ComponentGuid => new Guid("a3f6c1d2-8e47-4b9a-b5c0-2d61e7f49a35");

        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddBooleanParameter("enable", "enable", "Start (True) or stop (False) the local HTTP server.", GH_ParamAccess.item, false);
            pManager.AddIntegerParameter("port", "port", "Loopback port. Defaults to 8765.", GH_ParamAccess.item, 8765);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("status", "status", "Current server status.", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            bool enable = false;
            int port = 8765;
            DA.GetData(0, ref enable);
            DA.GetData(1, ref port);

            if (!enable)
            {
                BridgeServer.Stop();
                DA.SetData(0, "stopped");
                return;
            }

            if (port < 1024 || port > 65535)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Port must be between 1024 and 65535.");
                return;
            }

            try
            {
                BridgeServer server = BridgeServer.Start(port);
                DA.SetData(0, "listening on http://127.0.0.1:" + server.Port + " (IronPython 2.7)");
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Failed to start bridge on 127.0.0.1:" + port + ": " + ex.Message);
                DA.SetData(0, "error: " + ex.Message);
            }
        }

        public override void RemovedFromDocument(GH_Document document)
        {
            BridgeServer.Stop();
            base.RemovedFromDocument(document);
        }

        public override void DocumentContextChanged(GH_Document document, GH_DocumentContext context)
        {
            if (context == GH_DocumentContext.Close || context == GH_DocumentContext.Unloaded)
            {
                BridgeServer.Stop();
            }
            base.DocumentContextChanged(document, context);
        }
    }
}
