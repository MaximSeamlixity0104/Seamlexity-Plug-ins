# Seamlexity Plug-ins

Plug-ins for Rhino and Grasshopper.

## LAMCP Bridge R7

This Grasshopper component lets the `lamcp` MCP server control **Rhino 7**.
It uses the same wire protocol as the Rhino 8 `Lamcp_Bridge` component, so the MCP server works with Rhino 7 unchanged.

- Runs a small HTTP server on **127.0.0.1 only** (default port `8765`).
- `GET /health` reports whether the bridge is running.
- `POST /exec` with `{"code": "...", "timeout": 30}` runs IronPython 2.7 code inside Rhino and returns `{"stdout", "stderr", "result", "error"}`.
- Each request runs in one shared Python scope, so variables persist between calls. If the code sets `_`, the response's `result` field holds `repr(_)`.
- Requests run one at a time.

### ⚠️ Security

The bridge runs **any Python code** it receives, with full access to Rhino and your files.
It only listens on the loopback address (`127.0.0.1`). Never forward or proxy the port to a network.
Turn the component off (`enable = False`) when you don't need it.

### Installation

1. Download `Lamcp.Bridge.R7.gha` from [Releases](../../releases).
2. In Windows, right-click the file → **Properties** → tick **Unblock** if it is shown.
3. Copy it to `%APPDATA%\Grasshopper\Libraries\` (or use Grasshopper: *File → Special Folders → Components Folder*).
4. Restart Rhino 7 and open Grasshopper.
5. Add **LAMCP → Bridge → LAMCP Bridge R7** to the canvas and connect a `True` toggle to `enable`.
   The `status` output should read `listening on http://127.0.0.1:8765 (IronPython 2.7)`.
6. Point the `lamcp` MCP server at `http://127.0.0.1:8765`.

### Inputs and outputs

| Name | Type | Default | Meaning |
|------|------|---------|---------|
| `enable` | bool | `False` | Starts (True) or stops (False) the server |
| `port` | int | `8765` | Loopback port, 1024–65535 |
| `status` (output) | text | — | Current server status |

### Notes for Rhino 7

- The code runs in **IronPython 2.7**, not CPython 3. Tools from the MCP server that only work in Rhino 8 (venvs, CPython script components) do not work here.
- Each `/exec` call should return quickly. Don't wait inside the call for a Grasshopper solution to finish, or Rhino's UI can stall.

### Building from source

Requirements: Rhino 7 installed, .NET SDK (targets .NET Framework 4.8).

```powershell
dotnet build Lamcp.Bridge.R7/Lamcp.Bridge.R7.csproj -c Release
```

If Rhino is not in `C:\Program Files\Rhino 7`, pass the paths:

```powershell
dotnet build Lamcp.Bridge.R7/Lamcp.Bridge.R7.csproj -c Release -p:RhinoSystemDir="D:\Rhino 7\System" -p:GrasshopperDir="D:\Rhino 7\Plug-ins\Grasshopper"
```

The output is `Lamcp.Bridge.R7/bin/Release/Lamcp.Bridge.R7.gha`.

## License

[MIT](LICENSE)
