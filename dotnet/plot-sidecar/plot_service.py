# Copyright 2026 Google LLC
#
# Licensed under the Apache License, Version 2.0 (the "License");
# you may not use this file except in compliance with the License.
# You may obtain a copy of the License at
#
#     http://www.apache.org/licenses/LICENSE-2.0
#
# Unless required by applicable law or agreed to in writing, software
# distributed under the License is distributed on an "AS IS" BASIS,
# WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
# See the License for the specific language governing permissions and
# limitations under the License.

"""
Matplotlib plot-execution sidecar for the .NET PaperBanana port.

The "plot" task asks an LLM to emit Python matplotlib code, which has no faithful
.NET equivalent. To keep the plot pipeline fully working, the .NET
VisualizerAgent/VanillaAgent POST the generated code to this small HTTP service,
which executes it exactly like the original ``_execute_plot_code_worker`` and
returns the rendered figure as a base64-encoded JPEG.

Usage:
    python plot_service.py            # listens on 0.0.0.0:8500
    PORT=9000 python plot_service.py  # custom port

Endpoints:
    GET  /health   -> {"status": "ok"}
    POST /execute  -> body {"code": "..."} -> {"image_base64": "<b64>" | null}

Only matplotlib is required (already in the project's requirements.txt).
"""

import base64
import io
import json
import os
import re
from concurrent.futures import ProcessPoolExecutor
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer


def _execute_plot_code_worker(code_text: str):
    """Extract code, execute plotting, and return a base64 JPEG (or None).

    This mirrors the worker used by the original Python agents.
    """
    import matplotlib

    matplotlib.use("Agg")
    import matplotlib.pyplot as plt

    match = re.search(r"```python(.*?)```", code_text, re.DOTALL)
    code_clean = match.group(1).strip() if match else code_text.strip()

    plt.close("all")
    plt.rcdefaults()

    try:
        exec_globals = {}
        exec(code_clean, exec_globals)  # noqa: S102 - sandboxed sidecar by design
        if plt.get_fignums():
            buf = io.BytesIO()
            plt.savefig(buf, format="jpeg", bbox_inches="tight", dpi=300)
            plt.close("all")
            buf.seek(0)
            return base64.b64encode(buf.read()).decode("utf-8")
        return None
    except Exception as e:  # noqa: BLE001
        print(f"Error executing plot code: {e}")
        return None


# Execute in a separate process so a crashing/looping snippet cannot take down
# the service, matching the ProcessPoolExecutor design of the original agents.
_executor = ProcessPoolExecutor(max_workers=int(os.environ.get("PLOT_WORKERS", "4")))


class Handler(BaseHTTPRequestHandler):
    def _send(self, code: int, payload: dict):
        body = json.dumps(payload).encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):  # noqa: N802
        if self.path == "/health":
            self._send(200, {"status": "ok"})
        else:
            self._send(404, {"error": "not found"})

    def do_POST(self):  # noqa: N802
        if self.path != "/execute":
            self._send(404, {"error": "not found"})
            return

        length = int(self.headers.get("Content-Length", "0"))
        raw = self.rfile.read(length).decode("utf-8") if length else "{}"
        try:
            code = json.loads(raw).get("code", "")
        except json.JSONDecodeError:
            self._send(400, {"error": "invalid JSON"})
            return

        try:
            image_b64 = _executor.submit(_execute_plot_code_worker, code).result(timeout=120)
        except Exception as e:  # noqa: BLE001
            print(f"Execution failed: {e}")
            image_b64 = None

        self._send(200, {"image_base64": image_b64})

    def log_message(self, *_args):
        # Quieter default logging.
        pass


def main():
    host = os.environ.get("HOST", "0.0.0.0")
    port = int(os.environ.get("PORT", "8500"))
    server = ThreadingHTTPServer((host, port), Handler)
    print(f"PaperBanana plot sidecar listening on http://{host}:{port}")
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        server.shutdown()
        _executor.shutdown(wait=False)


if __name__ == "__main__":
    main()
