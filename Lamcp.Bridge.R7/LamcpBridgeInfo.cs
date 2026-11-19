using System;
using Grasshopper.Kernel;

namespace Lamcp.Bridge.R7
{
    public sealed class LamcpBridgeInfo : GH_AssemblyInfo
    {
        public override string Name => "LAMCP Bridge R7";
        public override string Description => "Loopback HTTP bridge for the lamcp MCP server (Rhino 7 / IronPython 2.7).";
        public override Guid Id => new Guid("5b7d3f0e-2a41-4c8e-9d1b-7e3a6c2f8a11");
        public override string AuthorName => "Rhino2Rvt";
        public override string AuthorContact => string.Empty;
        public override string Version => "0.1.0";
    }
}
