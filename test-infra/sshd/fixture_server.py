"""Disposable OpenSSH fixture. All state, credentials and processes are container-owned."""

import base64
from concurrent.futures import ThreadPoolExecutor
import hashlib
import http.server
import json
import os
from pathlib import Path
import secrets
import signal
import socket
import socketserver
import subprocess
import threading
import time


OUTPUT = Path("/fixture-output")
STATE = Path("/run/tunneldeck")
SERVERS = {"allow": 2222, "deny": 2223, "restricted": 2224, "mutable": 2225}
PROCESSES = {}
RESERVATIONS = {}
EXTRA_ECHOS = {}
LOCK = threading.Lock()
CONTROL_TOKEN = secrets.token_urlsafe(32)


class EchoHandler(socketserver.BaseRequestHandler):
    def handle(self):
        self.request.settimeout(5)
        try:
            while data := self.request.recv(65536):
                self.request.sendall(data)
        except (OSError, TimeoutError):
            pass


class EchoServer(socketserver.ThreadingTCPServer):
    daemon_threads = True
    request_queue_size = 32


class EchoServerV6(EchoServer):
    address_family = socket.AF_INET6


class ControlServer(http.server.ThreadingHTTPServer):
    request_queue_size = 32


def run_echo(server_type, address):
    server = server_type((address, 0), EchoHandler)
    threading.Thread(target=server.serve_forever, daemon=True).start()
    return server


def key_fingerprint(path):
    key_blob = base64.b64decode(path.read_text().split()[1])
    return "SHA256:" + base64.b64encode(hashlib.sha256(key_blob).digest()).decode().rstrip("=")


def descendant_processes(parent):
    """Only descendants of this fixture-owned daemon; never signal another server."""
    parents = {}
    for path in Path("/proc").iterdir():
        if not path.name.isdigit():
            continue
        try:
            fields = (path / "stat").read_text().rpartition(")")[2].split()
            parents[int(path.name)] = int(fields[1])
        except (OSError, ValueError, IndexError):
            continue
    descendants = []
    pending = [parent]
    while pending:
        current = pending.pop()
        children = [pid for pid, owner in parents.items() if owner == current]
        descendants.extend(children)
        pending.extend(children)
    return descendants


def stop_sshd(name, disconnect_clients=False):
    old = PROCESSES.get(name)
    if old and old.poll() is None:
        if disconnect_clients:
            # A plain sshd listener restart preserves established sessions. The explicit
            # service-stop fault also terminates its clients, like a cgroup-wide shutdown.
            for pid in reversed(descendant_processes(old.pid)):
                try:
                    os.kill(pid, signal.SIGCONT)
                    os.kill(pid, signal.SIGTERM)
                except ProcessLookupError:
                    pass
        os.killpg(old.pid, signal.SIGCONT)
        os.killpg(old.pid, signal.SIGTERM)
        old.wait(timeout=5)


def start_sshd(name, alternate=False, disconnect_clients=False):
    stop_sshd(name, disconnect_clients)
    host_key = "host_alternate" if alternate else "host_primary"
    config = STATE / f"{name}.conf"
    policy = "AllowTcpForwarding no" if name == "deny" else "AllowTcpForwarding yes"
    if name == "restricted":
        policy += "\nPermitListen 127.0.0.1:19001"
    config.write_text(f"""Port {SERVERS[name]}
ListenAddress 0.0.0.0
HostKey {STATE / host_key}
PidFile {STATE / (name + '.pid')}
AuthorizedKeysFile /home/tunneltest/.ssh/authorized_keys
PasswordAuthentication yes
KbdInteractiveAuthentication no
PubkeyAuthentication yes
PermitRootLogin no
AllowUsers tunneltest
UsePAM no
StrictModes yes
GatewayPorts no
PermitUserEnvironment no
PrintMotd no
LogLevel VERBOSE
ClientAliveInterval 2
ClientAliveCountMax 2
{policy}
""")
    subprocess.run(["/usr/sbin/sshd", "-t", "-f", str(config)], check=True)
    PROCESSES[name] = subprocess.Popen(
        ["/usr/sbin/sshd", "-D", "-e", "-f", str(config)],
        stdout=subprocess.DEVNULL,
        stderr=(OUTPUT / f"sshd-{name}.log").open("ab"),
        start_new_session=True,
    )
    deadline = time.monotonic() + 5
    while time.monotonic() < deadline:
        try:
            with socket.create_connection(("127.0.0.1", SERVERS[name]), timeout=0.2):
                return
        except OSError:
            time.sleep(0.05)
    raise RuntimeError(f"sshd {name} did not become ready")


class ControlHandler(http.server.BaseHTTPRequestHandler):
    # Every response has Content-Length. Keep the administrative connection alive instead
    # of rapidly opening/closing hundreds of WSL/Docker loopback relay connections.
    protocol_version = "HTTP/1.1"

    def log_message(self, *_args):
        pass

    def do_GET(self):
        if self.path != "/health":
            self.send_error(404)
            return
        self.reply({"ready": all(p.poll() is None for p in PROCESSES.values())})

    def do_POST(self):
        if self.headers.get("Authorization") != f"Bearer {CONTROL_TOKEN}":
            self.send_error(403)
            return
        try:
            length = int(self.headers.get("Content-Length", 0))
            if length < 0 or length > 4_000_000:
                raise ValueError("Fixture request exceeds the size limit")
            body = json.loads(self.rfile.read(length))
            if self.path == "/probe":
                payload = base64.b64decode(body["payload"], validate=True)
                expected = int(body.get("expectedBytes", len(payload)))
                if expected < 0 or expected > 4_000_000:
                    raise ValueError("Fixture response exceeds the size limit")
                idle_seconds = float(body.get("idleSeconds", 0))
                if not 0 <= idle_seconds <= 8:
                    raise ValueError("Fixture idle period exceeds the limit")
                actual = bytearray()
                outcome = "completed"
                try:
                    with socket.create_connection((body.get("host", "127.0.0.1"), int(body["port"])), timeout=5) as stream:
                        # Explicit liveness test: keep the real forwarded TCP channel open across
                        # several sshd ClientAliveInterval periods before sending the payload.
                        if idle_seconds:
                            time.sleep(idle_seconds)
                        with ThreadPoolExecutor(max_workers=1) as executor:
                            sending = executor.submit(stream.sendall, payload)
                            try:
                                while len(actual) < expected:
                                    part = stream.recv(expected - len(actual))
                                    if not part:
                                        outcome = "eof"
                                        break
                                    actual.extend(part)
                            finally:
                                sending.result(timeout=5)
                except (TimeoutError, ConnectionResetError, BrokenPipeError) as error:
                    if not body.get("observeFailure"):
                        raise
                    outcome = "timeout" if isinstance(error, TimeoutError) else "reset"
                self.reply({"payload": base64.b64encode(actual).decode(), "outcome": outcome})
            elif self.path == "/start-echo":
                echo = EchoServer(("127.0.0.1", int(body.get("port", 0))), EchoHandler)
                port = echo.server_address[1]
                with LOCK:
                    EXTRA_ECHOS[port] = echo
                threading.Thread(target=echo.serve_forever, daemon=True).start()
                self.reply({"port": port})
            elif self.path == "/stop-echo":
                with LOCK:
                    echo = EXTRA_ECHOS.pop(int(body["port"]), None)
                if echo:
                    echo.shutdown()
                    echo.server_close()
                self.reply({"stopped": echo is not None})
            elif self.path == "/listening":
                try:
                    with socket.create_connection((body.get("host", "127.0.0.1"), int(body["port"])), timeout=0.2):
                        listening = True
                except OSError:
                    listening = False
                self.reply({"listening": listening})
            elif self.path == "/reserve":
                family = socket.AF_INET6 if body.get("ipv6") else socket.AF_INET
                address = "::1" if family == socket.AF_INET6 else "127.0.0.1"
                stream = socket.socket(family, socket.SOCK_STREAM)
                stream.bind((address, int(body.get("port", 0))))
                stream.listen(4)
                port = stream.getsockname()[1]
                with LOCK:
                    RESERVATIONS[port] = stream
                self.reply({"port": port})
            elif self.path == "/release":
                with LOCK:
                    stream = RESERVATIONS.pop(int(body["port"]), None)
                if stream:
                    stream.close()
                self.reply({"released": stream is not None})
            elif self.path == "/restart":
                name = body["server"]
                if name not in SERVERS:
                    raise ValueError("Unknown fixture server")
                start_sshd(name, bool(body.get("alternateKey")), bool(body.get("disconnectClients")))
                self.reply({"restarted": name})
            elif self.path == "/stop":
                name = body["server"]
                if name not in SERVERS:
                    raise ValueError("Unknown fixture server")
                stop_sshd(name, disconnect_clients=True)
                self.reply({"stopped": name})
            elif self.path == "/logs":
                name = body["server"]
                if name not in SERVERS:
                    raise ValueError("Unknown fixture server")
                self.reply({"log": (OUTPUT / f"sshd-{name}.log").read_text(errors="replace")[-32000:]})
            else:
                self.send_error(404)
        except (ValueError, KeyError, OSError, TimeoutError, subprocess.SubprocessError) as error:
            self.reply({"error": type(error).__name__, "message": str(error)}, status=400)

    def reply(self, body, status=200):
        payload = json.dumps(body).encode()
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(payload)))
        self.end_headers()
        self.wfile.write(payload)


def main():
    STATE.mkdir(parents=True, exist_ok=True)
    OUTPUT.mkdir(parents=True, exist_ok=True)
    password = secrets.token_urlsafe(32)
    passphrase = secrets.token_urlsafe(32)
    subprocess.run(["chpasswd"], input=f"tunneltest:{password}\n", text=True, check=True)
    for name, secret in (("host_primary", ""), ("host_alternate", ""), ("identity", passphrase), ("identity_plain", "")):
        subprocess.run(["ssh-keygen", "-q", "-t", "ed25519", "-a", "16", "-N", secret, "-f", str(STATE / name)], check=True)
    ssh_dir = Path("/home/tunneltest/.ssh")
    ssh_dir.mkdir(mode=0o700, exist_ok=True)
    authorized_keys = ssh_dir / "authorized_keys"
    authorized_keys.write_text((STATE / "identity.pub").read_text() + (STATE / "identity_plain.pub").read_text())
    authorized_keys.chmod(0o600)
    subprocess.run(["chown", "-R", "tunneltest:tunneltest", str(ssh_dir)], check=True)
    (OUTPUT / "identity").write_bytes((STATE / "identity").read_bytes())
    (OUTPUT / "identity").chmod(0o600)
    (OUTPUT / "identity_plain").write_bytes((STATE / "identity_plain").read_bytes())
    (OUTPUT / "identity_plain").chmod(0o600)

    echo = run_echo(EchoServer, "127.0.0.1")
    echo6 = run_echo(EchoServerV6, "::1")
    for name in SERVERS:
        start_sshd(name)
    manifest = {
        "schemaVersion": 1,
        "username": "tunneltest",
        "password": password,
        "privateKeyPassphrase": passphrase,
        "hostKeyFingerprint": key_fingerprint(STATE / "host_primary.pub"),
        "alternateHostKeyFingerprint": key_fingerprint(STATE / "host_alternate.pub"),
        "controlToken": CONTROL_TOKEN,
        "echoPort": echo.server_address[1],
        "echoV6Port": echo6.server_address[1],
        "permitListenPort": 19001,
        "sshdVersion": subprocess.run(["dpkg-query", "-W", "-f=${Version}", "openssh-server"], capture_output=True, text=True, check=True).stdout,
    }
    temporary = OUTPUT / "container-manifest.tmp"
    temporary.write_text(json.dumps(manifest, indent=2))
    temporary.chmod(0o600)
    temporary.replace(OUTPUT / "container-manifest.json")
    ControlServer(("0.0.0.0", 8080), ControlHandler).serve_forever()


if __name__ == "__main__":
    main()
