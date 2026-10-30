"""Offline HTTP smoke test for navigate_scene against a local catalog stub.

Build AI_WebApi first, then run: python tools/verify_scene_command_resolver.py
Uses an installed compatible .NET runtime; never reads deployment credentials.
"""

import json
import os
from pathlib import Path
import socket
import subprocess
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen


ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "tmp" / "scene-http-smoke"
TOKEN = "scene-local-smoke-token"
ROWS = [
    [{"id": 5, "name": "康复大厅"}],
    [],
    [
        {"id": 13, "name": "打开历史对话"},
        {"id": 22, "name": "关闭地图"},
        {"id": 31, "name": "关闭训练记录面板"},
        {"id": 32, "name": "关闭训练详情面板"},
    ],
    [],
]


class CatalogStub(BaseHTTPRequestHandler):
    fail = False

    def log_message(self, *_):
        pass

    def do_POST(self):
        payload = json.loads(self.rfile.read(int(self.headers["Content-Length"])))
        assert self.headers["apikey"] == "smoke-key"
        assert self.headers["apitoken"] == "smoke-token"
        if self.path == "/version":
            assert payload["spaceId"] == "smoke-space"
            body, status = {"data": 321}, 200
        else:
            sql = payload["sql"]
            assert "version_id = 321" in sql
            assert "elem.ordinality - 1" in sql
            category = next(i for i, name in enumerate(
                ["TeleportConfig", "ScenePointCofig", "CommandList", "FastTaskConfig"]
            ) if name in sql)
            body, status = ({"error": "test failure"}, 503) if self.fail and category == 3 else (
                {"data": {"result": ROWS[category]}}, 200
            )
        encoded = json.dumps(body).encode()
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(encoded)))
        self.end_headers()
        self.wfile.write(encoded)


def request(base, path, payload=None, authorized=True):
    headers = {"Content-Type": "application/json"}
    if authorized:
        headers["Authorization"] = f"Bearer {TOKEN}"
    data = json.dumps(payload).encode() if payload is not None else None
    req = Request(base + path, data=data, headers=headers)
    try:
        with urlopen(req, timeout=5) as response:
            return response.status, json.load(response)
    except HTTPError as error:
        return error.code, json.load(error)


def main():
    OUTPUT.mkdir(parents=True, exist_ok=True)
    dll = ROOT / "AI_WebApi/bin/Debug/net6.0/AI_WebApi.dll"
    assert dll.is_file(), "Build AI_WebApi/AI_WebApi.csproj first"
    upstream = ThreadingHTTPServer(("127.0.0.1", 0), CatalogStub)
    thread = threading.Thread(target=upstream.serve_forever, daemon=True)
    thread.start()
    with socket.socket() as port_probe:
        port_probe.bind(("127.0.0.1", 0))
        port = port_probe.getsockname()[1]
    base = f"http://127.0.0.1:{port}"
    upstream_url = f"http://127.0.0.1:{upstream.server_port}"
    env = os.environ.copy()
    env.update({
        "ASPNETCORE_ENVIRONMENT": "Production",
        "IReGoTools__DifyServiceBearerToken": TOKEN,
        "MainDB": "unused-smoke-database",
        "RehabTools__DefaultSpaceId": "smoke-space",
        "RehabTools__Scene__VersionUrl": upstream_url + "/version",
        "RehabTools__Scene__QueryUrl": upstream_url + "/query",
        "RehabTools__Scene__ApiKey": "smoke-key",
        "RehabTools__Scene__ApiToken": "smoke-token",
        "RehabTools__Scene__Resolver__MinimumScore": "0.75",
        "RehabTools__Scene__Resolver__MinimumMargin": "0.05",
        "Logging__LogLevel__Default": "Warning",
    })
    process = None
    try:
        with (OUTPUT / "host.log").open("w", encoding="utf-8") as log:
            # The empty content root isolates the host from appsettings and deployment files.
            process = subprocess.Popen(
                ["dotnet", "--roll-forward", "LatestMajor", str(dll),
                 "--urls", base, "--contentRoot", str(OUTPUT)],
                cwd=OUTPUT, env=env, stdout=log, stderr=subprocess.STDOUT,
                creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0),
            )
            deadline = time.monotonic() + 20
            while True:
                assert process.poll() is None, f"Host exited; see {OUTPUT / 'host.log'}"
                try:
                    status, schema = request(base, "/api/ai/rehab/tools/openapi.json")
                    assert status == 200
                    break
                except URLError:
                    if time.monotonic() > deadline:
                        raise
                    time.sleep(0.1)

            path = "/api/ai/rehab/tools/navigate_scene"
            statuses = []
            for text, expected in [
                ("打开历史对话，然后关闭地图，再去康复大厅", ["matched"] * 3),
                ("帮我把历史对话打开地图关掉", ["matched"] * 2),
                ("打开历史对话，关闭训练面板，然后去康复大厅", ["matched", "ambiguous", "matched"]),
                ("打开历史对话，关闭天气面板，然后去康复大厅", ["matched", "unmatched", "matched"]),
            ]:
                status, body = request(base, path, {"target": text, "spaceId": "smoke-space"})
                assert status == 200, body
                data = body["data"]
                assert data["spaceId"] == "smoke-space" and data["version"] == 321
                assert data["result"] == "" and data["intentType"] == "unknown"
                commands = data["commands"]
                assert [c["status"] for c in commands] == expected, body
                assert [c["order"] for c in commands] == list(range(1, len(commands) + 1))
                for command in commands:
                    if command["status"] != "matched":
                        assert command["result"] is None and command["score"] is None
                        assert all("result" not in c and "id" not in c for c in command["candidates"])
                statuses.append(expected)

            assert request(base, path, {"target": "打开历史对话"}, authorized=False)[0] == 401
            assert request(base, path, {"target": " "})[0] == 400
            assert request(base, path, {})[0] == 400
            assert request(base, path, {"target": "打开历史对话", "expectedSceneVersion": -1})[0] == 400
            status, body = request(base, path, {"target": "打开历史对话", "expectedSceneVersion": 322})
            assert status == 409 and body["status"] == 409 and body["data"] is None, body
            for expected_version in [None, 321]:
                payload = {"target": "打开历史对话"}
                if expected_version is not None:
                    payload["expectedSceneVersion"] = expected_version
                status, body = request(base, path, payload)
                assert status == 200 and body["data"]["result"] == "[ClickPoint:]13", body
                assert body["data"]["commands"][0]["result"] == body["data"]["result"]
            CatalogStub.fail = True
            status, body = request(base, path, {"target": "打开历史对话"})
            assert status == 502 and body["data"] is None, body
            schemas = schema["components"]["schemas"]
            assert "commands" in schemas["SceneOperationToolResponseDto"]["properties"]
            assert "result" in schemas["SceneOperationToolResponseDto"]["properties"]
            assert "sourceText" in schemas["SceneCommandResultDto"]["properties"]
            request_schema = schemas["SceneOperationToolRequestDto"]
            assert "expectedSceneVersion" in request_schema["properties"]
            assert "expectedSceneVersion" not in request_schema.get("required", [])
            assert "409" in schema["paths"][path]["post"]["responses"]
            (OUTPUT / "result.json").write_text(json.dumps({
                "cases": statuses, "httpStatuses": [200, 400, 401, 409, 502], "openapi": "passed"
            }, ensure_ascii=False, indent=2), encoding="utf-8")
            print("HTTP smoke passed: ordered commands, legacy single result, 400/401/409/502, and OpenAPI.")
    finally:
        if process is not None and process.poll() is None:
            process.terminate()
            try:
                process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=5)
        upstream.shutdown()
        upstream.server_close()
        thread.join(timeout=5)


if __name__ == "__main__":
    main()
