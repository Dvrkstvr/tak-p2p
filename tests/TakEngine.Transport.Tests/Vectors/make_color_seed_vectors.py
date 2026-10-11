"""Regenerates color-seed-vectors.json (F-017, D-038) with an implementation independent of the C# ColorSeed and ColorResolver:
seed_commit = sha256(b"tak/seed/v1\n" + host_nonce), seed = sha256(b"tak/seed/v1\n" + host_nonce + guest_nonce) (raw 32-byte nonces),
colours = ColorResolver: sha256(UTF-8(seed_hex + ":" + lower(min(H, G)) + ":" + lower(max(H, G)))), first byte even -> the
ordinally smaller key is White.
Run from the repo root: python3 tests/TakEngine.Transport.Tests/Vectors/make_color_seed_vectors.py"""
import hashlib
import json
import os

HERE = os.path.dirname(os.path.abspath(__file__))
TAG = b"tak/seed/v1\n"


def h(label):
    return hashlib.sha256(label.encode()).digest()


def colours(seed_hex, host, guest):
    first, second = sorted([host, guest])
    digest = hashlib.sha256(f"{seed_hex}:{first}:{second}".encode()).digest()
    first_white = digest[0] % 2 == 0
    white = first if first_white else second
    return ("White" if white == host else "Black", "White" if white == guest else "Black")


host = "79be667ef9dcbbac55a06295ce870b07029bfcdb2dce28d959f2815b16f81798"
guest = "c6047f9441ed7d6d3045406e95c07cd85c778e4b8cef3ca7abac09b95c709ee5"
cases = []
for i in range(6):
    hn = h(f"host nonce {i}")
    gn = h(f"guest nonce {i}")
    seed = hashlib.sha256(TAG + hn + gn).hexdigest()
    hc, gc = colours(seed, host, guest)
    cases.append({
        "host_nonce": hn.hex(),
        "guest_nonce": gn.hex(),
        "seed_commit": hashlib.sha256(TAG + hn).hexdigest(),
        "seed": seed,
        "host_colour": hc,
        "guest_colour": gc,
    })

with open(os.path.join(HERE, "color-seed-vectors.json"), "w", newline="\n") as f:
    json.dump({"host": host, "guest": guest, "cases": cases}, f, indent=2)
    f.write("\n")
