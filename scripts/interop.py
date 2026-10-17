"""Independent SMPP 3.4 wire peer. Uses Python's standard library; never imports SMSGate codecs."""
import queue
import socket
import struct
import subprocess
import sys
import threading
import time


def frame(command, sequence, body=b"", status=0):
    return struct.pack("!IIII", 16 + len(body), command, status, sequence) + body


def read_exact(peer, count):
    data = b""
    while len(data) < count:
        block = peer.recv(count - len(data))
        if not block:
            raise EOFError("Peer closed before completing a PDU")
        data += block
    return data


def read_pdu(peer):
    length, command, status, sequence = struct.unpack("!IIII", read_exact(peer, 16))
    assert 16 <= length <= 65536, length
    return command, status, sequence, read_exact(peer, length - 16)


def cstring(data, position):
    end = data.index(0, position)
    return data[position:end], end + 1


def message_body(text, esm=0, delivery=1):
    return (b"\0\x01\x01" + b"1\0" + b"\x01\x01" + b"2\0" +
            bytes([esm, 0, 0]) + b"\0\0" + bytes([delivery, 0, 0, 0, len(text)]) + text)


def decode_message(data):
    _, pos = cstring(data, 0)
    _, pos = cstring(data, pos + 2)
    _, pos = cstring(data, pos + 2)
    esm = data[pos]
    _, pos = cstring(data, pos + 3)
    _, pos = cstring(data, pos)
    coding, length = data[pos + 2], data[pos + 4]
    pos += 5
    body = data[pos:pos + length]
    pos += length
    tlvs = {}
    while pos < len(data):
        tag, size = struct.unpack("!HH", data[pos:pos + 4])
        pos += 4
        tlvs[tag] = data[pos:pos + size]
        pos += size
    return esm, coding, body, tlvs


def start(dll, mode, port):
    process = subprocess.Popen(["dotnet", dll, mode, str(port)], stdin=subprocess.PIPE,
                               stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
    lines = queue.Queue()

    def collect():
        for line in process.stdout:
            lines.put(line.rstrip())

    threading.Thread(target=collect, daemon=True).start()
    return process, lines


def finish(process, lines):
    code = process.wait(timeout=25)
    output = []
    while not lines.empty():
        output.append(lines.get_nowait())
    assert code == 0, "\n".join(output)


def test_library_server(dll):
    with socket.socket() as reservation:
        reservation.bind(("127.0.0.1", 0))
        port = reservation.getsockname()[1]
    process, lines = start(dll, "server", port)
    try:
        until = time.monotonic() + 25
        while True:
            line = lines.get(timeout=max(0.1, until - time.monotonic()))
            if line == "READY":
                break
            assert time.monotonic() < until, line
        with socket.create_connection(("127.0.0.1", port), timeout=5) as peer:
            peer.sendall(frame(9, 7, b"system\0password\0\0\x34\x00\x00\0"))
            command, status, sequence, _ = read_pdu(peer)
            assert (command, status, sequence) == (0x80000009, 0, 7)
            peer.sendall(frame(0x15, 8))
            assert read_pdu(peer)[:3] == (0x80000015, 0, 8)
            peer.sendall(frame(4, 9, message_body(b"hello \x1be")))
            seen = set()
            for _ in range(2):
                command, status, sequence, body = read_pdu(peer)
                assert status == 0
                seen.add(command)
                if command == 0x80000004:
                    assert sequence == 9 and body == b"independent-42\0"
                else:
                    assert command == 5
                    esm, coding, text, tlvs = decode_message(body)
                    assert esm & 0x3C == 4 and coding == 0 and b"stat:DELIVRD" in text
                    assert tlvs[0x001E] == b"independent-42\0" and tlvs[0x0427] == b"\x02"
                    peer.sendall(frame(0x80000005, sequence, b"\0"))
            assert seen == {5, 0x80000004}
            peer.sendall(frame(0x1234, 10))
            assert read_pdu(peer)[:3] == (0x80000000, 3, 10)
            peer.sendall(frame(6, 11))
            assert read_pdu(peer)[:3] == (0x80000006, 0, 11)
        process.stdin.write("stop\n")
        process.stdin.flush()
        finish(process, lines)
    finally:
        if process.poll() is None:
            process.kill()
            process.wait()


def test_library_client(dll):
    with socket.socket() as listener:
        listener.bind(("127.0.0.1", 0))
        listener.listen()
        listener.settimeout(25)
        process, lines = start(dll, "client", listener.getsockname()[1])
        try:
            peer, _ = listener.accept()
            with peer:
                peer.settimeout(5)
                command, _, sequence, body = read_pdu(peer)
                assert command == 9 and body.startswith(b"system\0password\0")
                peer.sendall(frame(0x80000009, sequence, b"python\0"))
                command, _, first_sequence, body = read_pdu(peer)
                assert command == 4 and decode_message(body)[1:3] == (0, b"hello \x1be")
                peer.sendall(frame(0x80000004, first_sequence, b"\0", status=0x58))
                command, _, sequence, body = read_pdu(peer)
                assert command == 4 and sequence != first_sequence
                peer.sendall(frame(0x80000004, sequence, b"independent-42\0"))
                tlvs = struct.pack("!HH", 0x001E, 15) + b"independent-42\0" + struct.pack("!HHB", 0x0427, 1, 2)
                peer.sendall(frame(5, 90, message_body(b"", esm=4, delivery=0) + tlvs))
                assert read_pdu(peer) == (0x80000005, 0, 90, b"\0")
                command, status, sequence, _ = read_pdu(peer)
                assert command == 6 and status == 0
                # Graceful shutdown may close immediately after transmitting unbind.
            finish(process, lines)
        finally:
            if process.poll() is None:
                process.kill()
                process.wait()


if __name__ == "__main__":
    if len(sys.argv) != 2:
        raise SystemExit("Usage: python scripts/interop.py PATH_TO_InteropPeer.dll")
    test_library_server(sys.argv[1])
    test_library_client(sys.argv[1])
    print("Independent Python SMPP peer passed in both directions.")
