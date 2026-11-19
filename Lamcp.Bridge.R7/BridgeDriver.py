# LAMCP Bridge driver for Rhino 7 IronPython 2.7.
# Loaded once into a persistent PythonScript scope by LamcpBridgeComponent.
# Mirrors the Rhino 8 bridge: exec() into one shared globals dict, capture
# stdout/stderr, return repr(_) and a formatted traceback on error.
import sys
import traceback
from StringIO import StringIO

_lamcp_g = {"__name__": "__lamcp_bridge__"}


def _lamcp_exec(code):
    out = StringIO()
    err = StringIO()
    old_out, old_err = sys.stdout, sys.stderr
    result = None
    error = None
    sys.stdout, sys.stderr = out, err
    try:
        exec(compile(code, "<lamcp>", "exec"), _lamcp_g)
        result = repr(_lamcp_g.get("_"))
    except BaseException:
        error = traceback.format_exc()
    finally:
        sys.stdout, sys.stderr = old_out, old_err
    return [out.getvalue(), err.getvalue(), result, error]
