"""Independent verifier: BIP-340 reference implementation (bitcoin/bips reference.py) + NIP-01 id via hashlib/json.
Verifies events signed by the C# spike, and writes events signed by the Python reference for the C# runner to verify."""
import json, hashlib, sys, os
sys.path.insert(0, os.path.join(os.path.dirname(__file__), "vectors"))
from bip340_reference import schnorr_verify, schnorr_sign, pubkey_gen

def nip01_id(e):
    # NIP-01: compact JSON, UTF-8, only the mandatory escapes (json.dumps with ensure_ascii=False does exactly that)
    ser = json.dumps([0, e["pubkey"], e["created_at"], e["kind"], e["tags"], e["content"]], separators=(",", ":"), ensure_ascii=False)
    return hashlib.sha256(ser.encode("utf-8")).hexdigest()

here = os.path.dirname(__file__)
ok = bad = 0
for line in open(os.path.join(here, "out", "events.jsonl"), encoding="utf-8"):
    e = json.loads(line)
    id_ok = nip01_id(e) == e["id"]
    sig_ok = schnorr_verify(bytes.fromhex(e["id"]), bytes.fromhex(e["pubkey"]), bytes.fromhex(e["sig"]))
    print(f'  C#-signed event content={e["content"]!r:.50}: id_matches_python={id_ok} sig_valid_python={sig_ok}')
    ok += id_ok and sig_ok; bad += not (id_ok and sig_ok)
print(f"C# -> Python: {ok} ok, {bad} bad")

# Python -> C#
sk = bytes.fromhex("c90fdaa22168c234c4c6628b80dc1cd129024e088a67cc74020bbea63b14e5c9")
pk = pubkey_gen(sk)
out = []
for i, content in enumerate(["from python", "a1>+ <b2 \"q\" \n", "\U0001F984 表 \b\f"]):
    e = {"pubkey": pk.hex(), "created_at": 1700000100 + i, "kind": 1, "tags": [["t", "tak"]], "content": content}
    e["id"] = nip01_id(e)
    e["sig"] = schnorr_sign(bytes.fromhex(e["id"]), sk, os.urandom(32)).hex()
    out.append(json.dumps(e, separators=(",", ":"), ensure_ascii=False))
open(os.path.join(here, "out", "py-events.jsonl"), "w", encoding="utf-8").write("\n".join(out) + "\n")
print("wrote out/py-events.jsonl")
sys.exit(1 if bad else 0)
