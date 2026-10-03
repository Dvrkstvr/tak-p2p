# M0-A Plan A1: CI Gate and Crypto (F-029, F-031, F-032, F-015) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prove the CI gate on GitHub, then replace the wrong-by-design Ed25519 signer and SHA-256 "NIP-44" with one validated secp256k1 key per player, BIP-340 signatures, a hand-written NIP-01 id serializer and real NIP-44 v2 between two independent peers, all pinned by official vectors.

**Architecture:** A new pure library `src/TakEngine.Crypto` (net10.0, no I/O, no clock, no RNG: nonces, aux randomness and new secrets are arguments) holds `SecretKey`, `PublicKey`, `Schnorr`, `Nip44`, `Nip19` and `IdentityDocument`. `TakEngine.Transport` gets `Nip01Serializer` and `NostrEvents` (sign/verify) on top of it. A new `src/TakEngine.Storage.Local` holds the CLI/desktop key file (`FileKeyStore : IKeyStore`, seam in Abstractions). The Ed25519 `CryptoSigner`, `Nip44Encryption`, `NostrEvent.ComputeId` and the tests that passed for the wrong reason are deleted; every caller is rewired so the solution builds and every remaining test passes after each task.

**Tech Stack:** .NET 10 SDK (net10.0), NBitcoin.Secp256k1 4.0.3, BouncyCastle.Cryptography 2.7.0 (ChaCha7539Engine), BCL SHA256/HMACSHA256/HKDF, xUnit 2.9.3, Stryker.NET 5.0.0, GitHub Actions + `gh` CLI, Python 3 (vector generation and the independent verifier only).

**Spec:** `pipeline/features.json` (F-029, F-031, F-032, F-015), `pipeline/scope.md` (M0, "CI and the gate", "Interaction specs"), `pipeline/architecture.md` (Modules, Wire format, What gets deleted, Data, Test strategy), `pipeline/playbook.md`, `CLAUDE.md`, `.claude/rules/{crypto,transport,tests,ci}.md`, `docs/decisions/0001`-`0004`, `0007`, `0008`, `0010`, `pipeline/spikes/R-002-R-003-secp256k1-crypto/RESULT.md` and `Crypto/NostrCrypto.cs`.

**Prototype note (for the reviewer):** every code block in this plan was compiled and run in a throwaway copy of the repo before the plan was written, task by task in this order; after each task all four check commands passed. Stryker on the final state: Crypto module 86.73 %, Transport `Nip01Serializer.cs` + `NostrEvents.cs` 89.36 %. The full official vector suite passes on NBitcoin.Secp256k1 **4.0.3**, which is why 4.0.3 is pinned (playbook rule).

## Global Constraints

- Branch: work on `m0a-crypto-fake-relay-game`. Pushes to any branch except `main` are approved (D-031). **Never push to `main`.** Never publish to public Nostr relays; this plan has no LiveRelay test.
- Before any dotnet command in a shell: `export MSBuildEnableWorkloadResolver=false` (playbook: without it the Android/iOS projects in `TakGame.sln` break the build locally).
- The four checks, all green before every commit (CI runs the same): 
  `dotnet restore TakGame.Ci.slnf` · `dotnet build TakGame.Ci.slnf --no-restore` · `dotnet format TakGame.Ci.slnf --verify-no-changes --no-restore` · `dotnet test TakGame.Ci.slnf --no-build --filter "Category!=LiveRelay"`. Any failure in any test assembly is red. Fix formatting with `dotnet format TakGame.Ci.slnf`.
- A new project goes into `TakGame.sln`, `TakGame.slnx` AND `TakGame.Ci.slnf` in the same commit (CI builds only the .slnf). Android/iOS stay out of the .slnf.
- Compiler and NuGet warnings are errors (`Directory.Build.props`). Do not add `-warnaserror`; do not suppress a warning without a comment saying why.
- Engine libraries (`TakEngine.Abstractions`, `.Crypto`, `.Core`, `.Transport`) target `net10.0` only and use no file, socket, blocking-wait or SQLite APIs (browser-wasm-safe). File I/O lives only in `TakEngine.Storage.Local`.
- Project references: Crypto -> Abstractions; Core -> Abstractions + Crypto; Transport -> Abstractions + Crypto; Storage.Local -> Abstractions + Crypto. Transport does not reference Core and Core does not reference Transport.
- Packages, exact versions: `NBitcoin.Secp256k1` 4.0.3 (pin 4.0.3 only while the full vector suite passes on it, else 4.0.1), `BouncyCastle.Cryptography` 2.7.0, `xunit` 2.9.3, `xunit.runner.visualstudio` 3.1.4, `Microsoft.NET.Test.Sdk` 17.14.1, `coverlet.collector` 6.0.4, `dotnet-stryker` 5.0.0 (already in `dotnet-tools.json`).
- Crypto is pure: no I/O, no clock, no RNG inside. Nonces, BIP-340 aux randomness and new secrets come in as arguments; shells call `RandomNumberGenerator`.
- Alias `SHA256`/`HMACSHA256` from System.Security.Cryptography wherever `NBitcoin.Secp256k1` is also imported (it defines same-named types: CS0104).
- NIP-44 conversation key = HKDF-extract over the unhashed shared x coordinate (`GetSharedPubkey`), never a hashing ECDH helper. MAC compare with `CryptographicOperations.FixedTimeEquals`. Plaintext capped at 65535 UTF-8 bytes (decision 0002 "cap plaintext at 64 KiB"; the 2-byte length prefix only).
- Reject pubkeys that are not 32 bytes or not on the curve, and secrets that are 0 or >= n, at the boundary (`PublicKey`/`SecretKey`).
- Never test by decrypting with the sender's own keys. Crypto and wire tests use two independent keypairs. Randomness in tests is seeded and the seed is visible in the test name or parameters.
- NIP-01 event ids come only from `Nip01Serializer` (hand-written), never System.Text.Json. Verify `id` and `sig` of every received event before doing anything else with it.
- Wire format (kind 3825, tags `p` + `g`, `pv`) and the hash chain formula `StateHash = SHA-256(PrevStateHash || TurnIndex || PlayerPubKey || PtnMove || TpsSnapshot)` are not changed by this plan.
- Official vectors are test resources copied verbatim from `pipeline/spikes/R-002-R-003-secp256k1-crypto/vectors/`. Spike code is copied deliberately, never referenced.
- Test-first: write the test, run it, see it red, then write the code.
- UI freeze (D-006): Blazor, Avalonia and CLI change only where deleting `CryptoSigner` breaks compilation; no UI change.
- Stored identity: `{"v":1,"nsec":"nsec1…"}`; CLI/desktop file `<data-dir>/identity.json`; browser key `tak.identity.v1`; the old browser keys `tak_p2p_privkey`/`tak_p2p_pubkey` are never read (decision 0010). A higher `v` than the build knows is an error, never rewritten. The secret never leaves the device.
- Commit messages: short and accurate, e.g. `M0 F-015: NIP-44 v2 with official vectors`; no test counts (D-013); end with a blank line and `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Never `--no-verify`. Do not write test counts, DEVLOG entries or milestone tables into docs.
- `pipeline/features.json`: only Task 1 and Task 8 edit it, and only the `evidence` field. `passes` stays `false`; the stage-7 verifier flips it.
- Writing files: create files with your file-writing tool, not shell heredocs (heredocs can mangle backslashes). Never put the characters U+2028/U+2029 literally into C# source: they are line terminators in C#.
- Applying a diff from this plan: save it with your file-writing tool to a temp file outside the repo and run `git apply --whitespace=nowarn <file>` from the repo root; if it does not apply, make exactly the shown changes by hand.

## Review Focus

These are the inputs the spec implies but does not name, most likely to bite first. Each has a test in the owning task.
1. **Relay junk.** A relay or stranger sends an event with `null` id/pubkey/sig/content/tags, a `null` tag value, non-hex fields, or an off-curve pubkey: `NostrEvents.Verify` must return `false`, never throw (Task 5: `NostrEventsTests.RelayJunk_FailsVerification_WithoutThrowing`, `NullTagValue_…`, `NonHexSignature_…`, `OffCurvePubkey_…`).
2. **Case of hex from other clients.** Uppercase pubkey or id on a received event is not NIP-01 and must fail `Verify`; `PublicKey.FromHex` accepts either case but always prints lowercase, so string comparisons downstream stay exact (Task 5: `UppercasePubkeyOrId_FailsVerification`; Task 2: `PublicKey_ParsesEitherCase_AndPrintsLowercase`, `UppercaseInput_IsAccepted`).
3. **Machine culture.** A user whose OS culture formats numbers differently must produce the same event id (`created_at`, `kind` printed with invariant culture) (Task 5: `Serialize_IsCultureInvariant`).
4. **Non-ASCII plaintext at the size limit.** The NIP-44 limit counts UTF-8 bytes, not .NET chars: 21845 three-byte characters fit, one more does not (Task 4: `PlaintextLimits_AreCountedInUtf8Bytes`).
5. **A key file the build cannot read.** Empty file, truncated JSON, `v` from a newer build, wrong-length or out-of-range nsec: a clear message naming the file and reason, and the file is never overwritten or regenerated (Task 6: `FileKeyStoreTests.BadKeyFile_GivesAClearError_AndIsNotOverwritten`, `SaveNewSecret_RefusesToOverwriteAnExistingIdentity`).

## File map

| Path | Task | Responsibility |
|---|---|---|
| `src/TakEngine.Crypto/TakEngine.Crypto.csproj` | 2 | new pure library (net10.0) |
| `src/TakEngine.Crypto/InvalidKeyException.cs` | 2 | boundary validation failure |
| `src/TakEngine.Crypto/SecretKey.cs`, `PublicKey.cs` | 2 | validated secp256k1 secret / x-only public key |
| `src/TakEngine.Crypto/Nip19.cs` | 2 | moved from `src/TakEngine.Core/Cryptography/Nip19.cs`; npub/nsec |
| `src/TakEngine.Crypto/Schnorr.cs` | 3 | BIP-340 sign/verify of 32-byte digests |
| `src/TakEngine.Crypto/Nip44.cs` | 4 | NIP-44 v2 (+ `Nip44Exception`, `Nip44Error`) |
| `src/TakEngine.Transport/Nostr/Nip01Serializer.cs` | 5 | the one NIP-01 serializer / id |
| `src/TakEngine.Transport/Nostr/NostrEvents.cs` | 5 | event sign + verify |
| `src/TakEngine.Crypto/IdentityDocument.cs` | 6 | `{"v":1,"nsec":…}` codec (+ `IdentityFormatException`) |
| `src/TakEngine.Abstractions/IKeyStore.cs` | 6 | key-store seam (+ `KeyStoreException`) |
| `src/TakEngine.Storage.Local/*` | 6 | new project: `FileKeyStore`, `IdentityBootstrap` |
| `src/TakEngine.Core/Cryptography/PayloadSignature.cs` | 7 | BIP-340 over SHA-256(payload) for the session/spectator payloads (transitional until F-033) |
| `tests/TakEngine.Crypto.Tests/*` | 2-6 | new: vectors, two-peer, key and identity tests |
| `tests/TakEngine.Storage.Tests/*` | 6 | new: key-file tests |
| Deleted | 2, 4, 5, 7 | `Core/Cryptography/CryptoSigner.cs`, `Transport/Nostr/Nip44Encryption.cs`, `NostrEvent.ComputeId`, `Core.Tests/Nip19Tests.cs` (moved), `Transport.Tests/Nip44EncryptionTests.cs`, `Transport.Tests/TransportBenchmarkTests.cs`, `CryptoTests.KeyPair_GeneratesValidEd25519_AndSignsVerifies` |

## Interfaces this plan produces (consumed by Plans A2 and A3)

```csharp
namespace TakEngine.Crypto;
public sealed class InvalidKeyException(string message, Exception? innerException = null) : FormatException;
public sealed class SecretKey {
    public const int Length = 32;
    public PublicKey PublicKey { get; }
    public static SecretKey FromBytes(ReadOnlySpan<byte> secret32);            // throws InvalidKeyException
    public static bool TryFromBytes(ReadOnlySpan<byte> secret32, [NotNullWhen(true)] out SecretKey? key);
    public static SecretKey FromHex(string hex);
    public static SecretKey FromNsec(string nsec);
    public static SecretKey Generate(Func<byte[]> random32);                    // shell passes () => RandomNumberGenerator.GetBytes(32)
    public byte[] ToBytes(); public string ToHex(); public string ToNsec();
}
public sealed class PublicKey : IEquatable<PublicKey> {
    public const int Length = 32;
    public static PublicKey FromBytes(ReadOnlySpan<byte> xOnly32);            // throws InvalidKeyException (length, off-curve)
    public static bool TryFromBytes(ReadOnlySpan<byte> xOnly32, [NotNullWhen(true)] out PublicKey? key);
    public static PublicKey FromHex(string hex);                               // either case
    public static bool TryFromHex(string? hex, [NotNullWhen(true)] out PublicKey? key);
    public static PublicKey FromNpub(string npub);
    public byte[] ToBytes(); public string ToHex() /* lowercase */; public string ToNpub();
}
public static class Schnorr {
    public const int DigestLength = 32, AuxLength = 32, SignatureLength = 64;
    public static byte[] Sign(SecretKey key, ReadOnlySpan<byte> digest32, ReadOnlySpan<byte> aux32);
    public static bool Verify(PublicKey key, ReadOnlySpan<byte> digest32, ReadOnlySpan<byte> signature64); // never throws on malformed input
}
public enum Nip44Error { UnknownVersion, InvalidPayloadSize, InvalidBase64, InvalidMac, InvalidPadding }
public sealed class Nip44Exception(Nip44Error error, string message) : CryptographicException { public Nip44Error Error { get; } }
public static class Nip44 {
    public const byte Version = 2; public const int MinPlaintextBytes = 1, MaxPlaintextBytes = 65535, NonceLength = 32, ConversationKeyLength = 32;
    public static byte[] ConversationKey(SecretKey mine, PublicKey theirs);
    public static (byte[] ChaChaKey, byte[] ChaChaNonce, byte[] HmacKey) MessageKeys(byte[] conversationKey, byte[] nonce);
    public static int CalcPaddedLength(int unpaddedLength);
    public static string Encrypt(string plaintext, byte[] conversationKey, byte[] nonce);   // nonce: 32 bytes from the caller
    public static string Decrypt(string payload, byte[] conversationKey);                   // throws Nip44Exception
}
public static class Nip19 {
    public const string NpubPrefix = "npub", NsecPrefix = "nsec";
    public static string ToNpub(string hexPubKey); public static string ToNsec(string hexPrivKey);
    public static (string Hrp, string Hex) Decode(string bech32String);
    public static (string Hrp, byte[] Data) DecodeToBytes(string bech32String);
    public static string Encode(string hrp, byte[] data);
}
public sealed class IdentityFormatException(string message, Exception? innerException = null) : FormatException;
public static class IdentityDocument { public const int CurrentVersion = 1; public static string Serialize(SecretKey key); public static SecretKey Parse(string document); }

namespace TakEngine.Abstractions;
public interface IKeyStore {
    Task<byte[]?> LoadSecretAsync(CancellationToken cancellationToken = default);      // null = none yet; KeyStoreException = unreadable, untouched
    Task SaveNewSecretAsync(byte[] secret32, CancellationToken cancellationToken = default); // KeyStoreException if one exists
}
public sealed class KeyStoreException(string message, Exception? innerException = null) : Exception;

namespace TakEngine.Storage.Local;
public sealed class FileKeyStore(string dataDirectory) : IKeyStore { public const string FileName = "identity.json"; public string FilePath { get; } }
public static class IdentityBootstrap { public static Task<SecretKey> LoadOrCreateAsync(IKeyStore store, Func<byte[]> random32, CancellationToken cancellationToken = default); }

namespace TakEngine.Transport.Nostr;
public static class Nip01Serializer {
    public static string Serialize(string pubkey, long createdAt, int kind, IReadOnlyList<IReadOnlyList<string>> tags, string content);
    public static string Serialize(NostrEvent evt);
    public static byte[] ComputeIdBytes(string pubkey, long createdAt, int kind, IReadOnlyList<IReadOnlyList<string>> tags, string content);
    public static string ComputeId(NostrEvent evt);                                          // 64 lowercase hex
}
public static class NostrEvents {
    public static NostrEvent Sign(SecretKey key, long createdAt, int kind, IReadOnlyList<IReadOnlyList<string>> tags, string content, ReadOnlySpan<byte> aux32);
    public static bool Verify(NostrEvent? evt);                                              // never throws
}
// NostrEvent (existing mutable class: Id, Pubkey, CreatedAt, Kind, List<List<string>> Tags, Content, Sig) keeps its shape; ComputeId() is removed.

namespace TakEngine.Core.Cryptography;
public static class PayloadSignature { public static string Sign(SecretKey key, string payload); public static bool Verify(string? publicKeyHex, string? payload, string? signatureHex); }
namespace TakEngine.Core.Session;
// TakGameSession.CreateRemote(GameId id, BoardSize size, PlayerColor localColor, SecretKey localKey, PublicKey opponentPubKey)
```

---

### Task 1: F-029 — prove the CI gate red and green on GitHub

**Files:**
- Create (throwaway branch only, never on the milestone branch): `tests/TakEngine.Core.Tests/GateProofTests.cs`
- Modify: `pipeline/features.json` (F-029 `evidence` only)
- Read only: `.github/workflows/ci.yml`, `.github/workflows/deploy-gh-pages.yml`

**Interfaces:**
- Consumes: nothing.
- Produces: a pushed milestone branch with a green CI run; F-029 evidence (run ids). No code.

- [ ] **Step 1: Confirm the local baseline is green**

```bash
export MSBuildEnableWorkloadResolver=false
git status --short            # expected: empty
git branch --show-current     # expected: m0a-crypto-fake-relay-game
dotnet restore TakGame.Ci.slnf
dotnet build TakGame.Ci.slnf --no-restore
dotnet format TakGame.Ci.slnf --verify-no-changes --no-restore
dotnet test TakGame.Ci.slnf --no-build --filter "Category!=LiveRelay"
```
Expected: every command exits 0; each test assembly prints `Passed!`. (Known flake until Task 4 deletes it: `TransportBenchmarkTests.RoundTripPayloadProcessing_CompletesWellUnder300ms` can fail on a cold run; re-run once if it does.)

- [ ] **Step 2: Confirm the deploy is gated (static check)**

```bash
grep -n "uses: ./.github/workflows/ci.yml" .github/workflows/deploy-gh-pages.yml
grep -n "needs: test" .github/workflows/deploy-gh-pages.yml
grep -n "Category!=LiveRelay" .github/workflows/ci.yml
grep -n "TakGame.Ci.slnf" .github/workflows/ci.yml
```
Expected: each grep prints at least one line. Do not edit the workflows.

- [ ] **Step 3: Push the milestone branch and watch its CI run go green**

```bash
git push -u origin m0a-crypto-fake-relay-game
SHA=$(git rev-parse HEAD)
gh run list --workflow ci.yml --commit "$SHA" --json databaseId,status,conclusion
```
If the list is empty the run has not registered yet; poll the last command every ~5 s (a background `until` loop) until it shows one run. Then:
```bash
GREEN_MILESTONE=<databaseId from the list>
gh run watch "$GREEN_MILESTONE" --exit-status
gh run view "$GREEN_MILESTONE" --json conclusion,url,headSha
```
Expected: `gh run watch` exits 0, `conclusion` is `success`. Note the id and url. This also proves "restore on ubuntu works without Android/iOS workloads" (the run restores `TakGame.Ci.slnf` on `ubuntu-latest`).

- [ ] **Step 4: Create the throwaway branch with one deliberately failing test**

```bash
git switch -c ci-gate-proof-f029
```
Create `tests/TakEngine.Core.Tests/GateProofTests.cs`:
```csharp
namespace TakEngine.Core.Tests;

/// <summary>Throwaway (F-029 gate proof). Lives only on branch ci-gate-proof-f029 and is reverted there.</summary>
public class GateProofTests
{
    [Fact]
    public void DeliberatelyRed_ProvesTheCiGate()
    {
        Assert.Fail("Deliberate failure: F-029 proves that a red test turns CI red.");
    }
}
```
Run the first three checks so the run fails in the test step, not earlier:
```bash
dotnet build TakGame.Ci.slnf --no-restore
dotnet format TakGame.Ci.slnf --verify-no-changes --no-restore
dotnet test TakGame.Ci.slnf --no-build --filter "FullyQualifiedName~GateProofTests"
```
Expected: build and format exit 0; the test run reports `Failed DeliberatelyRed_ProvesTheCiGate` (red locally).

- [ ] **Step 5: Commit, push, and watch CI go red**

```bash
git add tests/TakEngine.Core.Tests/GateProofTests.cs
git commit -m "Throwaway: deliberately failing test to prove the CI gate (F-029)" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
git push -u origin ci-gate-proof-f029
SHA=$(git rev-parse HEAD)
gh run list --workflow ci.yml --commit "$SHA" --json databaseId,status,conclusion
```
Poll until the run appears, then (wait for this run to finish before pushing anything else to this branch: `ci.yml` cancels an in-progress run on the same ref):
```bash
RED=<databaseId>
gh run watch "$RED" --exit-status; echo "exit=$?"
gh run view "$RED" --json conclusion,url --jq '.conclusion + " " + .url'
gh run view "$RED" --json jobs --jq '.jobs[].steps[] | select(.conclusion=="failure") | .name'
gh run view "$RED" --log-failed | grep -m1 "DeliberatelyRed_ProvesTheCiGate"
```
Expected: `exit=1`, conclusion `failure`, the failed step is `Test (excluding LiveRelay)`, and the log names `DeliberatelyRed_ProvesTheCiGate`.

- [ ] **Step 6: Revert, push, and watch CI go green**

```bash
git revert --no-edit HEAD
git push
SHA=$(git rev-parse HEAD)
gh run list --workflow ci.yml --commit "$SHA" --json databaseId,status,conclusion
```
Poll until the run appears, then:
```bash
GREEN_REVERT=<databaseId>
gh run watch "$GREEN_REVERT" --exit-status
gh run view "$GREEN_REVERT" --json conclusion,url --jq '.conclusion + " " + .url'
```
Expected: exit 0, conclusion `success`.

- [ ] **Step 7: Delete the throwaway branch locally and on origin**

```bash
git switch m0a-crypto-fake-relay-game
git branch -D ci-gate-proof-f029
git push origin --delete ci-gate-proof-f029
git ls-remote --heads origin ci-gate-proof-f029   # expected: no output
test ! -e tests/TakEngine.Core.Tests/GateProofTests.cs && echo "not on milestone branch"
```

- [ ] **Step 8: Record the evidence in `pipeline/features.json`**

Replace exactly this line (inside the F-029 entry):
```json
      "evidence": "Seen in code: neither .github/workflows/deploy-gh-pages.yml nor release.yml has a test step. docs/AUDIT.md flagged this as Critical on 2026-09-12; still open."
```
with (fill the three run ids/urls and the date you observed in Steps 3, 5, 6):
```json
      "evidence": "Seen running <YYYY-MM-DD>: ci.yml on ubuntu-latest restores, builds, format-checks and tests TakGame.Ci.slnf (no Android/iOS workloads needed) excluding Category=LiveRelay. Milestone branch m0a-crypto-fake-relay-game green: run <GREEN_MILESTONE> (<url>). Throwaway branch ci-gate-proof-f029 with GateProofTests.DeliberatelyRed_ProvesTheCiGate: red run <RED> (<url>), failed step 'Test (excluding LiveRelay)'; green after revert: run <GREEN_REVERT> (<url>); branch deleted locally and on origin. Seen in code: deploy-gh-pages.yml job test uses ./.github/workflows/ci.yml and build-and-deploy has needs: test (not exercised live: that needs a push to main, which D-031 forbids). Owed to the user: enable the required status check on main. release.yml gets its gate in M7."
```
Leave `"passes": false`.

- [ ] **Step 9: Run the four checks, commit, push**

```bash
export MSBuildEnableWorkloadResolver=false
dotnet restore TakGame.Ci.slnf
dotnet build TakGame.Ci.slnf --no-restore
dotnet format TakGame.Ci.slnf --verify-no-changes --no-restore
dotnet test TakGame.Ci.slnf --no-build --filter "Category!=LiveRelay"
git add pipeline/features.json
git commit -m "M0 F-029: record CI gate proof (red and green run ids)" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
git push
```
Expected: all four exit 0; push succeeds (CI on this commit should be green; it is not part of the evidence).

---

### Task 2: F-031 (part 1) — `TakEngine.Crypto` with validated keys and NIP-19

**Files:**
- Create: `src/TakEngine.Crypto/TakEngine.Crypto.csproj`, `src/TakEngine.Crypto/InvalidKeyException.cs`, `src/TakEngine.Crypto/SecretKey.cs`, `src/TakEngine.Crypto/PublicKey.cs`
- Move + rewrite: `src/TakEngine.Core/Cryptography/Nip19.cs` -> `src/TakEngine.Crypto/Nip19.cs`
- Create: `tests/TakEngine.Crypto.Tests/TakEngine.Crypto.Tests.csproj`, `VectorFiles.cs`, `KeyTests.cs`, `Nip19Tests.cs`, `Vectors/bip340-test-vectors.csv`, `Vectors/nip44.vectors.json`
- Delete: `tests/TakEngine.Core.Tests/Nip19Tests.cs` (replaced by the Crypto.Tests version; the old one used the Ed25519 `CryptoSigner`)
- Modify: `src/TakEngine.Core/TakEngine.Core.csproj` (reference Crypto), `src/TakApp.Blazor/_Imports.razor`, `src/TakApp.Blazor/Services/BrowserStorage.cs` (namespace of `Nip19`), `TakGame.sln`, `TakGame.slnx`, `TakGame.Ci.slnf`

**Interfaces:**
- Consumes: nothing.
- Produces: `TakEngine.Crypto.SecretKey`, `PublicKey`, `InvalidKeyException`, `Nip19` (exact signatures in "Interfaces this plan produces"). `Nip19.DecodeKey(string, string)` is `internal`. `SecretKey.Inner` (`ECPrivKey`), `PublicKey.XOnly` (`ECXOnlyPubKey`) and `PublicKey.EvenY` (`ECPubKey`) are `internal` and used by Tasks 3-4.

- [ ] **Step 1: Create the two projects and register them in all three solution files**

`src/TakEngine.Crypto/TakEngine.Crypto.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <!-- secp256k1 identity, BIP-340, NIP-44 v2, NIP-19 (docs/decisions/0001, 0002). Pure: no I/O, clock or RNG.
       Bump NBitcoin.Secp256k1 only with the full vector suite in tests/TakEngine.Crypto.Tests green. -->

  <ItemGroup>
    <ProjectReference Include="..\TakEngine.Abstractions\TakEngine.Abstractions.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="BouncyCastle.Cryptography" Version="2.7.0" />
    <PackageReference Include="NBitcoin.Secp256k1" Version="4.0.3" />
  </ItemGroup>

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

</Project>
```

`tests/TakEngine.Crypto.Tests/TakEngine.Crypto.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.4" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <!-- Official vectors, copied verbatim from pipeline/spikes/R-002-R-003-secp256k1-crypto/vectors/
         (paulmillr/nip44 nip44.vectors.json, bitcoin/bips bip-0340/test-vectors.csv). -->
    <EmbeddedResource Include="Vectors\*" LogicalName="Vectors/%(Filename)%(Extension)" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\TakEngine.Crypto\TakEngine.Crypto.csproj" />
  </ItemGroup>

</Project>
```

```bash
export MSBuildEnableWorkloadResolver=false
mkdir -p tests/TakEngine.Crypto.Tests/Vectors
cp pipeline/spikes/R-002-R-003-secp256k1-crypto/vectors/bip340-test-vectors.csv tests/TakEngine.Crypto.Tests/Vectors/
cp pipeline/spikes/R-002-R-003-secp256k1-crypto/vectors/nip44.vectors.json tests/TakEngine.Crypto.Tests/Vectors/
dotnet sln TakGame.sln add src/TakEngine.Crypto/TakEngine.Crypto.csproj --solution-folder src
dotnet sln TakGame.sln add tests/TakEngine.Crypto.Tests/TakEngine.Crypto.Tests.csproj --solution-folder tests
dotnet sln TakGame.slnx add src/TakEngine.Crypto/TakEngine.Crypto.csproj --solution-folder src
dotnet sln TakGame.slnx add tests/TakEngine.Crypto.Tests/TakEngine.Crypto.Tests.csproj --solution-folder tests
```
Replace `TakGame.Ci.slnf` with:
```json
{
  "solution": {
    "path": "TakGame.sln",
    "projects": [
      "src/TakEngine.Abstractions/TakEngine.Abstractions.csproj",
      "src/TakEngine.Crypto/TakEngine.Crypto.csproj",
      "src/TakEngine.Core/TakEngine.Core.csproj",
      "src/TakEngine.Transport/TakEngine.Transport.csproj",
      "src/TakApp.Cli/TakApp.Cli.csproj",
      "src/TakApp.Blazor/TakApp.Blazor.csproj",
      "src/TakApp.Avalonia/TakApp.Avalonia.csproj",
      "src/TakApp.Avalonia.Desktop/TakApp.Avalonia.Desktop.csproj",
      "tests/TakEngine.Core.Tests/TakEngine.Core.Tests.csproj",
      "tests/TakEngine.Crypto.Tests/TakEngine.Crypto.Tests.csproj",
      "tests/TakEngine.Transport.Tests/TakEngine.Transport.Tests.csproj"
    ]
  }
}
```

In `src/TakEngine.Core/TakEngine.Core.csproj` add the Crypto reference under the Abstractions reference (keep BouncyCastle for now; Task 7 removes it):
```xml
    <ProjectReference Include="..\TakEngine.Abstractions\TakEngine.Abstractions.csproj" />
    <ProjectReference Include="..\TakEngine.Crypto\TakEngine.Crypto.csproj" />
```

- [ ] **Step 2: Write the failing tests**

`tests/TakEngine.Crypto.Tests/VectorFiles.cs`:
```csharp
using System.Text.Json;

namespace TakEngine.Crypto.Tests;

/// <summary>Reads the official vector files embedded in this assembly (see the csproj).</summary>
internal static class VectorFiles
{
    public static string ReadText(string fileName)
    {
        using Stream stream = typeof(VectorFiles).Assembly.GetManifestResourceStream("Vectors/" + fileName)
            ?? throw new InvalidOperationException($"Embedded vector file 'Vectors/{fileName}' not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>The "v2" object of nip44.vectors.json.</summary>
    public static JsonElement Nip44V2()
    {
        using JsonDocument doc = JsonDocument.Parse(ReadText("nip44.vectors.json"));
        return doc.RootElement.GetProperty("v2").Clone();
    }

    /// <summary>Rows of bip340-test-vectors.csv without the header: index, secret key, public key, aux_rand, message, signature, result, comment.</summary>
    public static IReadOnlyList<string[]> Bip340Rows() =>
        ReadText("bip340-test-vectors.csv")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Skip(1)
            .Select(line => line.TrimEnd('\r').Split(','))
            .ToList();

    public static byte[] Hex(string hex) => Convert.FromHexString(hex);
}
```

`tests/TakEngine.Crypto.Tests/KeyTests.cs`:
```csharp
namespace TakEngine.Crypto.Tests;

/// <summary>F-031: one secp256k1 key per player; keys are validated at the boundary.</summary>
public class KeyTests
{
    private const string CurveOrderHex = "fffffffffffffffffffffffffffffffebaaedce6af48a03bbfd25e8cd0364141";

    // index, secret key, public key (rows 15-18 share one key; the index keeps the theory rows distinct)
    public static TheoryData<string, string, string> SecretToPublicVectors()
    {
        var data = new TheoryData<string, string, string>();
        foreach (string[] row in VectorFiles.Bip340Rows().Where(r => r[1].Length == 64))
            data.Add(row[0], row[1], row[2]);
        return data;
    }

    [Fact]
    public void Bip340Csv_HasEightSecretKeyRows()
    {
        Assert.Equal(8, SecretToPublicVectors().Count);
    }

    [Theory]
    [MemberData(nameof(SecretToPublicVectors))]
    public void SecretKey_DerivesXOnlyPublicKey_FromBip340Vectors(string index, string secretHex, string expectedPublicHex)
    {
        var key = SecretKey.FromHex(secretHex);

        Assert.True(expectedPublicHex.ToLowerInvariant() == key.PublicKey.ToHex(), $"vector {index}");
        Assert.Equal(VectorFiles.Hex(expectedPublicHex), key.PublicKey.ToBytes());
    }

    [Fact]
    public void Generate_UsesSuppliedRandomness_AndRedrawsOutOfRangeScalars()
    {
        var draws = new Queue<byte[]>([new byte[32], VectorFiles.Hex(CurveOrderHex), VectorFiles.Hex("0000000000000000000000000000000000000000000000000000000000000003")]);

        var key = SecretKey.Generate(draws.Dequeue);

        Assert.Empty(draws);
        Assert.Equal("f9308a019258c31049344f85f89d5229b531c845836f99b08601f113bce036f9", key.PublicKey.ToHex());
    }

    [Fact]
    public void Generate_GivesUpAfter64InvalidDraws_WhenTheRandomSourceIsBroken()
    {
        int draws = 0;

        Assert.Throws<InvalidOperationException>(() => SecretKey.Generate(() =>
        {
            draws++;
            return new byte[32];
        }));
        Assert.Equal(64, draws);
    }

    [Fact]
    public void NullArguments_AreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => SecretKey.FromHex(null!));
        Assert.Throws<ArgumentNullException>(() => SecretKey.FromNsec(null!));
        Assert.Throws<ArgumentNullException>(() => SecretKey.Generate(null!));
        Assert.Throws<ArgumentNullException>(() => PublicKey.FromHex(null!));
        Assert.Throws<ArgumentNullException>(() => PublicKey.FromNpub(null!));
    }

    [Theory]
    [InlineData("0000000000000000000000000000000000000000000000000000000000000000", "out of range")]
    [InlineData(CurveOrderHex, "out of range")]
    [InlineData("ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff", "out of range")]
    [InlineData("0000000000000000000000000000000000000000000000000000000000000003ab", "64 characters")]
    [InlineData("00000000000000000000000000000000000000000000000000000000000003", "64 characters")]
    [InlineData("zz00000000000000000000000000000000000000000000000000000000000003", "not valid hex")]
    public void SecretKey_RejectsInvalidInput_WithSpecificMessage(string hex, string expectedMessagePart)
    {
        var ex = Assert.Throws<InvalidKeyException>(() => SecretKey.FromHex(hex));
        Assert.Contains(expectedMessagePart, ex.Message);
    }

    [Theory]
    [InlineData(31)]
    [InlineData(33)]
    [InlineData(0)]
    public void SecretKey_RejectsWrongByteLength(int length)
    {
        byte[] bytes = new byte[length];
        if (length > 0)
            bytes[^1] = 3;

        var ex = Assert.Throws<InvalidKeyException>(() => SecretKey.FromBytes(bytes));
        Assert.Contains("32 bytes", ex.Message);
        Assert.False(SecretKey.TryFromBytes(bytes, out _));
    }

    [Fact]
    public void SecretKey_AcceptsLargestValidScalar()
    {
        var key = SecretKey.FromHex("fffffffffffffffffffffffffffffffebaaedce6af48a03bbfd25e8cd0364140");

        Assert.Equal(64, key.PublicKey.ToHex().Length);
    }

    [Theory]
    [InlineData("EEFDEA4CDB677750A420FEE807EACF21EB9898AE79B9768766E4FAA04A2D4A34")] // BIP-340 vector 5: not on the curve
    [InlineData("FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFEFFFFFC30")] // BIP-340 vector 14: x >= field size
    [InlineData("1234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef")] // NIP-44 invalid: no sqrt
    [InlineData("0000000000000000000000000000000000000000000000000000000000000000")] // NIP-44 invalid: point on the twist
    public void PublicKey_RejectsPointsNotOnTheCurve(string hex)
    {
        var ex = Assert.Throws<InvalidKeyException>(() => PublicKey.FromHex(hex));
        Assert.Contains("not on the secp256k1 curve", ex.Message);
        Assert.False(PublicKey.TryFromHex(hex, out _));
    }

    [Theory]
    [InlineData(31)]
    [InlineData(33)]
    [InlineData(0)]
    public void PublicKey_RejectsWrongByteLength(int length)
    {
        byte[] valid = SecretKey.FromHex("0000000000000000000000000000000000000000000000000000000000000003").PublicKey.ToBytes();
        byte[] bytes = new byte[length];
        valid.AsSpan(0, Math.Min(length, 32)).CopyTo(bytes);

        var ex = Assert.Throws<InvalidKeyException>(() => PublicKey.FromBytes(bytes));
        Assert.Contains("32 bytes", ex.Message);
        Assert.False(PublicKey.TryFromBytes(bytes, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("f9308a019258c31049344f85f89d5229b531c845836f99b08601f113bce036")]
    [InlineData("g9308a019258c31049344f85f89d5229b531c845836f99b08601f113bce036f9")]
    public void PublicKey_TryFromHex_ReturnsFalseForMalformedHex(string? hex)
    {
        Assert.False(PublicKey.TryFromHex(hex, out var key));
        Assert.Null(key);
    }

    [Fact]
    public void PublicKey_TryFromHex_ReturnsTheKey_ForValidHex()
    {
        Assert.True(PublicKey.TryFromHex("f9308a019258c31049344f85f89d5229b531c845836f99b08601f113bce036f9", out var key));
        Assert.Equal("f9308a019258c31049344f85f89d5229b531c845836f99b08601f113bce036f9", key.ToHex());
        Assert.True(PublicKey.TryFromBytes(key.ToBytes(), out var again));
        Assert.Equal(key, again);
        Assert.True(SecretKey.TryFromBytes(VectorFiles.Hex("0000000000000000000000000000000000000000000000000000000000000003"), out var secret));
        Assert.Equal(key, secret.PublicKey);
    }

    [Fact]
    public void PublicKey_ParsesEitherCase_AndPrintsLowercase()
    {
        var upper = PublicKey.FromHex("F9308A019258C31049344F85F89D5229B531C845836F99B08601F113BCE036F9");
        var lower = PublicKey.FromHex("f9308a019258c31049344f85f89d5229b531c845836f99b08601f113bce036f9");

        Assert.Equal(lower, upper);
        Assert.Equal(lower.GetHashCode(), upper.GetHashCode());
        Assert.Equal("f9308a019258c31049344f85f89d5229b531c845836f99b08601f113bce036f9", upper.ToHex());
        Assert.Equal(upper.ToHex(), upper.ToString());
    }

    [Fact]
    public void TwoIndependentKeys_HaveDifferentPublicKeys()
    {
        var alice = SecretKey.FromHex("0000000000000000000000000000000000000000000000000000000000000003");
        var bob = SecretKey.FromHex("b7e151628aed2a6abf7158809cf4f3c762e7160f38b4da56a784d9045190cfef");

        Assert.NotEqual(alice.PublicKey, bob.PublicKey);
        Assert.False(alice.PublicKey.Equals(null));
        Assert.False(alice.PublicKey.Equals((object)"not a key"));
    }

    [Fact]
    public void SecretKey_ToBytesAndToHex_RoundTrip_AndToStringHidesTheSecret()
    {
        const string hex = "b7e151628aed2a6abf7158809cf4f3c762e7160f38b4da56a784d9045190cfef";
        var key = SecretKey.FromHex(hex);

        Assert.Equal(hex, key.ToHex());
        Assert.Equal(VectorFiles.Hex(hex), key.ToBytes());
        Assert.DoesNotContain(hex, key.ToString());
        Assert.Contains(key.PublicKey.ToHex(), key.ToString());
    }

    [Fact]
    public void ToBytes_ReturnsACopy()
    {
        var key = SecretKey.FromHex("b7e151628aed2a6abf7158809cf4f3c762e7160f38b4da56a784d9045190cfef");
        byte[] secret = key.ToBytes();
        byte[] pub = key.PublicKey.ToBytes();
        secret[0] ^= 0xff;
        pub[0] ^= 0xff;

        Assert.Equal("b7e151628aed2a6abf7158809cf4f3c762e7160f38b4da56a784d9045190cfef", key.ToHex());
        Assert.Equal("dff1d77f2a671c5f36183726db2341be58feae1da2deced843240f7b502ba659", key.PublicKey.ToHex());
    }
}
```

`tests/TakEngine.Crypto.Tests/Nip19Tests.cs` (the bech32 strings `a1qqqd87cq`, `npub106246s`, `nsec1qe882ll`, `nsec1qpvjxt40` were computed with an independent Python bech32 implementation; the NIP-19 pair is from nostr-protocol/nips 19.md):
```csharp
namespace TakEngine.Crypto.Tests;

/// <summary>F-031: npub/nsec (NIP-19), including the example pair from nostr-protocol/nips 19.md.</summary>
public class Nip19Tests
{
    private const string SpecNpub = "npub10elfcs4fr0l0r8af98jlmgdh9c8tcxjvz9qkw038js35mp4dma8qzvjptg";
    private const string SpecNpubHex = "7e7e9c42a91bfef19fa929e5fda1b72e0ebc1a4c1141673e2794234d86addf4e";
    private const string SpecNsec = "nsec1vl029mgpspedva04g90vltkh6fvh240zqtv9k0t9af8935ke9laqsnlfe5";
    private const string SpecNsecHex = "67dea2ed018072d675f5415ecfaed7d2597555e202d85b3d65ea4e58d2d92ffa";

    [Fact]
    public void SpecExamplePair_DecodesToTheDocumentedHex()
    {
        Assert.Equal(SpecNpubHex, PublicKey.FromNpub(SpecNpub).ToHex());
        Assert.Equal(SpecNsecHex, SecretKey.FromNsec(SpecNsec).ToHex());
        Assert.Equal(("npub", SpecNpubHex), Nip19.Decode(SpecNpub));
        Assert.Equal(("nsec", SpecNsecHex), Nip19.Decode(SpecNsec));
    }

    [Fact]
    public void SpecExamplePair_EncodesBackToTheDocumentedStrings()
    {
        Assert.Equal(SpecNpub, PublicKey.FromHex(SpecNpubHex).ToNpub());
        Assert.Equal(SpecNsec, SecretKey.FromHex(SpecNsecHex).ToNsec());
        Assert.Equal(SpecNpub, Nip19.ToNpub(SpecNpubHex));
        Assert.Equal(SpecNsec, Nip19.ToNsec(SpecNsecHex));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void RandomKey_RoundTripsThroughNsecAndNpub(int seed)
    {
        var random = new Random(seed);
        var key = SecretKey.Generate(() =>
        {
            byte[] b = new byte[32];
            random.NextBytes(b);
            return b;
        });

        string nsec = key.ToNsec();
        string npub = key.PublicKey.ToNpub();

        Assert.StartsWith("nsec1", nsec);
        Assert.StartsWith("npub1", npub);
        Assert.Equal(key.ToHex(), SecretKey.FromNsec(nsec).ToHex());
        Assert.Equal(key.PublicKey, PublicKey.FromNpub(npub));
    }

    [Fact]
    public void BadChecksum_IsRejected()
    {
        string tampered = SpecNpub[..^1] + (SpecNpub[^1] == 'q' ? 'p' : 'q');

        Assert.Throws<FormatException>(() => Nip19.Decode(tampered));
        var ex = Assert.Throws<InvalidKeyException>(() => PublicKey.FromNpub(tampered));
        Assert.Contains("checksum", ex.Message);
    }

    [Fact]
    public void WrongPrefix_IsRejected()
    {
        var ex1 = Assert.Throws<InvalidKeyException>(() => PublicKey.FromNpub(SpecNsec));
        Assert.Contains("Expected an npub", ex1.Message);
        var ex2 = Assert.Throws<InvalidKeyException>(() => SecretKey.FromNsec(SpecNpub));
        Assert.Contains("Expected an nsec", ex2.Message);
    }

    [Fact]
    public void NsecOfWrongLength_IsRejectedWithLengthMessage()
    {
        string shortNsec = Nip19.Encode("nsec", new byte[31]);

        var ex = Assert.Throws<InvalidKeyException>(() => SecretKey.FromNsec(shortNsec));
        Assert.Contains("32 bytes", ex.Message);
    }

    [Fact]
    public void OneCharacterPrefix_AndEmptyPayload_DecodeToTheIndependentEncoding()
    {
        // Strings computed with a separate Python bech32 implementation.
        Assert.Equal("a1qqqd87cq", Nip19.Encode("a", [0]));
        var (hrp, data) = Nip19.DecodeToBytes("a1qqqd87cq");
        Assert.Equal("a", hrp);
        Assert.Equal(new byte[] { 0 }, data);
        Assert.Equal("npub106246s", Nip19.Encode("npub", []));
        Assert.Empty(Nip19.DecodeToBytes("npub106246s").Data);
        var ex = Assert.Throws<InvalidKeyException>(() => PublicKey.FromNpub("npub106246s"));
        Assert.Contains("32 bytes, got 0", ex.Message);
    }

    [Theory]
    [InlineData("nsec1qe882ll")]   // a single 5-bit group: not even one byte
    [InlineData("nsec1qpvjxt40")]  // two 5-bit groups with non-zero padding bits
    public void ChecksumValidStrings_WithBadPadding_AreRejected(string input)
    {
        var ex = Assert.Throws<FormatException>(() => Nip19.DecodeToBytes(input));
        Assert.Contains("padding", ex.Message);
        Assert.Throws<InvalidKeyException>(() => SecretKey.FromNsec(input));
    }

    [Fact]
    public void UppercaseInput_IsAccepted()
    {
        Assert.Equal(SpecNpubHex, PublicKey.FromNpub(SpecNpub.ToUpperInvariant()).ToHex());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("npub1")]
    [InlineData("npub1bbbbbb!")]
    public void Garbage_IsRejected(string input)
    {
        Assert.Throws<InvalidKeyException>(() => PublicKey.FromNpub(input));
    }
}
```

- [ ] **Step 3: Run the tests to see them fail**

```bash
dotnet test tests/TakEngine.Crypto.Tests
```
Expected: build FAILS with `error CS0246: The type or namespace name 'SecretKey' could not be found` (and `PublicKey`, `InvalidKeyException`, `Nip19`).

- [ ] **Step 4: Implement the keys**

`src/TakEngine.Crypto/InvalidKeyException.cs`:
```csharp
namespace TakEngine.Crypto;

/// <summary>A secret or public key failed validation at the boundary (wrong length, not hex, not on the curve, out of range).</summary>
public sealed class InvalidKeyException(string message, Exception? innerException = null)
    : FormatException(message, innerException);
```

`src/TakEngine.Crypto/SecretKey.cs`:
```csharp
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using NBitcoin.Secp256k1;

namespace TakEngine.Crypto;

/// <summary>
/// A player's secp256k1 secret (D-011): the one key behind the npub, BIP-340 event and action signatures and NIP-44.
/// Always valid: construction rejects anything that is not 32 bytes or is 0 or &gt;= the curve order n.
/// </summary>
public sealed class SecretKey
{
    public const int Length = 32;

    private readonly byte[] _bytes;

    private SecretKey(byte[] bytes, ECPrivKey inner)
    {
        _bytes = bytes;
        Inner = inner;
        PublicKey = PublicKey.FromBytes(inner.CreateXOnlyPubKey().ToBytes());
    }

    internal ECPrivKey Inner { get; }

    public PublicKey PublicKey { get; }

    public static SecretKey FromBytes(ReadOnlySpan<byte> secret32)
    {
        if (secret32.Length != Length)
            throw new InvalidKeyException($"Secret key must be {Length} bytes, got {secret32.Length}.");
        if (!ECPrivKey.TryCreate(secret32, out ECPrivKey? inner))
            throw new InvalidKeyException("Secret key is out of range (zero or not below the curve order).");
        return new SecretKey(secret32.ToArray(), inner!);
    }

    public static bool TryFromBytes(ReadOnlySpan<byte> secret32, [NotNullWhen(true)] out SecretKey? key)
    {
        try
        {
            key = FromBytes(secret32);
            return true;
        }
        catch (InvalidKeyException)
        {
            key = null;
            return false;
        }
    }

    /// <summary>Parses exactly 64 hex characters (either case).</summary>
    public static SecretKey FromHex(string hex)
    {
        ArgumentNullException.ThrowIfNull(hex);
        if (hex.Length != Length * 2)
            throw new InvalidKeyException($"Secret key hex must be {Length * 2} characters, got {hex.Length}.");
        byte[] bytes = new byte[Length];
        if (Convert.FromHexString(hex, bytes, out _, out _) != OperationStatus.Done)
            throw new InvalidKeyException("Secret key is not valid hex.");
        return FromBytes(bytes);
    }

    /// <summary>Decodes an <c>nsec1…</c> string (NIP-19).</summary>
    public static SecretKey FromNsec(string nsec)
    {
        return FromBytes(Nip19.DecodeKey(nsec, Nip19.NsecPrefix));
    }

    /// <summary>
    /// Creates a key from caller-supplied randomness (pure: the shell passes
    /// <c>() =&gt; RandomNumberGenerator.GetBytes(32)</c>). Draws again in the negligible case of an out-of-range scalar.
    /// </summary>
    public static SecretKey Generate(Func<byte[]> random32)
    {
        ArgumentNullException.ThrowIfNull(random32);
        for (int attempt = 0; attempt < 64; attempt++)
        {
            if (TryFromBytes(random32(), out var key))
                return key;
        }
        throw new InvalidOperationException("The random source produced 64 invalid secret keys in a row.");
    }

    public byte[] ToBytes() => (byte[])_bytes.Clone();

    public string ToHex() => Convert.ToHexStringLower(_bytes);

    public string ToNsec() => Nip19.Encode(Nip19.NsecPrefix, _bytes);

    /// <summary>Never prints the secret.</summary>
    public override string ToString() => $"SecretKey(pub={PublicKey.ToHex()})";
}
```

`src/TakEngine.Crypto/PublicKey.cs`:
```csharp
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using NBitcoin.Secp256k1;

namespace TakEngine.Crypto;

/// <summary>
/// A player's 32-byte x-only secp256k1 public key (BIP-340 / Nostr "pubkey"). Always valid: construction rejects
/// any input that is not exactly 32 bytes or is not the x coordinate of a point on the curve.
/// </summary>
public sealed class PublicKey : IEquatable<PublicKey>
{
    public const int Length = 32;

    private readonly byte[] _bytes;

    private PublicKey(byte[] bytes, ECPubKey evenY)
    {
        _bytes = bytes;
        EvenY = evenY;
        XOnly = evenY.ToXOnlyPubKey();
    }

    internal ECXOnlyPubKey XOnly { get; }

    /// <summary>The full point with even y (BIP-340 lift_x); the NIP-44 ECDH multiplies this point.</summary>
    internal ECPubKey EvenY { get; }

    public static PublicKey FromBytes(ReadOnlySpan<byte> xOnly32)
    {
        if (xOnly32.Length != Length)
            throw new InvalidKeyException($"Public key must be {Length} bytes, got {xOnly32.Length}.");

        Span<byte> compressed = stackalloc byte[33];
        compressed[0] = 0x02;
        xOnly32.CopyTo(compressed[1..]);
        if (!ECPubKey.TryCreate(compressed, Context.Instance, out _, out ECPubKey? evenY))
            throw new InvalidKeyException("Public key is not on the secp256k1 curve.");

        return new PublicKey(xOnly32.ToArray(), evenY!);
    }

    public static bool TryFromBytes(ReadOnlySpan<byte> xOnly32, [NotNullWhen(true)] out PublicKey? key)
    {
        try
        {
            key = FromBytes(xOnly32);
            return true;
        }
        catch (InvalidKeyException)
        {
            key = null;
            return false;
        }
    }

    /// <summary>Parses exactly 64 hex characters (either case).</summary>
    public static PublicKey FromHex(string hex)
    {
        ArgumentNullException.ThrowIfNull(hex);
        if (hex.Length != Length * 2)
            throw new InvalidKeyException($"Public key hex must be {Length * 2} characters, got {hex.Length}.");
        byte[] bytes = new byte[Length];
        if (Convert.FromHexString(hex, bytes, out _, out _) != OperationStatus.Done)
            throw new InvalidKeyException("Public key is not valid hex.");
        return FromBytes(bytes);
    }

    public static bool TryFromHex(string? hex, [NotNullWhen(true)] out PublicKey? key)
    {
        key = null;
        if (hex is null)
            return false;
        try
        {
            key = FromHex(hex);
            return true;
        }
        catch (InvalidKeyException)
        {
            return false;
        }
    }

    /// <summary>Decodes an <c>npub1…</c> string (NIP-19).</summary>
    public static PublicKey FromNpub(string npub)
    {
        return FromBytes(Nip19.DecodeKey(npub, Nip19.NpubPrefix));
    }

    public byte[] ToBytes() => (byte[])_bytes.Clone();

    /// <summary>Lowercase hex, 64 characters: the form used in Nostr events and tags.</summary>
    public string ToHex() => Convert.ToHexStringLower(_bytes);

    public string ToNpub() => Nip19.Encode(Nip19.NpubPrefix, _bytes);

    public bool Equals(PublicKey? other) => other is not null && _bytes.AsSpan().SequenceEqual(other._bytes);

    public override bool Equals(object? obj) => Equals(obj as PublicKey);

    public override int GetHashCode() => BitConverter.ToInt32(_bytes, 0);

    public override string ToString() => ToHex();
}
```

- [ ] **Step 5: Move NIP-19 into Crypto**

```bash
git mv src/TakEngine.Core/Cryptography/Nip19.cs src/TakEngine.Crypto/Nip19.cs
git rm tests/TakEngine.Core.Tests/Nip19Tests.cs
```
Replace the whole content of `src/TakEngine.Crypto/Nip19.cs` with:
```csharp
using System;
using System.Collections.Generic;
using System.Text;

namespace TakEngine.Crypto;

/// <summary>
/// NIP-19 bech32 encoding and decoding for Nostr identities (npub, nsec). Moved from TakEngine.Core.Cryptography (F-031).
/// </summary>
public static class Nip19
{
    public const string NpubPrefix = "npub";
    public const string NsecPrefix = "nsec";

    private const string Charset = "qpzry9x8gf2tvdw0s3jn54khce6mua7l";
    private static readonly uint[] Generator = [0x3b6a57b2u, 0x26508e6du, 0x1ea119fau, 0x3d4233ddu, 0x2a1462b3u];

    public static string ToNpub(string hexPubKey)
    {
        byte[] bytes = Convert.FromHexString(hexPubKey);
        return Encode("npub", bytes);
    }

    public static string ToNsec(string hexPrivKey)
    {
        byte[] bytes = Convert.FromHexString(hexPrivKey);
        return Encode("nsec", bytes);
    }

    public static (string Hrp, string Hex) Decode(string bech32String)
    {
        var (hrp, data) = DecodeToBytes(bech32String);
        return (hrp, Convert.ToHexStringLower(data));
    }

    /// <summary>Decodes any bech32 string to its human-readable part and payload bytes. Throws <see cref="FormatException"/> on a bad checksum, character or padding.</summary>
    public static (string Hrp, byte[] Data) DecodeToBytes(string bech32String)
    {
        ArgumentNullException.ThrowIfNull(bech32String);
        bech32String = bech32String.Trim().ToLowerInvariant();
        int pos = bech32String.LastIndexOf('1');
        if (pos < 1 || pos + 7 > bech32String.Length)
            throw new FormatException("Invalid Bech32 separator position.");

        string hrp = bech32String[..pos];
        string dataPart = bech32String[(pos + 1)..];

        var values = new byte[dataPart.Length];
        for (int i = 0; i < dataPart.Length; i++)
        {
            int idx = Charset.IndexOf(dataPart[i]);
            if (idx < 0)
                throw new FormatException($"Invalid Bech32 character '{dataPart[i]}'.");
            values[i] = (byte)idx;
        }

        if (!VerifyChecksum(hrp, values))
            throw new FormatException("Invalid Bech32 checksum.");

        byte[] payload5Bit = new byte[values.Length - 6];
        Array.Copy(values, 0, payload5Bit, 0, payload5Bit.Length);

        byte[] bytes = ConvertBits(payload5Bit, 5, 8, false);
        return (hrp, bytes);
    }

    /// <summary>Decodes an npub/nsec and checks its prefix. Every failure is an <see cref="InvalidKeyException"/> with a specific message.</summary>
    internal static byte[] DecodeKey(string bech32String, string expectedHrp)
    {
        var (hrp, data) = DecodeAsKey(bech32String, expectedHrp);
        if (hrp != expectedHrp)
            throw new InvalidKeyException($"Expected an {expectedHrp}, got '{hrp}'.");
        return data;
    }

    private static (string Hrp, byte[] Data) DecodeAsKey(string bech32String, string expectedHrp)
    {
        try
        {
            return DecodeToBytes(bech32String);
        }
        catch (FormatException ex)
        {
            throw new InvalidKeyException($"Not a valid {expectedHrp}: {ex.Message}", ex);
        }
    }

    public static string Encode(string hrp, byte[] data)
    {
        hrp = hrp.ToLowerInvariant();
        byte[] values = ConvertBits(data, 8, 5, true);
        byte[] checksum = CreateChecksum(hrp, values);

        var sb = new StringBuilder(hrp.Length + 1 + values.Length + checksum.Length);
        sb.Append(hrp);
        sb.Append('1');

        foreach (byte b in values)
            sb.Append(Charset[b]);

        foreach (byte b in checksum)
            sb.Append(Charset[b]);

        return sb.ToString();
    }

    private static uint Polymod(byte[] values)
    {
        uint chk = 1;
        foreach (byte v in values)
        {
            byte top = (byte)(chk >> 25);
            chk = ((chk & 0x1ffffff) << 5) ^ v;
            for (int i = 0; i < 5; i++)
            {
                if (((top >> i) & 1) != 0)
                    chk ^= Generator[i];
            }
        }
        return chk;
    }

    private static byte[] HrpExpand(string hrp)
    {
        byte[] ret = new byte[hrp.Length * 2 + 1];
        for (int i = 0; i < hrp.Length; i++)
        {
            ret[i] = (byte)(hrp[i] >> 5);
            ret[i + hrp.Length + 1] = (byte)(hrp[i] & 31);
        }
        ret[hrp.Length] = 0;
        return ret;
    }

    private static bool VerifyChecksum(string hrp, byte[] values)
    {
        byte[] hrpExp = HrpExpand(hrp);
        byte[] enc = new byte[hrpExp.Length + values.Length];
        Array.Copy(hrpExp, 0, enc, 0, hrpExp.Length);
        Array.Copy(values, 0, enc, hrpExp.Length, values.Length);
        return Polymod(enc) == 1;
    }

    private static byte[] CreateChecksum(string hrp, byte[] values)
    {
        byte[] hrpExp = HrpExpand(hrp);
        byte[] enc = new byte[hrpExp.Length + values.Length + 6];
        Array.Copy(hrpExp, 0, enc, 0, hrpExp.Length);
        Array.Copy(values, 0, enc, hrpExp.Length, values.Length);
        uint mod = Polymod(enc) ^ 1;
        byte[] ret = new byte[6];
        for (int i = 0; i < 6; i++)
        {
            ret[i] = (byte)((mod >> (5 * (5 - i))) & 31);
        }
        return ret;
    }

    private static byte[] ConvertBits(byte[] data, int fromBits, int toBits, bool pad)
    {
        int acc = 0;
        int bits = 0;
        int maxv = (1 << toBits) - 1;
        var ret = new List<byte>();

        foreach (byte value in data)
        {
            acc = (acc << fromBits) | value;
            bits += fromBits;
            while (bits >= toBits)
            {
                bits -= toBits;
                ret.Add((byte)((acc >> bits) & maxv));
            }
        }

        if (pad)
        {
            if (bits > 0)
                ret.Add((byte)((acc << (toBits - bits)) & maxv));
        }
        else if (bits >= fromBits || ((acc << (toBits - bits)) & maxv) != 0)
        {
            throw new FormatException("Invalid bit conversion padding.");
        }

        return ret.ToArray();
    }
}
```

Blazor used `Nip19` through the old namespace. In `src/TakApp.Blazor/_Imports.razor`, directly under `@using TakEngine.Core.Cryptography`, add:
```razor
@using TakEngine.Crypto
```
In `src/TakApp.Blazor/Services/BrowserStorage.cs`, directly under `using TakEngine.Core.Cryptography;`, add:
```csharp
using TakEngine.Crypto;
```
(Blazor sees `TakEngine.Crypto` through its Core reference.)

- [ ] **Step 6: Run the tests to see them pass**

```bash
dotnet test tests/TakEngine.Crypto.Tests
```
Expected: `Passed!`, 0 failed. The secret-to-pubkey theory runs for the 8 csv rows that carry a secret key.

- [ ] **Step 7: Run the four checks**

```bash
export MSBuildEnableWorkloadResolver=false
dotnet restore TakGame.Ci.slnf
dotnet build TakGame.Ci.slnf --no-restore
dotnet format TakGame.Ci.slnf --verify-no-changes --no-restore
dotnet test TakGame.Ci.slnf --no-build --filter "Category!=LiveRelay"
```
Expected: all exit 0; Core, Transport and Crypto test assemblies print `Passed!`. If `dotnet format` reports changes, run `dotnet format TakGame.Ci.slnf` and re-run the checks.

- [ ] **Step 8: Commit**

```bash
git add -A src/TakEngine.Crypto tests/TakEngine.Crypto.Tests src/TakEngine.Core/TakEngine.Core.csproj src/TakApp.Blazor/_Imports.razor src/TakApp.Blazor/Services/BrowserStorage.cs TakGame.sln TakGame.slnx TakGame.Ci.slnf src/TakEngine.Core/Cryptography/Nip19.cs tests/TakEngine.Core.Tests/Nip19Tests.cs
git status --short   # expected: nothing left unstaged
git commit -m "M0 F-031: TakEngine.Crypto with validated secp256k1 keys and NIP-19" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: F-032 (part 1) — BIP-340 Schnorr sign/verify with the official vectors

**Files:**
- Create: `src/TakEngine.Crypto/Schnorr.cs`, `tests/TakEngine.Crypto.Tests/SchnorrTests.cs`

**Interfaces:**
- Consumes: `SecretKey` (incl. `internal ECPrivKey Inner`), `PublicKey` (incl. `internal ECXOnlyPubKey XOnly`), `VectorFiles.Bip340Rows()`, `VectorFiles.Hex(string)` from Task 2.
- Produces: `public static class Schnorr { DigestLength = 32; AuxLength = 32; SignatureLength = 64; byte[] Sign(SecretKey key, ReadOnlySpan<byte> digest32, ReadOnlySpan<byte> aux32); bool Verify(PublicKey key, ReadOnlySpan<byte> digest32, ReadOnlySpan<byte> signature64); }`

- [ ] **Step 1: Write the failing tests**

`tests/TakEngine.Crypto.Tests/SchnorrTests.cs` (the 15 verify vectors are the csv rows with a 32-byte message, including the invalid ones; a vector whose public key is not on the curve must fail at `PublicKey.TryFromBytes`, which counts as "verify = false"):
```csharp
namespace TakEngine.Crypto.Tests;

/// <summary>F-032 (BIP-340 part): bitcoin/bips test-vectors.csv plus two independent keys.</summary>
public class SchnorrTests
{
    // Rows with a 32-byte message. Rows 15-18 sign 0/1/17/100-byte messages; Nostr only signs 32-byte digests.
    public static TheoryData<string, string, string, string, bool> VerifyVectors()
    {
        var data = new TheoryData<string, string, string, string, bool>();
        foreach (string[] row in VectorFiles.Bip340Rows().Where(r => r[4].Length == 64))
            data.Add(row[0], row[2], row[4], row[5], row[6] == "TRUE");
        return data;
    }

    public static TheoryData<string, string, string, string> SignVectors()
    {
        var data = new TheoryData<string, string, string, string>();
        foreach (string[] row in VectorFiles.Bip340Rows().Where(r => r[1].Length == 64 && r[4].Length == 64))
            data.Add(row[1], row[3], row[4], row[5]);
        return data;
    }

    [Fact]
    public void Csv_Has15VerifyVectorsAnd4SignVectors()
    {
        Assert.Equal(15, VerifyVectors().Count);
        Assert.Equal(4, SignVectors().Count);
    }

    [Theory]
    [MemberData(nameof(VerifyVectors))]
    public void Verify_MatchesBip340Vectors(string index, string publicHex, string messageHex, string signatureHex, bool expected)
    {
        bool actual = PublicKey.TryFromBytes(VectorFiles.Hex(publicHex), out var key)
            && Schnorr.Verify(key, VectorFiles.Hex(messageHex), VectorFiles.Hex(signatureHex));

        Assert.True(expected == actual, $"vector {index}: expected {expected}, got {actual}");
    }

    [Theory]
    [MemberData(nameof(SignVectors))]
    public void Sign_WithVectorAux_ProducesTheExactVectorSignature(string secretHex, string auxHex, string messageHex, string signatureHex)
    {
        var key = SecretKey.FromHex(secretHex);

        byte[] signature = Schnorr.Sign(key, VectorFiles.Hex(messageHex), VectorFiles.Hex(auxHex));

        Assert.Equal(signatureHex.ToLowerInvariant(), Convert.ToHexStringLower(signature));
        Assert.True(Schnorr.Verify(key.PublicKey, VectorFiles.Hex(messageHex), signature));
    }

    [Fact]
    public void SignatureByAlice_DoesNotVerifyUnderBob_OrForAnotherDigest_OrWithAFlippedBit()
    {
        var alice = SecretKey.FromHex("b7e151628aed2a6abf7158809cf4f3c762e7160f38b4da56a784d9045190cfef");
        var bob = SecretKey.FromHex("c90fdaa22168c234c4c6628b80dc1cd129024e088a67cc74020bbea63b14e5c9");
        byte[] digest = System.Security.Cryptography.SHA256.HashData("tak move a1"u8);
        byte[] otherDigest = System.Security.Cryptography.SHA256.HashData("tak move a2"u8);

        byte[] signature = Schnorr.Sign(alice, digest, new byte[32]);

        Assert.True(Schnorr.Verify(alice.PublicKey, digest, signature));
        Assert.False(Schnorr.Verify(bob.PublicKey, digest, signature));
        Assert.False(Schnorr.Verify(alice.PublicKey, otherDigest, signature));
        for (int bit = 0; bit < 512; bit += 37)
        {
            byte[] flipped = (byte[])signature.Clone();
            flipped[bit / 8] ^= (byte)(1 << (bit % 8));
            Assert.False(Schnorr.Verify(alice.PublicKey, digest, flipped), $"flipped bit {bit} still verified");
        }
    }

    [Fact]
    public void DifferentAux_GivesDifferentSignatures_ThatBothVerify()
    {
        var key = SecretKey.FromHex("b7e151628aed2a6abf7158809cf4f3c762e7160f38b4da56a784d9045190cfef");
        byte[] digest = new byte[32];
        byte[] aux1 = new byte[32];
        byte[] aux2 = new byte[32];
        aux2[31] = 1;

        byte[] sig1 = Schnorr.Sign(key, digest, aux1);
        byte[] sig2 = Schnorr.Sign(key, digest, aux2);

        Assert.NotEqual(sig1, sig2);
        Assert.True(Schnorr.Verify(key.PublicKey, digest, sig1));
        Assert.True(Schnorr.Verify(key.PublicKey, digest, sig2));
    }

    [Fact]
    public void NullKey_IsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => Schnorr.Sign(null!, new byte[32], new byte[32]));
        Assert.Throws<ArgumentNullException>(() => Schnorr.Verify(null!, new byte[32], new byte[64]));
    }

    [Theory]
    [InlineData(31, 64)]
    [InlineData(33, 64)]
    [InlineData(32, 63)]
    [InlineData(32, 65)]
    [InlineData(0, 0)]
    public void Verify_ReturnsFalse_ForWrongLengths(int digestLength, int signatureLength)
    {
        var key = SecretKey.FromHex("b7e151628aed2a6abf7158809cf4f3c762e7160f38b4da56a784d9045190cfef");

        Assert.False(Schnorr.Verify(key.PublicKey, new byte[digestLength], new byte[signatureLength]));
    }

    [Theory]
    [InlineData(31, 32)]
    [InlineData(33, 32)]
    [InlineData(32, 31)]
    [InlineData(32, 0)]
    public void Sign_RejectsWrongDigestOrAuxLength(int digestLength, int auxLength)
    {
        var key = SecretKey.FromHex("b7e151628aed2a6abf7158809cf4f3c762e7160f38b4da56a784d9045190cfef");

        Assert.ThrowsAny<ArgumentException>(() => Schnorr.Sign(key, new byte[digestLength], new byte[auxLength]));
    }
}
```

- [ ] **Step 2: Run the tests to see them fail**

```bash
export MSBuildEnableWorkloadResolver=false
dotnet test tests/TakEngine.Crypto.Tests
```
Expected: build FAILS with `error CS0103: The name 'Schnorr' does not exist in the current context`.

- [ ] **Step 3: Implement**

`src/TakEngine.Crypto/Schnorr.cs` (API checked against NBitcoin.Secp256k1 4.0.3: `ECPrivKey.SignBIP340(ReadOnlySpan<byte>, ReadOnlyMemory<byte>)`, `SecpSchnorrSignature.TryCreate`, `ECXOnlyPubKey.SigVerifyBIP340`):
```csharp
using NBitcoin.Secp256k1;

namespace TakEngine.Crypto;

/// <summary>BIP-340 Schnorr signatures over 32-byte digests (Nostr event ids, game action digests).</summary>
public static class Schnorr
{
    public const int DigestLength = 32;
    public const int AuxLength = 32;
    public const int SignatureLength = 64;

    /// <summary>
    /// Signs a 32-byte digest. <paramref name="aux32"/> is BIP-340 auxiliary randomness supplied by the caller
    /// (shell: <c>RandomNumberGenerator.GetBytes(32)</c>; tests: fixed bytes so official vectors pin the output).
    /// </summary>
    public static byte[] Sign(SecretKey key, ReadOnlySpan<byte> digest32, ReadOnlySpan<byte> aux32)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (digest32.Length != DigestLength)
            throw new ArgumentException($"Digest must be {DigestLength} bytes, got {digest32.Length}.", nameof(digest32));
        if (aux32.Length != AuxLength)
            throw new ArgumentException($"Aux randomness must be {AuxLength} bytes, got {aux32.Length}.", nameof(aux32));

        return key.Inner.SignBIP340(digest32, aux32.ToArray()).ToBytes();
    }

    /// <summary>True only for a valid BIP-340 signature by <paramref name="key"/> over <paramref name="digest32"/>. Never throws on malformed input.</summary>
    public static bool Verify(PublicKey key, ReadOnlySpan<byte> digest32, ReadOnlySpan<byte> signature64)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (digest32.Length != DigestLength || signature64.Length != SignatureLength)
            return false;
        if (!SecpSchnorrSignature.TryCreate(signature64, out SecpSchnorrSignature? signature))
            return false;
        return key.XOnly.SigVerifyBIP340(signature!, digest32);
    }
}
```

- [ ] **Step 4: Run the tests to see them pass**

```bash
dotnet test tests/TakEngine.Crypto.Tests --filter "FullyQualifiedName~SchnorrTests"
```
Expected: `Passed!`, 0 failed (15 verify vectors, 4 sign vectors with exact signatures).

- [ ] **Step 5: Run the four checks**

```bash
export MSBuildEnableWorkloadResolver=false
dotnet restore TakGame.Ci.slnf
dotnet build TakGame.Ci.slnf --no-restore
dotnet format TakGame.Ci.slnf --verify-no-changes --no-restore
dotnet test TakGame.Ci.slnf --no-build --filter "Category!=LiveRelay"
```
Expected: all exit 0.

- [ ] **Step 6: Commit**

```bash
git add src/TakEngine.Crypto/Schnorr.cs tests/TakEngine.Crypto.Tests/SchnorrTests.cs
git commit -m "M0 F-032: BIP-340 Schnorr sign/verify with official vectors" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: F-015 — NIP-44 v2 between two independent peers; delete `Nip44Encryption`

**Files:**
- Create: `src/TakEngine.Crypto/Nip44.cs`, `tests/TakEngine.Crypto.Tests/Nip44VectorTests.cs`, `tests/TakEngine.Crypto.Tests/Nip44TwoPeerTests.cs`
- Delete: `src/TakEngine.Transport/Nostr/Nip44Encryption.cs`, `tests/TakEngine.Transport.Tests/Nip44EncryptionTests.cs` (both tests; `Nip44_EncryptAndDecrypt_RoundTripsSuccessfully` passed for the wrong reason), `tests/TakEngine.Transport.Tests/TransportBenchmarkTests.cs` (its only test decrypts with the sender's own secret; replaced by `Nip44TwoPeerTests.TwoPeerRoundTrip_IsFastEnoughForOneMovePerTurn`)
- Modify: `src/TakEngine.Transport/TakEngine.Transport.csproj` (reference Crypto, drop BouncyCastle), `src/TakEngine.Transport/Nostr/NostrTransportClient.cs` (last caller of `Nip44Encryption`; the whole class is deleted in A2/F-034)

**Interfaces:**
- Consumes: `SecretKey` (`internal ECPrivKey Inner`), `PublicKey` (`internal ECPubKey EvenY`), `SecretKey.Generate(Func<byte[]>)`, `VectorFiles.Nip44V2()` from Task 2.
- Produces: `Nip44` (`ConversationKey`, `MessageKeys`, `CalcPaddedLength`, `Encrypt(string, byte[] conversationKey, byte[] nonce)`, `Decrypt(string, byte[])`, constants), `Nip44Exception` with `Nip44Error Error`, `enum Nip44Error { UnknownVersion, InvalidPayloadSize, InvalidBase64, InvalidMac, InvalidPadding }`. Encrypt-side input errors throw `ArgumentException` (or `ArgumentOutOfRangeException`); every decrypt failure throws `Nip44Exception`.

- [ ] **Step 1: Write the failing tests**

`tests/TakEngine.Crypto.Tests/Nip44VectorTests.cs`:
```csharp
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TakEngine.Crypto.Tests;

/// <summary>F-015: paulmillr/nip44 nip44.vectors.json, every section.</summary>
public class Nip44VectorTests
{
    private static readonly JsonElement V2 = VectorFiles.Nip44V2();
    private static readonly JsonElement Valid = V2.GetProperty("valid");
    private static readonly JsonElement Invalid = V2.GetProperty("invalid");

    private static byte[] Hex(JsonElement e, string name) => Convert.FromHexString(e.GetProperty(name).GetString()!);

    private static string Str(JsonElement e, string name) => e.GetProperty(name).GetString()!;

    [Fact]
    public void VectorFile_HasTheExpectedSectionSizes()
    {
        Assert.Equal(35, Valid.GetProperty("get_conversation_key").GetArrayLength());
        Assert.Equal(32, Valid.GetProperty("get_message_keys").GetProperty("keys").GetArrayLength());
        Assert.Equal(24, Valid.GetProperty("calc_padded_len").GetArrayLength());
        Assert.Equal(10, Valid.GetProperty("encrypt_decrypt").GetArrayLength());
        Assert.Equal(3, Valid.GetProperty("encrypt_decrypt_long_msg").GetArrayLength());
        Assert.Equal(8, Invalid.GetProperty("get_conversation_key").GetArrayLength());
        Assert.Equal(12, Invalid.GetProperty("decrypt").GetArrayLength());
        Assert.Equal(4, Invalid.GetProperty("encrypt_msg_lengths").GetArrayLength());
    }

    [Fact]
    public void ConversationKey_MatchesAllValidVectors()
    {
        foreach (JsonElement t in Valid.GetProperty("get_conversation_key").EnumerateArray())
        {
            byte[] actual = Nip44.ConversationKey(SecretKey.FromHex(Str(t, "sec1")), PublicKey.FromHex(Str(t, "pub2")));

            Assert.Equal(Str(t, "conversation_key"), Convert.ToHexStringLower(actual));
        }
    }

    [Fact]
    public void ConversationKey_InvalidVectors_AreRejectedAtTheKeyBoundary()
    {
        foreach (JsonElement t in Invalid.GetProperty("get_conversation_key").EnumerateArray())
        {
            string note = Str(t, "note");
            var ex = Assert.Throws<InvalidKeyException>(
                () => Nip44.ConversationKey(SecretKey.FromHex(Str(t, "sec1")), PublicKey.FromHex(Str(t, "pub2"))));
            Assert.True(ex.Message.Length > 0, note);
        }
    }

    [Fact]
    public void MessageKeys_MatchAllVectors()
    {
        JsonElement section = Valid.GetProperty("get_message_keys");
        byte[] conversationKey = Hex(section, "conversation_key");
        foreach (JsonElement t in section.GetProperty("keys").EnumerateArray())
        {
            var (chachaKey, chachaNonce, hmacKey) = Nip44.MessageKeys(conversationKey, Hex(t, "nonce"));

            Assert.Equal(Str(t, "chacha_key"), Convert.ToHexStringLower(chachaKey));
            Assert.Equal(Str(t, "chacha_nonce"), Convert.ToHexStringLower(chachaNonce));
            Assert.Equal(Str(t, "hmac_key"), Convert.ToHexStringLower(hmacKey));
        }
    }

    [Fact]
    public void CalcPaddedLength_MatchesAllVectors()
    {
        foreach (JsonElement t in Valid.GetProperty("calc_padded_len").EnumerateArray())
            Assert.Equal(t[1].GetInt32(), Nip44.CalcPaddedLength(t[0].GetInt32()));
    }

    [Fact]
    public void EncryptDecrypt_MatchesAllVectors_InBothKeyDirections()
    {
        foreach (JsonElement t in Valid.GetProperty("encrypt_decrypt").EnumerateArray())
        {
            var sec1 = SecretKey.FromHex(Str(t, "sec1"));
            var sec2 = SecretKey.FromHex(Str(t, "sec2"));
            byte[] expectedKey = Hex(t, "conversation_key");
            string plaintext = Str(t, "plaintext");
            string payload = Str(t, "payload");

            Assert.Equal(expectedKey, Nip44.ConversationKey(sec1, sec2.PublicKey));
            Assert.Equal(expectedKey, Nip44.ConversationKey(sec2, sec1.PublicKey));
            Assert.Equal(payload, Nip44.Encrypt(plaintext, expectedKey, Hex(t, "nonce")));
            Assert.Equal(plaintext, Nip44.Decrypt(payload, expectedKey));
        }
    }

    [Fact]
    public void LongMessages_MatchAllVectors()
    {
        foreach (JsonElement t in Valid.GetProperty("encrypt_decrypt_long_msg").EnumerateArray())
        {
            byte[] conversationKey = Hex(t, "conversation_key");
            string plaintext = string.Concat(Enumerable.Repeat(Str(t, "pattern"), t.GetProperty("repeat").GetInt32()));

            string payload = Nip44.Encrypt(plaintext, conversationKey, Hex(t, "nonce"));

            Assert.Equal(Str(t, "plaintext_sha256"), Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(plaintext))));
            Assert.Equal(Str(t, "payload_sha256"), Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload))));
            Assert.Equal(plaintext, Nip44.Decrypt(payload, conversationKey));
        }
    }

    [Fact]
    public void InvalidPayloads_FailToDecrypt_WithTheReasonTheVectorNames()
    {
        foreach (JsonElement t in Invalid.GetProperty("decrypt").EnumerateArray())
        {
            string note = Str(t, "note");
            Nip44Error expected = note switch
            {
                _ when note.StartsWith("unknown encryption version", StringComparison.Ordinal) => Nip44Error.UnknownVersion,
                "invalid base64" => Nip44Error.InvalidBase64,
                "invalid MAC" => Nip44Error.InvalidMac,
                "invalid padding" => Nip44Error.InvalidPadding,
                _ when note.StartsWith("invalid payload length", StringComparison.Ordinal) => Nip44Error.InvalidPayloadSize,
                _ => throw new InvalidOperationException("unmapped vector note: " + note),
            };

            var ex = Assert.Throws<Nip44Exception>(() => Nip44.Decrypt(Str(t, "payload"), Hex(t, "conversation_key")));
            Assert.True(expected == ex.Error, $"'{note}': expected {expected}, got {ex.Error}");
        }
    }

    [Fact]
    public void InvalidMessageLengths_AreRejectedOnEncrypt()
    {
        byte[] conversationKey = Convert.FromHexString("c41c775356fd92eadc63ff5a0dc1da211b268cbea22316767095b2871ea1412d");
        foreach (JsonElement length in Invalid.GetProperty("encrypt_msg_lengths").EnumerateArray())
        {
            string plaintext = new('x', length.GetInt32());
            Assert.ThrowsAny<ArgumentException>(() => Nip44.Encrypt(plaintext, conversationKey, new byte[32]));
        }
    }
}
```

`tests/TakEngine.Crypto.Tests/Nip44TwoPeerTests.cs` (every round trip has the RECEIVER derive the key from his own secret and the sender's public key):
```csharp
using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Parameters;

namespace TakEngine.Crypto.Tests;

/// <summary>
/// F-015: two independent peers. The receiver always decrypts with HIS OWN secret and the sender's public key;
/// nothing here decrypts with the sender's own keys (that is how the deleted Nip44Encryption passed while wrong).
/// </summary>
public class Nip44TwoPeerTests
{
    private const string Move = "{\"pv\":1,\"action_type\":\"MOVE\",\"turn\":7,\"action_data\":{\"ptn\":\"3c3+12\"}}";

    private static SecretKey NewKey(Random random) => SecretKey.Generate(() =>
    {
        byte[] bytes = new byte[32];
        random.NextBytes(bytes);
        return bytes;
    });

    private static byte[] NewNonce(Random random)
    {
        byte[] nonce = new byte[32];
        random.NextBytes(nonce);
        return nonce;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(20261002)]
    public void IndependentPeers_DeriveTheSameKey_AndDecryptEachOther(int seed)
    {
        var random = new Random(seed);
        var alice = NewKey(random);
        var bob = NewKey(random);

        byte[] aliceSide = Nip44.ConversationKey(alice, bob.PublicKey);
        byte[] bobSide = Nip44.ConversationKey(bob, alice.PublicKey);
        Assert.Equal(aliceSide, bobSide);

        string toBob = Nip44.Encrypt(Move, aliceSide, NewNonce(random));
        Assert.Equal(Move, Nip44.Decrypt(toBob, bobSide));

        string toAlice = Nip44.Encrypt("ack é 表 🦄", bobSide, NewNonce(random));
        Assert.Equal("ack é 表 🦄", Nip44.Decrypt(toAlice, aliceSide));
    }

    [Fact]
    public void ThirdKey_CannotDecrypt()
    {
        var random = new Random(7);
        var alice = NewKey(random);
        var bob = NewKey(random);
        var eve = NewKey(random);
        string toBob = Nip44.Encrypt(Move, Nip44.ConversationKey(alice, bob.PublicKey), NewNonce(random));

        var ex = Assert.Throws<Nip44Exception>(() => Nip44.Decrypt(toBob, Nip44.ConversationKey(eve, alice.PublicKey)));
        Assert.Equal(Nip44Error.InvalidMac, ex.Error);
    }

    [Theory]
    [InlineData(1)]     // first nonce byte
    [InlineData(40)]    // ciphertext
    [InlineData(-1)]    // last MAC byte
    public void FlippedBit_FailsTheMac(int position)
    {
        var random = new Random(11);
        var alice = NewKey(random);
        var bob = NewKey(random);
        string payload = Nip44.Encrypt(Move, Nip44.ConversationKey(alice, bob.PublicKey), NewNonce(random));
        byte[] raw = Convert.FromBase64String(payload);
        raw[position >= 0 ? position : raw.Length + position] ^= 0x01;

        var ex = Assert.Throws<Nip44Exception>(
            () => Nip44.Decrypt(Convert.ToBase64String(raw), Nip44.ConversationKey(bob, alice.PublicKey)));
        Assert.Equal(Nip44Error.InvalidMac, ex.Error);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(0)]
    public void WrongVersionByte_IsUnknownVersion(byte version)
    {
        var random = new Random(13);
        var alice = NewKey(random);
        var bob = NewKey(random);
        byte[] raw = Convert.FromBase64String(Nip44.Encrypt(Move, Nip44.ConversationKey(alice, bob.PublicKey), NewNonce(random)));
        raw[0] = version;

        var ex = Assert.Throws<Nip44Exception>(
            () => Nip44.Decrypt(Convert.ToBase64String(raw), Nip44.ConversationKey(bob, alice.PublicKey)));
        Assert.Equal(Nip44Error.UnknownVersion, ex.Error);
    }

    [Theory]
    [InlineData(0)]  // zero length
    [InlineData(31)] // calc_padded_len(31) = 32, but the block is 64 bytes
    [InlineData(65)] // calc_padded_len(65) = 96: claims more bytes than the 64-byte block holds
    public void BadPadding_WithAValidMac_IsInvalidPadding(int claimedLength)
    {
        var random = new Random(17);
        var alice = NewKey(random);
        var bob = NewKey(random);
        byte[] conversationKey = Nip44.ConversationKey(alice, bob.PublicKey);
        byte[] nonce = NewNonce(random);

        // A sender who authenticates correctly but pads wrongly: 2-byte length prefix + a 64-byte block.
        byte[] padded = new byte[2 + 64];
        BinaryPrimitives.WriteUInt16BigEndian(padded, (ushort)claimedLength);
        string payload = SealWithValidMac(conversationKey, nonce, padded);

        var ex = Assert.Throws<Nip44Exception>(() => Nip44.Decrypt(payload, Nip44.ConversationKey(bob, alice.PublicKey)));
        Assert.Equal(Nip44Error.InvalidPadding, ex.Error);
    }

    [Fact]
    public void PayloadSizeLimits_AreChecked_BeforeAnyCrypto()
    {
        byte[] key = new byte[32];
        byte[] tooShortData = new byte[98];
        tooShortData[0] = 2;
        byte[] tooLongData = new byte[65604];
        tooLongData[0] = 2;

        Assert.Equal(Nip44Error.UnknownVersion, Assert.Throws<Nip44Exception>(() => Nip44.Decrypt("#" + new string('A', 140), key)).Error);
        Assert.Equal(Nip44Error.InvalidPayloadSize, Assert.Throws<Nip44Exception>(() => Nip44.Decrypt(new string('A', 131), key)).Error);
        Assert.Equal(Nip44Error.InvalidPayloadSize, Assert.Throws<Nip44Exception>(() => Nip44.Decrypt(new string('A', 87473), key)).Error);
        Assert.Equal(Nip44Error.InvalidPayloadSize, Assert.Throws<Nip44Exception>(() => Nip44.Decrypt(Convert.ToBase64String(tooShortData), key)).Error);
        Assert.Equal(Nip44Error.InvalidPayloadSize, Assert.Throws<Nip44Exception>(() => Nip44.Decrypt(Convert.ToBase64String(tooLongData), key)).Error);
    }

    [Fact]
    public void PlaintextLimits_AreCountedInUtf8Bytes()
    {
        var random = new Random(19);
        byte[] key = Nip44.ConversationKey(NewKey(random), NewKey(random).PublicKey);
        string maxAscii = new('x', Nip44.MaxPlaintextBytes);
        string maxThreeByte = new('表', Nip44.MaxPlaintextBytes / 3);

        Assert.Equal(maxAscii, Nip44.Decrypt(Nip44.Encrypt(maxAscii, key, NewNonce(random)), key));
        Assert.Equal(maxThreeByte, Nip44.Decrypt(Nip44.Encrypt(maxThreeByte, key, NewNonce(random)), key));
        Assert.ThrowsAny<ArgumentException>(() => Nip44.Encrypt(maxAscii + "x", key, NewNonce(random)));
        Assert.ThrowsAny<ArgumentException>(() => Nip44.Encrypt(maxThreeByte + "表", key, NewNonce(random)));
        Assert.ThrowsAny<ArgumentException>(() => Nip44.Encrypt("", key, NewNonce(random)));
    }

    [Fact]
    public void NullArguments_AreRejected()
    {
        var key = SecretKey.FromHex("0000000000000000000000000000000000000000000000000000000000000003");
        Assert.Throws<ArgumentNullException>(() => Nip44.ConversationKey(null!, key.PublicKey));
        Assert.Throws<ArgumentNullException>(() => Nip44.ConversationKey(key, null!));
        Assert.Throws<ArgumentNullException>(() => Nip44.MessageKeys(null!, new byte[32]));
        Assert.Throws<ArgumentNullException>(() => Nip44.MessageKeys(new byte[32], null!));
        Assert.Throws<ArgumentNullException>(() => Nip44.Encrypt(null!, new byte[32], new byte[32]));
        Assert.Throws<ArgumentNullException>(() => Nip44.Decrypt(null!, new byte[32]));
        Assert.Throws<ArgumentOutOfRangeException>(() => Nip44.CalcPaddedLength(0));
    }

    [Fact]
    public void Encrypt_RejectsWrongKeyOrNonceLength()
    {
        Assert.ThrowsAny<ArgumentException>(() => Nip44.Encrypt("a", new byte[31], new byte[32]));
        Assert.ThrowsAny<ArgumentException>(() => Nip44.Encrypt("a", new byte[32], new byte[31]));
        Assert.ThrowsAny<ArgumentException>(() => Nip44.Encrypt("a", new byte[32], new byte[33]));
    }

    [Fact]
    public void TwoPeerRoundTrip_IsFastEnoughForOneMovePerTurn()
    {
        // Replaces TransportBenchmarkTests (which decrypted with the sender's own secret). Best of 5 runs, so a
        // cold JIT or a busy CI runner does not make it flaky; the bound is generous on purpose.
        var random = new Random(23);
        var alice = NewKey(random);
        var bob = NewKey(random);
        long best = long.MaxValue;
        for (int run = 0; run < 5; run++)
        {
            var stopwatch = Stopwatch.StartNew();
            string payload = Nip44.Encrypt(Move, Nip44.ConversationKey(alice, bob.PublicKey), NewNonce(random));
            string received = Nip44.Decrypt(payload, Nip44.ConversationKey(bob, alice.PublicKey));
            stopwatch.Stop();
            Assert.Equal(Move, received);
            best = Math.Min(best, stopwatch.ElapsedMilliseconds);
        }

        Assert.True(best < 300, $"best of 5 two-peer round trips took {best} ms");
    }

    private static string SealWithValidMac(byte[] conversationKey, byte[] nonce, byte[] padded)
    {
        var (chachaKey, chachaNonce, hmacKey) = Nip44.MessageKeys(conversationKey, nonce);
        var engine = new ChaCha7539Engine();
        engine.Init(true, new ParametersWithIV(new KeyParameter(chachaKey), chachaNonce));
        byte[] ciphertext = new byte[padded.Length];
        engine.ProcessBytes(padded, 0, padded.Length, ciphertext, 0);
        byte[] mac = HMACSHA256.HashData(hmacKey, (byte[])[.. nonce, .. ciphertext]);
        return Convert.ToBase64String([2, .. nonce, .. ciphertext, .. mac]);
    }
}
```

- [ ] **Step 2: Run the tests to see them fail**

```bash
export MSBuildEnableWorkloadResolver=false
dotnet test tests/TakEngine.Crypto.Tests
```
Expected: build FAILS with `error CS0103: The name 'Nip44' does not exist` / `CS0246: 'Nip44Exception' could not be found`.

- [ ] **Step 3: Implement NIP-44 v2**

`src/TakEngine.Crypto/Nip44.cs` (glue copied deliberately from the spike's `NostrCrypto.cs`, with HKDF-expand from the BCL and the 65535-byte cap; no extended length prefix):
```csharp
using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Parameters;
using HMACSHA256 = System.Security.Cryptography.HMACSHA256;

namespace TakEngine.Crypto;

/// <summary>Why a NIP-44 payload was rejected. Every decrypt failure maps to exactly one reason.</summary>
public enum Nip44Error
{
    UnknownVersion,
    InvalidPayloadSize,
    InvalidBase64,
    InvalidMac,
    InvalidPadding,
}

/// <summary>A NIP-44 payload could not be decrypted. <see cref="Error"/> says why.</summary>
public sealed class Nip44Exception(Nip44Error error, string message) : CryptographicException(message)
{
    public Nip44Error Error { get; } = error;
}

/// <summary>
/// NIP-44 v2 (nostr-protocol/nips 44.md), proven by paulmillr/nip44 nip44.vectors.json. Pure: the 32-byte nonce is an
/// argument (shell: <c>RandomNumberGenerator.GetBytes(32)</c>). Plaintext is capped at 65535 UTF-8 bytes
/// (2-byte length prefix only; docs/decisions/0002 "cap plaintext at 64 KiB").
/// </summary>
public static class Nip44
{
    public const byte Version = 2;
    public const int MinPlaintextBytes = 1;
    public const int MaxPlaintextBytes = 65535;
    public const int NonceLength = 32;
    public const int ConversationKeyLength = 32;
    private const int MacLength = 32;
    private const int MinPayloadChars = 132;
    private const int MaxPayloadChars = 87472;
    private const int MinDataBytes = 99;
    private const int MaxDataBytes = 65603;

    private static readonly byte[] Salt = Encoding.UTF8.GetBytes("nip44-v2");

    /// <summary>
    /// conversation_key = HKDF-extract(salt = "nip44-v2", IKM = unhashed shared x of mine * theirs).
    /// Symmetric: ConversationKey(a, B) == ConversationKey(b, A).
    /// </summary>
    public static byte[] ConversationKey(SecretKey mine, PublicKey theirs)
    {
        ArgumentNullException.ThrowIfNull(mine);
        ArgumentNullException.ThrowIfNull(theirs);
        var shared = theirs.EvenY.GetSharedPubkey(mine.Inner); // raw point, NOT a hashing ECDH helper
        byte[] sharedX = shared.ToBytes(true)[1..];
        return HMACSHA256.HashData(Salt, sharedX);
    }

    /// <summary>HKDF-expand(conversation_key, info = nonce, 76) split into ChaCha20 key (32), ChaCha20 nonce (12), HMAC key (32).</summary>
    public static (byte[] ChaChaKey, byte[] ChaChaNonce, byte[] HmacKey) MessageKeys(byte[] conversationKey, byte[] nonce)
    {
        ArgumentNullException.ThrowIfNull(conversationKey);
        ArgumentNullException.ThrowIfNull(nonce);
        if (conversationKey.Length != ConversationKeyLength)
            throw new ArgumentException($"Conversation key must be {ConversationKeyLength} bytes.", nameof(conversationKey));
        if (nonce.Length != NonceLength)
            throw new ArgumentException($"Nonce must be {NonceLength} bytes.", nameof(nonce));

        byte[] keys = HKDF.Expand(HashAlgorithmName.SHA256, conversationKey, 76, nonce);
        return (keys[..32], keys[32..44], keys[44..76]);
    }

    public static int CalcPaddedLength(int unpaddedLength)
    {
        if (unpaddedLength < 1)
            throw new ArgumentOutOfRangeException(nameof(unpaddedLength), "Length must be at least 1.");
        if (unpaddedLength <= 32)
            return 32;
        int nextPower = 1 << (BitOperations.Log2((uint)(unpaddedLength - 1)) + 1);
        int chunk = nextPower <= 256 ? 32 : nextPower / 8;
        return chunk * (((unpaddedLength - 1) / chunk) + 1);
    }

    public static string Encrypt(string plaintext, byte[] conversationKey, byte[] nonce)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        var (chachaKey, chachaNonce, hmacKey) = MessageKeys(conversationKey, nonce);
        byte[] ciphertext = ChaCha20(chachaKey, chachaNonce, Pad(plaintext));
        byte[] mac = HmacAad(hmacKey, ciphertext, nonce);
        return Convert.ToBase64String([Version, .. nonce, .. ciphertext, .. mac]);
    }

    /// <summary>Decrypts and authenticates. Any failure throws <see cref="Nip44Exception"/>; nothing is returned unauthenticated.</summary>
    public static string Decrypt(string payload, byte[] conversationKey)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (payload.Length > 0 && payload[0] == '#')
            throw new Nip44Exception(Nip44Error.UnknownVersion, "Unknown encryption version.");
        if (payload.Length < MinPayloadChars || payload.Length > MaxPayloadChars)
            throw new Nip44Exception(Nip44Error.InvalidPayloadSize, $"Invalid payload size {payload.Length}.");

        byte[] buffer = new byte[payload.Length];
        if (!Convert.TryFromBase64String(payload, buffer, out int written))
            throw new Nip44Exception(Nip44Error.InvalidBase64, "Payload is not valid base64.");
        byte[] data = buffer[..written];
        if (data.Length < MinDataBytes || data.Length > MaxDataBytes)
            throw new Nip44Exception(Nip44Error.InvalidPayloadSize, $"Invalid data size {data.Length}.");
        if (data[0] != Version)
            throw new Nip44Exception(Nip44Error.UnknownVersion, $"Unknown encryption version {data[0]}.");

        byte[] nonce = data[1..33];
        byte[] ciphertext = data[33..^MacLength];
        byte[] mac = data[^MacLength..];
        var (chachaKey, chachaNonce, hmacKey) = MessageKeys(conversationKey, nonce);
        if (!CryptographicOperations.FixedTimeEquals(HmacAad(hmacKey, ciphertext, nonce), mac))
            throw new Nip44Exception(Nip44Error.InvalidMac, "Invalid MAC.");

        return Unpad(ChaCha20(chachaKey, chachaNonce, ciphertext));
    }

    private static byte[] Pad(string plaintext)
    {
        byte[] unpadded = Encoding.UTF8.GetBytes(plaintext);
        if (unpadded.Length < MinPlaintextBytes || unpadded.Length > MaxPlaintextBytes)
            throw new ArgumentOutOfRangeException(
                nameof(plaintext),
                $"Plaintext must be {MinPlaintextBytes}..{MaxPlaintextBytes} UTF-8 bytes, got {unpadded.Length}.");

        byte[] padded = new byte[2 + CalcPaddedLength(unpadded.Length)];
        BinaryPrimitives.WriteUInt16BigEndian(padded, (ushort)unpadded.Length);
        unpadded.CopyTo(padded, 2);
        return padded;
    }

    private static string Unpad(byte[] padded)
    {
        int length = BinaryPrimitives.ReadUInt16BigEndian(padded);
        if (length == 0 || padded.Length != 2 + CalcPaddedLength(length))
            throw new Nip44Exception(Nip44Error.InvalidPadding, "Invalid padding.");
        return Encoding.UTF8.GetString(padded, 2, length);
    }

    private static byte[] ChaCha20(byte[] key, byte[] nonce12, byte[] input)
    {
        var engine = new ChaCha7539Engine(); // RFC 8439 ChaCha20, counter starts at 0
        engine.Init(true, new ParametersWithIV(new KeyParameter(key), nonce12));
        byte[] output = new byte[input.Length];
        engine.ProcessBytes(input, 0, input.Length, output, 0);
        return output;
    }

    private static byte[] HmacAad(byte[] key, byte[] message, byte[] aad) =>
        HMACSHA256.HashData(key, (byte[])[.. aad, .. message]);
}
```

- [ ] **Step 4: Run the tests to see them pass**

```bash
dotnet test tests/TakEngine.Crypto.Tests --filter "FullyQualifiedName~Nip44"
```
Expected: `Passed!`, 0 failed.

- [ ] **Step 5: Delete the wrong implementation and its wrong-reason tests; rewire the last caller**

```bash
git rm src/TakEngine.Transport/Nostr/Nip44Encryption.cs tests/TakEngine.Transport.Tests/Nip44EncryptionTests.cs tests/TakEngine.Transport.Tests/TransportBenchmarkTests.cs
```
Replace `src/TakEngine.Transport/TakEngine.Transport.csproj` with:
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <ProjectReference Include="..\TakEngine.Abstractions\TakEngine.Abstractions.csproj" />
    <ProjectReference Include="..\TakEngine.Crypto\TakEngine.Crypto.csproj" />
  </ItemGroup>

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

</Project>
```

Apply to `src/TakEngine.Transport/Nostr/NostrTransportClient.cs`:
```diff
diff --git a/src/TakEngine.Transport/Nostr/NostrTransportClient.cs b/src/TakEngine.Transport/Nostr/NostrTransportClient.cs
index b58f0a5..8ba22fc 100644
--- a/src/TakEngine.Transport/Nostr/NostrTransportClient.cs
+++ b/src/TakEngine.Transport/Nostr/NostrTransportClient.cs
@@ -1,9 +1,11 @@
 using System;
 using System.Collections.Concurrent;
 using System.Collections.Generic;
+using System.Security.Cryptography;
 using System.Text.Json;
 using System.Threading;
 using System.Threading.Tasks;
+using TakEngine.Crypto;
 
 namespace TakEngine.Transport.Nostr;
 
@@ -100,8 +102,8 @@ public sealed class NostrTransportClient : IAsyncDisposable
         CancellationToken cancellationToken = default)
     {
         string json = JsonSerializer.Serialize(envelope);
-        byte[] sharedSecret = Nip44Encryption.DeriveSharedSecret(_localPrivKey, recipientPubKey);
-        string encryptedContent = Nip44Encryption.Encrypt(json, sharedSecret);
+        byte[] conversationKey = Nip44.ConversationKey(SecretKey.FromHex(_localPrivKey), PublicKey.FromHex(recipientPubKey));
+        string encryptedContent = Nip44.Encrypt(json, conversationKey, RandomNumberGenerator.GetBytes(Nip44.NonceLength));
 
         var evt = new NostrEvent
         {
@@ -212,8 +214,8 @@ public sealed class NostrTransportClient : IAsyncDisposable
 
             try
             {
-                byte[] sharedSecret = Nip44Encryption.DeriveSharedSecret(_localPrivKey, evt.Pubkey);
-                string decryptedJson = Nip44Encryption.Decrypt(evt.Content, sharedSecret);
+                byte[] conversationKey = Nip44.ConversationKey(SecretKey.FromHex(_localPrivKey), PublicKey.FromHex(evt.Pubkey));
+                string decryptedJson = Nip44.Decrypt(evt.Content, conversationKey);
 
                 var envelope = JsonSerializer.Deserialize<TransportEnvelope>(decryptedJson);
                 if (envelope != null)
```

Check nothing else uses the old type:
```bash
git grep -n "Nip44Encryption\|DeriveSharedSecret" -- src tests   # expected: only the doc comment in Nip44TwoPeerTests.cs that names the deleted class
```

- [ ] **Step 6: Run the four checks**

```bash
export MSBuildEnableWorkloadResolver=false
dotnet restore TakGame.Ci.slnf
dotnet build TakGame.Ci.slnf --no-restore
dotnet format TakGame.Ci.slnf --verify-no-changes --no-restore
dotnet test TakGame.Ci.slnf --no-build --filter "Category!=LiveRelay"
```
Expected: all exit 0.

- [ ] **Step 7: Commit**

```bash
git add -A src/TakEngine.Crypto/Nip44.cs tests/TakEngine.Crypto.Tests src/TakEngine.Transport tests/TakEngine.Transport.Tests
git commit -m "M0 F-015: NIP-44 v2 with official vectors between two independent peers; delete Nip44Encryption" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: F-032 (part 2) — hand-written NIP-01 serializer and event sign/verify

**Files:**
- Create: `src/TakEngine.Transport/Nostr/Nip01Serializer.cs`, `src/TakEngine.Transport/Nostr/NostrEvents.cs`
- Create: `tests/TakEngine.Transport.Tests/Vectors/make_nip01_vectors.py`, `Vectors/nip01-vectors.json` (generated), `Vectors/spike-csharp-events.jsonl`, `Vectors/spike-python-events.jsonl` (copied), `tests/TakEngine.Transport.Tests/Nip01VectorFiles.cs`, `Nip01SerializerTests.cs`, `NostrEventsTests.cs`
- Modify: `tests/TakEngine.Transport.Tests/TakEngine.Transport.Tests.csproj` (embedded vectors), `src/TakEngine.Transport/Nostr/NostrModels.cs` (delete `NostrEvent.ComputeId`), callers `src/TakEngine.Transport/Matchmaking/QuickPlayMatchmaker.cs`, `src/TakEngine.Transport/Nostr/NostrProfile.cs`, `src/TakEngine.Transport/Nostr/NostrTransportClient.cs`, test `tests/TakEngine.Transport.Tests/NostrMessageTests.cs`

**Interfaces:**
- Consumes: `SecretKey`, `PublicKey.TryFromHex`, `PublicKey.ToHex()`, `Schnorr.Sign/Verify`, `Schnorr.SignatureLength` (Tasks 2-3); Transport already references Crypto (Task 4).
- Produces: `Nip01Serializer.Serialize(string pubkey, long createdAt, int kind, IReadOnlyList<IReadOnlyList<string>> tags, string content)`, `Serialize(NostrEvent)`, `ComputeIdBytes(...)`, `ComputeId(NostrEvent)`; `NostrEvents.Sign(SecretKey key, long createdAt, int kind, IReadOnlyList<IReadOnlyList<string>> tags, string content, ReadOnlySpan<byte> aux32) : NostrEvent`, `NostrEvents.Verify(NostrEvent?) : bool`. `NostrEvent.ComputeId()` no longer exists. (`List<List<string>>` converts to `IReadOnlyList<IReadOnlyList<string>>` by covariance.)

- [ ] **Step 1: Generate the independent vectors and copy the spike's verified events**

Create `tests/TakEngine.Transport.Tests/Vectors/make_nip01_vectors.py`:
```python
"""Regenerates nip01-vectors.json (F-032) with an implementation independent of the C# code under test:
NIP-01 id = sha256 of json.dumps([0,pubkey,created_at,kind,tags,content], separators=(",",":"), ensure_ascii=False),
sig = bitcoin/bips BIP-340 reference implementation with a fixed aux per event (so the C# signature must match exactly).
Run from the repo root: python tests/TakEngine.Transport.Tests/Vectors/make_nip01_vectors.py"""
import hashlib
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "..", "..", "pipeline", "spikes", "R-002-R-003-secp256k1-crypto", "vectors"))
from bip340_reference import pubkey_gen, schnorr_sign, schnorr_verify  # noqa: E402

sk = hashlib.sha256(b"tak-p2p nip01 vector key").digest()
pk = pubkey_gen(sk).hex()
recipient = pubkey_gen(hashlib.sha256(b"tak-p2p nip01 vector recipient").digest()).hex()
cases = [
    ("plain", 1, [["t", "tak"]], "hello"),
    ("json-specials", 1, [["t", "tak"]], "a1>+ <b2 & \"q\" \\ / end"),
    ("newline-cr-tab", 1, [["t", "tak"]], "line1\nline2\r\n\tend"),
    ("backspace-formfeed", 1, [["t", "tak"]], "ctl \b \f end"),
    ("u0001-u001f", 1, [["t", "tak"]], "ctl \u0001 \u001f end"),
    ("del", 1, [["t", "tak"]], "del \u007f end"),
    ("emoji-surrogate-pair", 1, [["t", "tak"]], "emoji \U0001F984\U0001F355"),
    ("u2028-u2029", 1, [["t", "tak"]], "sep " + chr(0x2028) + " " + chr(0x2029) + " end"),
    ("non-ascii-tag", 1, [["t", "tak", "é表"]], "unicode é表"),
    ("empty-content", 1, [["t", "tak"]], ""),
    ("empty-tags", 1, [], "no tags"),
    ("kind-3825-envelope", 3825, [["p", recipient], ["g", "0b7e3d4c-1f2a-4b5c-8d9e-0f1a2b3c4d5e"]],
     "AgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABee0G5VSK0/9YypIObAtDKfYEAjD35uVkHyB0F4DwrcNaCXlCWZKaArsGrY6M9wnuTMxWfp1RTN9Xga8no+kF5Vsb"),
]
out = {
    "_source": "Generated by make_nip01_vectors.py (Python json + hashlib for the NIP-01 id, bitcoin/bips bip340_reference.py for the signature).",
    "secret_key": sk.hex(),
    "pubkey": pk,
    "events": [],
}
for i, (name, kind, tags, content) in enumerate(cases):
    created_at = 1700000000 + i
    serialized = json.dumps([0, pk, created_at, kind, tags, content], separators=(",", ":"), ensure_ascii=False)
    event_id = hashlib.sha256(serialized.encode("utf-8")).digest()
    aux = hashlib.sha256(("aux-%d" % i).encode()).digest()
    sig = schnorr_sign(event_id, sk, aux)
    assert schnorr_verify(event_id, bytes.fromhex(pk), sig)
    out["events"].append({
        "name": name, "created_at": created_at, "kind": kind, "tags": tags, "content": content,
        "serialized": serialized, "aux": aux.hex(), "id": event_id.hex(), "sig": sig.hex(),
    })
with open(os.path.join(HERE, "nip01-vectors.json"), "w", encoding="ascii", newline="\n") as f:
    f.write(json.dumps(out, indent=2, ensure_ascii=True) + "\n")
print("wrote nip01-vectors.json with %d events" % len(cases))
```

```bash
python tests/TakEngine.Transport.Tests/Vectors/make_nip01_vectors.py
sha256sum tests/TakEngine.Transport.Tests/Vectors/nip01-vectors.json
cp pipeline/spikes/R-002-R-003-secp256k1-crypto/out/events.jsonl tests/TakEngine.Transport.Tests/Vectors/spike-csharp-events.jsonl
cp pipeline/spikes/R-002-R-003-secp256k1-crypto/out/py-events.jsonl tests/TakEngine.Transport.Tests/Vectors/spike-python-events.jsonl
```
Expected: `wrote nip01-vectors.json with 12 events` and sha256 `c0c47ad3e85d5a50653f341d6a667ddc9fbaec436385e657be20b8b557608d5c` (the generator is deterministic: fixed key, fixed aux). If the hash differs, stop and report: the vectors are not the ones this plan was checked against.

Apply to `tests/TakEngine.Transport.Tests/TakEngine.Transport.Tests.csproj` (only `.json`/`.jsonl` are embedded; the `.py` stays a plain file):
```diff
diff --git a/tests/TakEngine.Transport.Tests/TakEngine.Transport.Tests.csproj b/tests/TakEngine.Transport.Tests/TakEngine.Transport.Tests.csproj
index a58c3b5..baeaa93 100644
--- a/tests/TakEngine.Transport.Tests/TakEngine.Transport.Tests.csproj
+++ b/tests/TakEngine.Transport.Tests/TakEngine.Transport.Tests.csproj
@@ -18,6 +18,12 @@
     <Using Include="Xunit" />
   </ItemGroup>
 
+  <ItemGroup>
+    <!-- NIP-01 known-good events from independent implementations: nip01-vectors.json (make_nip01_vectors.py, Python
+         json/hashlib + BIP-340 reference) and the spike's Python-verified events (pipeline/spikes/R-002-R-003-secp256k1-crypto/out/). -->
+    <EmbeddedResource Include="Vectors\*.json;Vectors\*.jsonl" LogicalName="Vectors/%(Filename)%(Extension)" />
+  </ItemGroup>
+
   <ItemGroup>
     <ProjectReference Include="..\..\src\TakEngine.Abstractions\TakEngine.Abstractions.csproj" />
     <ProjectReference Include="..\..\src\TakEngine.Transport\TakEngine.Transport.csproj" />
```

- [ ] **Step 2: Write the failing tests**

`tests/TakEngine.Transport.Tests/Nip01VectorFiles.cs`:
```csharp
using System.Text.Json;
using TakEngine.Transport.Nostr;

namespace TakEngine.Transport.Tests;

/// <summary>One known-good event from an independent implementation. Fields are exactly as the generator wrote them.</summary>
public sealed record Nip01Vector(
    string Name,
    long CreatedAt,
    int Kind,
    List<List<string>> Tags,
    string Content,
    string Serialized,
    string Aux,
    string Id,
    string Sig);

internal static class Nip01VectorFiles
{
    public static string ReadText(string fileName)
    {
        using Stream stream = typeof(Nip01VectorFiles).Assembly.GetManifestResourceStream("Vectors/" + fileName)
            ?? throw new InvalidOperationException($"Embedded vector file 'Vectors/{fileName}' not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>nip01-vectors.json: (secret key hex, pubkey hex, events).</summary>
    public static (string SecretKeyHex, string PubkeyHex, IReadOnlyList<Nip01Vector> Events) Generated()
    {
        using JsonDocument doc = JsonDocument.Parse(ReadText("nip01-vectors.json"));
        JsonElement root = doc.RootElement;
        var events = root.GetProperty("events").EnumerateArray().Select(e => new Nip01Vector(
            e.GetProperty("name").GetString()!,
            e.GetProperty("created_at").GetInt64(),
            e.GetProperty("kind").GetInt32(),
            e.GetProperty("tags").EnumerateArray().Select(t => t.EnumerateArray().Select(v => v.GetString()!).ToList()).ToList(),
            e.GetProperty("content").GetString()!,
            e.GetProperty("serialized").GetString()!,
            e.GetProperty("aux").GetString()!,
            e.GetProperty("id").GetString()!,
            e.GetProperty("sig").GetString()!)).ToList();
        return (root.GetProperty("secret_key").GetString()!, root.GetProperty("pubkey").GetString()!, events);
    }

    /// <summary>A JSONL file of complete signed events (the spike's C#-signed/Python-verified and Python-signed sets).</summary>
    public static IReadOnlyList<NostrEvent> SignedEvents(string fileName) =>
        ReadText(fileName)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonSerializer.Deserialize<NostrEvent>(line.TrimEnd('\r'))!)
            .ToList();
}
```

`tests/TakEngine.Transport.Tests/Nip01SerializerTests.cs`:
```csharp
using System.Globalization;
using TakEngine.Transport.Nostr;

namespace TakEngine.Transport.Tests;

/// <summary>F-032: NIP-01 ids equal the ids an independent implementation computed, for every escaping edge case.</summary>
public class Nip01SerializerTests
{
    public static TheoryData<string> VectorNames()
    {
        var data = new TheoryData<string>();
        foreach (Nip01Vector v in Nip01VectorFiles.Generated().Events)
            data.Add(v.Name);
        return data;
    }

    private static Nip01Vector Vector(string name) => Nip01VectorFiles.Generated().Events.Single(v => v.Name == name);

    [Fact]
    public void VectorFile_CoversTheEdgeCasesFromTheTestStrategy()
    {
        string[] expected =
        [
            "plain", "json-specials", "newline-cr-tab", "backspace-formfeed", "u0001-u001f", "del",
            "emoji-surrogate-pair", "u2028-u2029", "non-ascii-tag", "empty-content", "empty-tags", "kind-3825-envelope",
        ];
        Assert.Equal(expected, Nip01VectorFiles.Generated().Events.Select(v => v.Name));
    }

    [Theory]
    [MemberData(nameof(VectorNames))]
    public void Serialize_MatchesTheIndependentSerialization(string name)
    {
        Nip01Vector v = Vector(name);
        string pubkey = Nip01VectorFiles.Generated().PubkeyHex;

        Assert.Equal(v.Serialized, Nip01Serializer.Serialize(pubkey, v.CreatedAt, v.Kind, v.Tags, v.Content));
    }

    [Theory]
    [MemberData(nameof(VectorNames))]
    public void ComputeId_MatchesTheIndependentId(string name)
    {
        Nip01Vector v = Vector(name);
        var evt = new NostrEvent
        {
            Pubkey = Nip01VectorFiles.Generated().PubkeyHex,
            CreatedAt = v.CreatedAt,
            Kind = v.Kind,
            Tags = v.Tags,
            Content = v.Content,
        };

        Assert.Equal(v.Id, Nip01Serializer.ComputeId(evt));
    }

    [Theory]
    [InlineData("spike-csharp-events.jsonl", 5)]
    [InlineData("spike-python-events.jsonl", 3)]
    public void ComputeId_MatchesTheSpikesPythonVerifiedEvents(string file, int count)
    {
        IReadOnlyList<NostrEvent> events = Nip01VectorFiles.SignedEvents(file);

        Assert.Equal(count, events.Count);
        Assert.All(events, e => Assert.Equal(e.Id, Nip01Serializer.ComputeId(e)));
    }

    [Fact]
    public void Serialize_IsCultureInvariant()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            // A culture whose negative sign is not '-' (U+2212) would corrupt created_at if the serializer used it.
            var culture = (CultureInfo)CultureInfo.GetCultureInfo("sv-SE").Clone();
            culture.NumberFormat.NegativeSign = "−";
            CultureInfo.CurrentCulture = culture;

            Assert.Equal("[0,\"ab\",-5,1,[],\"\"]", Nip01Serializer.Serialize("ab", -5, 1, [], ""));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Serialize_RejectsNullTagsAndTagValues()
    {
        List<List<string>> nullValue = [["p", null!]];
        List<List<string>> nullTag = [null!];

        Assert.ThrowsAny<ArgumentException>(() => Nip01Serializer.Serialize("ab", 1, 1, nullValue, "x"));
        Assert.ThrowsAny<ArgumentException>(() => Nip01Serializer.Serialize("ab", 1, 1, nullTag, "x"));
    }

    [Fact]
    public void NullArguments_AreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => Nip01Serializer.Serialize(null!, 1, 1, [], "x"));
        Assert.Throws<ArgumentNullException>(() => Nip01Serializer.Serialize("ab", 1, 1, null!, "x"));
        Assert.Throws<ArgumentNullException>(() => Nip01Serializer.Serialize("ab", 1, 1, [], null!));
        Assert.Throws<ArgumentNullException>(() => Nip01Serializer.Serialize(null!));
        Assert.Throws<ArgumentNullException>(() => Nip01Serializer.ComputeId(null!));
    }
}
```

`tests/TakEngine.Transport.Tests/NostrEventsTests.cs` (the `Sign_ProducesExactlyThePythonReferenceEvent` theory is the CI-runnable form of "a C#-signed event verifies in the Python reference verifier": with the same key and aux, the C# signature must be byte-identical to the one `bip340_reference.py` produced and verified):
```csharp
using System.Text.Json;
using TakEngine.Crypto;
using TakEngine.Transport.Nostr;

namespace TakEngine.Transport.Tests;

/// <summary>F-032: events are signed with BIP-340 over the NIP-01 id and verified before anything else is done with them.</summary>
public class NostrEventsTests
{
    private static readonly SecretKey Alice = SecretKey.FromHex("b7e151628aed2a6abf7158809cf4f3c762e7160f38b4da56a784d9045190cfef");
    private static readonly SecretKey Bob = SecretKey.FromHex("c90fdaa22168c234c4c6628b80dc1cd129024e088a67cc74020bbea63b14e5c9");

    public static TheoryData<string> VectorNames()
    {
        var data = new TheoryData<string>();
        foreach (Nip01Vector v in Nip01VectorFiles.Generated().Events)
            data.Add(v.Name);
        return data;
    }

    private static NostrEvent SignedByAlice(string content = "AgAAAA+/==") =>
        NostrEvents.Sign(Alice, 1_700_000_000, 3825, [["p", Bob.PublicKey.ToHex()], ["g", "0b7e3d4c-1f2a-4b5c-8d9e-0f1a2b3c4d5e"]], content, new byte[32]);

    private static NostrEvent Copy(NostrEvent e) => JsonSerializer.Deserialize<NostrEvent>(JsonSerializer.Serialize(e))!;

    [Theory]
    [MemberData(nameof(VectorNames))]
    public void Sign_ProducesExactlyThePythonReferenceEvent(string name)
    {
        var (secretHex, pubkeyHex, events) = Nip01VectorFiles.Generated();
        Nip01Vector v = events.Single(e => e.Name == name);

        NostrEvent signed = NostrEvents.Sign(SecretKey.FromHex(secretHex), v.CreatedAt, v.Kind, v.Tags, v.Content, Convert.FromHexString(v.Aux));

        Assert.Equal(pubkeyHex, signed.Pubkey);
        Assert.Equal(v.Id, signed.Id);
        Assert.Equal(v.Sig, signed.Sig); // byte-identical to what the BIP-340 reference produced, so the reference verifies it
        Assert.True(NostrEvents.Verify(signed));
    }

    [Theory]
    [InlineData("spike-csharp-events.jsonl")]
    [InlineData("spike-python-events.jsonl")]
    public void Verify_AcceptsTheSpikesIndependentlyVerifiedEvents(string file)
    {
        Assert.All(Nip01VectorFiles.SignedEvents(file), e => Assert.True(NostrEvents.Verify(e), e.Content));
    }

    [Fact]
    public void Sign_SetsKindTagsContentAndCreatedAt_AndCopiesTheTags()
    {
        List<List<string>> tags = [["p", Bob.PublicKey.ToHex()]];

        NostrEvent e = NostrEvents.Sign(Alice, 42, 3825, tags, "c", new byte[32]);
        tags[0][1] = "changed";

        Assert.Equal(42, e.CreatedAt);
        Assert.Equal(3825, e.Kind);
        Assert.Equal("c", e.Content);
        Assert.Equal(Bob.PublicKey.ToHex(), e.Tags[0][1]);
        Assert.True(NostrEvents.Verify(e));
    }

    [Fact]
    public void FlippedIdCharacter_FailsVerification()
    {
        NostrEvent e = Copy(SignedByAlice());
        e.Id = (e.Id[0] == '0' ? "1" : "0") + e.Id[1..];

        Assert.False(NostrEvents.Verify(e));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(127)]
    public void FlippedSignatureBit_FailsVerification(int hexIndex)
    {
        NostrEvent e = Copy(SignedByAlice());
        char[] sig = e.Sig.ToCharArray();
        int nibble = Convert.ToInt32(sig[hexIndex].ToString(), 16) ^ 1;
        sig[hexIndex] = "0123456789abcdef"[nibble];
        e.Sig = new string(sig);

        Assert.False(NostrEvents.Verify(e));
    }

    [Theory]
    [InlineData("content")]
    [InlineData("created_at")]
    [InlineData("kind")]
    [InlineData("tag")]
    [InlineData("pubkey")]
    public void TamperedField_FailsVerification(string field)
    {
        NostrEvent e = Copy(SignedByAlice());
        switch (field)
        {
            case "content": e.Content += "x"; break;
            case "created_at": e.CreatedAt += 1; break;
            case "kind": e.Kind = 1; break;
            case "tag": e.Tags[0][1] = Alice.PublicKey.ToHex(); break;
            case "pubkey": e.Pubkey = Bob.PublicKey.ToHex(); break;
        }

        Assert.False(NostrEvents.Verify(e));
    }

    [Theory]
    [InlineData(62)]  // 31 bytes
    [InlineData(66)]  // 33 bytes
    [InlineData(0)]
    public void PubkeyOfWrongLength_FailsVerification(int hexLength)
    {
        NostrEvent e = Copy(SignedByAlice());
        e.Pubkey = (e.Pubkey + e.Pubkey)[..hexLength];

        Assert.False(NostrEvents.Verify(e));
    }

    [Fact]
    public void UppercasePubkeyOrId_FailsVerification()
    {
        NostrEvent upperPub = Copy(SignedByAlice());
        upperPub.Pubkey = upperPub.Pubkey.ToUpperInvariant();
        NostrEvent upperId = Copy(SignedByAlice());
        upperId.Id = upperId.Id.ToUpperInvariant();

        Assert.False(NostrEvents.Verify(upperPub));
        Assert.False(NostrEvents.Verify(upperId));
    }

    [Fact]
    public void OffCurvePubkey_FailsVerification()
    {
        NostrEvent e = Copy(SignedByAlice());
        e.Pubkey = "eefdea4cdb677750a420fee807eacf21eb9898ae79b9768766e4faa04a2d4a34";

        Assert.False(NostrEvents.Verify(e));
    }

    [Theory]
    [InlineData("""{"id":null,"pubkey":"a","created_at":1,"kind":1,"tags":[],"content":"","sig":"b"}""")]
    [InlineData("""{"pubkey":"a","created_at":1,"kind":1,"tags":[],"content":""}""")]
    [InlineData("""{"id":"00","pubkey":null,"created_at":1,"kind":1,"tags":null,"content":null,"sig":null}""")]
    [InlineData("""{"id":"zz","pubkey":"zz","created_at":1,"kind":1,"tags":[["p",null]],"content":"","sig":"zz"}""")]
    public void RelayJunk_FailsVerification_WithoutThrowing(string json)
    {
        NostrEvent? e = JsonSerializer.Deserialize<NostrEvent>(json);

        Assert.False(NostrEvents.Verify(e));
    }

    [Fact]
    public void NullTagValue_InAnOtherwiseValidEvent_FailsVerification()
    {
        NostrEvent e = Copy(SignedByAlice());
        e.Tags[1][1] = null!;

        Assert.False(NostrEvents.Verify(e));
    }

    [Fact]
    public void Sign_RejectsNullArguments()
    {
        Assert.Throws<ArgumentNullException>(() => NostrEvents.Sign(null!, 1, 1, [], "c", new byte[32]));
        Assert.Throws<ArgumentNullException>(() => NostrEvents.Sign(Alice, 1, 1, null!, "c", new byte[32]));
        Assert.Throws<ArgumentNullException>(() => NostrEvents.Sign(Alice, 1, 1, [], null!, new byte[32]));
    }

    [Fact]
    public void NonHexSignature_FailsVerification()
    {
        NostrEvent e = Copy(SignedByAlice());
        e.Sig = new string('z', 128);

        Assert.False(NostrEvents.Verify(e));
        Assert.False(NostrEvents.Verify(null));
    }
}
```

In `tests/TakEngine.Transport.Tests/NostrMessageTests.cs` move the one existing id test onto the serializer:
```diff
diff --git a/tests/TakEngine.Transport.Tests/NostrMessageTests.cs b/tests/TakEngine.Transport.Tests/NostrMessageTests.cs
index a80ae20..2267ac7 100644
--- a/tests/TakEngine.Transport.Tests/NostrMessageTests.cs
+++ b/tests/TakEngine.Transport.Tests/NostrMessageTests.cs
@@ -10,7 +10,7 @@ namespace TakEngine.Transport.Tests;
 public class NostrMessageTests
 {
     [Fact]
-    public void NostrEvent_ComputeId_ReturnsSha256Hex()
+    public void Nip01Serializer_ComputeId_ReturnsSha256Hex()
     {
         var evt = new NostrEvent
         {
@@ -21,7 +21,7 @@ public class NostrMessageTests
             Content = "test content"
         };
 
-        string id = evt.ComputeId();
+        string id = Nip01Serializer.ComputeId(evt);
 
         Assert.Equal(64, id.Length);
         Assert.Matches("^[0-9a-f]{64}$", id);
```

- [ ] **Step 3: Run the tests to see them fail**

```bash
export MSBuildEnableWorkloadResolver=false
dotnet test tests/TakEngine.Transport.Tests
```
Expected: build FAILS with `error CS0103: The name 'Nip01Serializer' does not exist` and `'NostrEvents' does not exist`.

- [ ] **Step 4: Implement the serializer and sign/verify**

`src/TakEngine.Transport/Nostr/Nip01Serializer.cs` (escaping copied from the spike's `NostrEventLite.Serialize`, plus invariant-culture numbers):
```csharp
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace TakEngine.Transport.Nostr;

/// <summary>
/// The one NIP-01 event serializer: <c>[0,pubkey,created_at,kind,tags,content]</c>, compact, UTF-8. Hand-written on
/// purpose (pipeline/architecture.md "Wire format", docs/decisions/0004): System.Text.Json escapes + &lt; &gt; &amp;, DEL and
/// astral characters even with relaxed escaping, so its ids differ from the ids relays compute.
/// Escapes: \" \\ \n \r \t \b \f; every other character below U+0020 as \u00xx (lowercase hex); everything else verbatim.
/// </summary>
public static class Nip01Serializer
{
    /// <summary>Throws <see cref="ArgumentException"/> if pubkey, content, tags or any tag value is null.</summary>
    public static string Serialize(string pubkey, long createdAt, int kind, IReadOnlyList<IReadOnlyList<string>> tags, string content)
    {
        ArgumentNullException.ThrowIfNull(pubkey);
        ArgumentNullException.ThrowIfNull(tags);
        ArgumentNullException.ThrowIfNull(content);

        var sb = new StringBuilder(128 + content.Length);
        sb.Append("[0,");
        AppendString(sb, pubkey);
        sb.Append(',').Append(createdAt.ToString(CultureInfo.InvariantCulture));
        sb.Append(',').Append(kind.ToString(CultureInfo.InvariantCulture));
        sb.Append(",[");
        for (int i = 0; i < tags.Count; i++)
        {
            IReadOnlyList<string> tag = tags[i] ?? throw new ArgumentException($"Tag {i} is null.", nameof(tags));
            if (i > 0)
                sb.Append(',');
            sb.Append('[');
            for (int j = 0; j < tag.Count; j++)
            {
                if (j > 0)
                    sb.Append(',');
                AppendString(sb, tag[j] ?? throw new ArgumentException($"Tag {i} value {j} is null.", nameof(tags)));
            }
            sb.Append(']');
        }
        sb.Append("],");
        AppendString(sb, content);
        sb.Append(']');
        return sb.ToString();
    }

    public static string Serialize(NostrEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);
        return Serialize(evt.Pubkey, evt.CreatedAt, evt.Kind, evt.Tags, evt.Content);
    }

    /// <summary>SHA-256 of the UTF-8 serialization: the 32-byte event id that BIP-340 signs.</summary>
    public static byte[] ComputeIdBytes(string pubkey, long createdAt, int kind, IReadOnlyList<IReadOnlyList<string>> tags, string content) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(Serialize(pubkey, createdAt, kind, tags, content)));

    /// <summary>The event id as 64 lowercase hex characters.</summary>
    public static string ComputeId(NostrEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);
        return Convert.ToHexStringLower(ComputeIdBytes(evt.Pubkey, evt.CreatedAt, evt.Kind, evt.Tags, evt.Content));
    }

    private static void AppendString(StringBuilder sb, string value)
    {
        sb.Append('"');
        foreach (char c in value)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                default:
                    if (c < ' ')
                        sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else
                        sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
    }
}
```

`src/TakEngine.Transport/Nostr/NostrEvents.cs`:
```csharp
using System.Buffers;
using TakEngine.Crypto;

namespace TakEngine.Transport.Nostr;

/// <summary>
/// Signing and verification of Nostr events (NIP-01 id + BIP-340 sig). Pure: <c>created_at</c> and the BIP-340 aux
/// randomness are arguments. Every received event goes through <see cref="Verify"/> before anything else is done with it.
/// </summary>
public static class NostrEvents
{
    /// <summary>Builds a signed event: pubkey = <paramref name="key"/>'s x-only hex, id = NIP-01 id, sig = BIP-340 over the id.</summary>
    public static NostrEvent Sign(
        SecretKey key,
        long createdAt,
        int kind,
        IReadOnlyList<IReadOnlyList<string>> tags,
        string content,
        ReadOnlySpan<byte> aux32)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(tags);
        ArgumentNullException.ThrowIfNull(content);

        string pubkey = key.PublicKey.ToHex();
        byte[] id = Nip01Serializer.ComputeIdBytes(pubkey, createdAt, kind, tags, content);
        byte[] sig = Schnorr.Sign(key, id, aux32);
        return new NostrEvent
        {
            Id = Convert.ToHexStringLower(id),
            Pubkey = pubkey,
            CreatedAt = createdAt,
            Kind = kind,
            Tags = tags.Select(tag => tag.ToList()).ToList(),
            Content = content,
            Sig = Convert.ToHexStringLower(sig),
        };
    }

    /// <summary>
    /// True only if <c>id</c> is the NIP-01 id of the event's fields (64 lowercase hex), <c>pubkey</c> is a valid 32-byte
    /// x-only key in lowercase hex, and <c>sig</c> is a valid BIP-340 signature over the id. Never throws: relay junk returns false.
    /// </summary>
    public static bool Verify(NostrEvent? evt)
    {
        if (evt is null || evt.Id is null || evt.Pubkey is null || evt.Sig is null || evt.Content is null || evt.Tags is null)
            return false;
        if (evt.Tags.Any(tag => tag is null || tag.Any(value => value is null)))
            return false;
        if (!PublicKey.TryFromHex(evt.Pubkey, out PublicKey? author) || !string.Equals(author.ToHex(), evt.Pubkey, StringComparison.Ordinal))
            return false; // not a valid 32-byte x-only key in lowercase hex (NIP-01)

        byte[] id = Nip01Serializer.ComputeIdBytes(evt.Pubkey, evt.CreatedAt, evt.Kind, evt.Tags, evt.Content);
        if (!string.Equals(Convert.ToHexStringLower(id), evt.Id, StringComparison.Ordinal))
            return false;

        byte[] sig = new byte[Schnorr.SignatureLength];
        if (evt.Sig.Length != Schnorr.SignatureLength * 2 || Convert.FromHexString(evt.Sig, sig, out _, out _) != OperationStatus.Done)
            return false;
        return Schnorr.Verify(author, id, sig);
    }
}
```

- [ ] **Step 5: Run the tests to see them pass**

```bash
dotnet test tests/TakEngine.Transport.Tests --filter "FullyQualifiedName~Nip01|FullyQualifiedName~NostrEvents|FullyQualifiedName~NostrMessageTests"
```
Expected: `Passed!`, 0 failed.

- [ ] **Step 6: Delete the STJ-based `NostrEvent.ComputeId` and move its callers to the serializer**

Apply:
```diff
diff --git a/src/TakEngine.Transport/Nostr/NostrModels.cs b/src/TakEngine.Transport/Nostr/NostrModels.cs
index b4bcf3a..976fce2 100644
--- a/src/TakEngine.Transport/Nostr/NostrModels.cs
+++ b/src/TakEngine.Transport/Nostr/NostrModels.cs
@@ -29,38 +29,6 @@ public sealed class NostrEvent
 
     [JsonPropertyName("sig")]
     public string Sig { get; set; } = "";
-
-    public string ComputeId()
-    {
-        // NIP-01 serialized event: [0, pubkey, created_at, kind, tags, content]
-        using var stream = new System.IO.MemoryStream();
-        using var writer = new Utf8JsonWriter(stream);
-
-        writer.WriteStartArray();
-        writer.WriteNumberValue(0);
-        writer.WriteStringValue(Pubkey);
-        writer.WriteNumberValue(CreatedAt);
-        writer.WriteNumberValue(Kind);
-
-        writer.WriteStartArray();
-        foreach (var tag in Tags)
-        {
-            writer.WriteStartArray();
-            foreach (var item in tag)
-            {
-                writer.WriteStringValue(item);
-            }
-            writer.WriteEndArray();
-        }
-        writer.WriteEndArray();
-
-        writer.WriteStringValue(Content);
-        writer.WriteEndArray();
-        writer.Flush();
-
-        byte[] hash = SHA256.HashData(stream.ToArray());
-        return Convert.ToHexStringLower(hash);
-    }
 }
 
 public sealed class NostrFilter
```
```diff
diff --git a/src/TakEngine.Transport/Matchmaking/QuickPlayMatchmaker.cs b/src/TakEngine.Transport/Matchmaking/QuickPlayMatchmaker.cs
index 2eecf91..5be07af 100644
--- a/src/TakEngine.Transport/Matchmaking/QuickPlayMatchmaker.cs
+++ b/src/TakEngine.Transport/Matchmaking/QuickPlayMatchmaker.cs
@@ -46,7 +46,7 @@ public sealed class QuickPlayMatchmaker
             Content = content
         };
 
-        evt.Id = evt.ComputeId();
+        evt.Id = Nip01Serializer.ComputeId(evt);
         return evt;
     }
```
```diff
diff --git a/src/TakEngine.Transport/Nostr/NostrProfile.cs b/src/TakEngine.Transport/Nostr/NostrProfile.cs
index ad11df6..f951d33 100644
--- a/src/TakEngine.Transport/Nostr/NostrProfile.cs
+++ b/src/TakEngine.Transport/Nostr/NostrProfile.cs
@@ -40,7 +40,7 @@ public sealed record NostrProfile(
             Tags = new(),
             Content = json
         };
-        evt.Id = evt.ComputeId();
+        evt.Id = Nip01Serializer.ComputeId(evt);
         return evt;
     }
 }
```
```diff
diff --git a/src/TakEngine.Transport/Nostr/NostrTransportClient.cs b/src/TakEngine.Transport/Nostr/NostrTransportClient.cs
index 8ba22fc..ac236c9 100644
--- a/src/TakEngine.Transport/Nostr/NostrTransportClient.cs
+++ b/src/TakEngine.Transport/Nostr/NostrTransportClient.cs
@@ -117,7 +117,7 @@ public sealed class NostrTransportClient : IAsyncDisposable
             ],
             Content = encryptedContent
         };
-        evt.Id = evt.ComputeId();
+        evt.Id = Nip01Serializer.ComputeId(evt);
 
         int publishedCount = 0;
         foreach (var relay in _relays)
```

```bash
git grep -n "\.ComputeId()" -- src tests   # expected: no output
```

- [ ] **Step 7: Run the four checks**

```bash
export MSBuildEnableWorkloadResolver=false
dotnet restore TakGame.Ci.slnf
dotnet build TakGame.Ci.slnf --no-restore
dotnet format TakGame.Ci.slnf --verify-no-changes --no-restore
dotnet test TakGame.Ci.slnf --no-build --filter "Category!=LiveRelay"
```
Expected: all exit 0.

- [ ] **Step 8: Commit**

```bash
git add -A src/TakEngine.Transport tests/TakEngine.Transport.Tests
git commit -m "M0 F-032: hand-written NIP-01 serializer and BIP-340 event sign/verify" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: F-031 (part 2) — persisted identity: `IdentityDocument`, `IKeyStore`, `FileKeyStore`

**Files:**
- Create: `src/TakEngine.Crypto/IdentityDocument.cs`, `tests/TakEngine.Crypto.Tests/IdentityDocumentTests.cs`
- Create: `src/TakEngine.Abstractions/IKeyStore.cs`
- Create (new project): `src/TakEngine.Storage.Local/TakEngine.Storage.Local.csproj`, `FileKeyStore.cs`, `IdentityBootstrap.cs`
- Create (new project): `tests/TakEngine.Storage.Tests/TakEngine.Storage.Tests.csproj`, `FileKeyStoreTests.cs`
- Modify: `TakGame.sln`, `TakGame.slnx`, `TakGame.Ci.slnf`

**Interfaces:**
- Consumes: `SecretKey.FromNsec`, `SecretKey.FromBytes`, `SecretKey.Generate`, `SecretKey.ToNsec()`, `SecretKey.ToBytes()`, `InvalidKeyException`, `Nip19.Encode` (Task 2).
- Produces: `IdentityDocument.Serialize(SecretKey)`/`Parse(string)`, `IdentityFormatException`; `TakEngine.Abstractions.IKeyStore` (`Task<byte[]?> LoadSecretAsync(CancellationToken = default)`, `Task SaveNewSecretAsync(byte[] secret32, CancellationToken = default)`), `KeyStoreException`; `TakEngine.Storage.Local.FileKeyStore(string dataDirectory)` with `FileName = "identity.json"` and `FilePath`; `IdentityBootstrap.LoadOrCreateAsync(IKeyStore store, Func<byte[]> random32, CancellationToken = default) : Task<SecretKey>`. `IKeyStore` deals in raw 32-byte secrets because Abstractions cannot reference Crypto (Crypto references Abstractions).

- [ ] **Step 1: Create the Storage projects and register them**

`src/TakEngine.Storage.Local/TakEngine.Storage.Local.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <!-- Local disk for CLI and desktop (pipeline/architecture.md "Modules"). Never referenced by the browser build. -->

  <ItemGroup>
    <ProjectReference Include="..\TakEngine.Abstractions\TakEngine.Abstractions.csproj" />
    <ProjectReference Include="..\TakEngine.Crypto\TakEngine.Crypto.csproj" />
  </ItemGroup>

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

</Project>
```

`tests/TakEngine.Storage.Tests/TakEngine.Storage.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.4" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\TakEngine.Storage.Local\TakEngine.Storage.Local.csproj" />
  </ItemGroup>

</Project>
```

```bash
export MSBuildEnableWorkloadResolver=false
dotnet sln TakGame.sln add src/TakEngine.Storage.Local/TakEngine.Storage.Local.csproj --solution-folder src
dotnet sln TakGame.sln add tests/TakEngine.Storage.Tests/TakEngine.Storage.Tests.csproj --solution-folder tests
dotnet sln TakGame.slnx add src/TakEngine.Storage.Local/TakEngine.Storage.Local.csproj --solution-folder src
dotnet sln TakGame.slnx add tests/TakEngine.Storage.Tests/TakEngine.Storage.Tests.csproj --solution-folder tests
```
Replace `TakGame.Ci.slnf` with:
```json
{
  "solution": {
    "path": "TakGame.sln",
    "projects": [
      "src/TakEngine.Abstractions/TakEngine.Abstractions.csproj",
      "src/TakEngine.Crypto/TakEngine.Crypto.csproj",
      "src/TakEngine.Core/TakEngine.Core.csproj",
      "src/TakEngine.Transport/TakEngine.Transport.csproj",
      "src/TakEngine.Storage.Local/TakEngine.Storage.Local.csproj",
      "src/TakApp.Cli/TakApp.Cli.csproj",
      "src/TakApp.Blazor/TakApp.Blazor.csproj",
      "src/TakApp.Avalonia/TakApp.Avalonia.csproj",
      "src/TakApp.Avalonia.Desktop/TakApp.Avalonia.Desktop.csproj",
      "tests/TakEngine.Core.Tests/TakEngine.Core.Tests.csproj",
      "tests/TakEngine.Crypto.Tests/TakEngine.Crypto.Tests.csproj",
      "tests/TakEngine.Storage.Tests/TakEngine.Storage.Tests.csproj",
      "tests/TakEngine.Transport.Tests/TakEngine.Transport.Tests.csproj"
    ]
  }
}
```

- [ ] **Step 2: Write the failing tests**

`tests/TakEngine.Crypto.Tests/IdentityDocumentTests.cs`:
```csharp
namespace TakEngine.Crypto.Tests;

/// <summary>F-031: the versioned identity blob {"v":1,"nsec":"nsec1…"} and its specific errors.</summary>
public class IdentityDocumentTests
{
    private const string SpecNsec = "nsec1vl029mgpspedva04g90vltkh6fvh240zqtv9k0t9af8935ke9laqsnlfe5";
    private const string SpecNsecHex = "67dea2ed018072d675f5415ecfaed7d2597555e202d85b3d65ea4e58d2d92ffa";

    [Fact]
    public void Serialize_WritesVersionAndNsec()
    {
        Assert.Equal(
            "{\"v\":1,\"nsec\":\"" + SpecNsec + "\"}",
            IdentityDocument.Serialize(SecretKey.FromHex(SpecNsecHex)));
    }

    [Fact]
    public void Parse_ReadsWhatSerializeWrote()
    {
        var key = SecretKey.FromHex(SpecNsecHex);

        Assert.Equal(SpecNsecHex, IdentityDocument.Parse(IdentityDocument.Serialize(key)).ToHex());
    }

    [Fact]
    public void Parse_IgnoresWhitespaceAndUnknownFields()
    {
        var key = IdentityDocument.Parse("{ \"nsec\" : \"" + SpecNsec + "\", \"v\": 1, \"note\": \"x\" }\n");

        Assert.Equal(SpecNsecHex, key.ToHex());
    }

    [Theory]
    [InlineData("", "not valid JSON")]
    [InlineData("nsec1vl029mgpspedva04g90vltkh6fvh240zqtv9k0t9af8935ke9laqsnlfe5", "not valid JSON")]
    [InlineData("[1]", "not a JSON object")]
    [InlineData("{\"nsec\":\"" + SpecNsec + "\"}", "no integer version")]
    [InlineData("{\"v\":\"1\",\"nsec\":\"" + SpecNsec + "\"}", "no integer version")]
    [InlineData("{\"v\":2,\"nsec\":\"" + SpecNsec + "\"}", "newer version (v=2)")]
    [InlineData("{\"v\":0,\"nsec\":\"" + SpecNsec + "\"}", "Unknown identity version v=0")]
    [InlineData("{\"v\":1}", "no \"nsec\"")]
    [InlineData("{\"v\":1,\"nsec\":42}", "no \"nsec\"")]
    [InlineData("{\"v\":1,\"nsec\":\"npub10elfcs4fr0l0r8af98jlmgdh9c8tcxjvz9qkw038js35mp4dma8qzvjptg\"}", "Expected an nsec")]
    [InlineData("{\"v\":1,\"nsec\":\"nsec1vl029mgpspedva04g90vltkh6fvh240zqtv9k0t9af8935ke9laqsnlfe6\"}", "checksum")]
    public void Parse_RejectsBadDocuments_WithASpecificMessage(string document, string expectedMessagePart)
    {
        var ex = Assert.Throws<IdentityFormatException>(() => IdentityDocument.Parse(document));
        Assert.Contains(expectedMessagePart, ex.Message);
    }

    [Fact]
    public void NullArguments_AreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => IdentityDocument.Serialize(null!));
        Assert.Throws<ArgumentNullException>(() => IdentityDocument.Parse(null!));
    }

    [Fact]
    public void Parse_RejectsAnNsecOfTheWrongLength()
    {
        string document = "{\"v\":1,\"nsec\":\"" + Nip19.Encode("nsec", new byte[31]) + "\"}";

        var ex = Assert.Throws<IdentityFormatException>(() => IdentityDocument.Parse(document));
        Assert.Contains("32 bytes, got 31", ex.Message);
    }

    [Fact]
    public void Parse_RejectsAnOutOfRangeScalar()
    {
        byte[] curveOrder = Convert.FromHexString("fffffffffffffffffffffffffffffffebaaedce6af48a03bbfd25e8cd0364141");
        string document = "{\"v\":1,\"nsec\":\"" + Nip19.Encode("nsec", curveOrder) + "\"}";

        var ex = Assert.Throws<IdentityFormatException>(() => IdentityDocument.Parse(document));
        Assert.Contains("out of range", ex.Message);
    }
}
```

`tests/TakEngine.Storage.Tests/FileKeyStoreTests.cs` (a "restart" is a new `FileKeyStore` over the same directory):
```csharp
using TakEngine.Abstractions;
using TakEngine.Crypto;
using TakEngine.Storage.Local;

namespace TakEngine.Storage.Tests;

/// <summary>F-031: the key is generated once, persisted, reloaded after a restart, and a bad key file is reported, never overwritten.</summary>
public sealed class FileKeyStoreTests : IDisposable
{
    private const string SpecNsecHex = "67dea2ed018072d675f5415ecfaed7d2597555e202d85b3d65ea4e58d2d92ffa";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tak-keystore-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private string IdentityPath => Path.Combine(_dir, FileKeyStore.FileName);

    private static Func<byte[]> FixedRandom(string hex) => () => Convert.FromHexString(hex);

    [Fact]
    public async Task FirstRun_GeneratesAndSaves_AndARestartLoadsTheSameKey()
    {
        SecretKey first = await IdentityBootstrap.LoadOrCreateAsync(new FileKeyStore(_dir), FixedRandom(SpecNsecHex));

        // "Restart": a new store instance over the same directory, with a random source that must not be used.
        SecretKey second = await IdentityBootstrap.LoadOrCreateAsync(
            new FileKeyStore(_dir), () => throw new InvalidOperationException("must not generate on restart"));

        Assert.Equal(SpecNsecHex, first.ToHex());
        Assert.Equal(first.ToHex(), second.ToHex());
        Assert.Equal(first.PublicKey, second.PublicKey);
    }

    [Fact]
    public async Task SavedFile_IsTheVersionedNsecDocument()
    {
        await IdentityBootstrap.LoadOrCreateAsync(new FileKeyStore(_dir), FixedRandom(SpecNsecHex));

        Assert.Equal(
            "{\"v\":1,\"nsec\":\"nsec1vl029mgpspedva04g90vltkh6fvh240zqtv9k0t9af8935ke9laqsnlfe5\"}",
            await File.ReadAllTextAsync(IdentityPath));
        Assert.Single(Directory.GetFiles(_dir)); // no temp file left behind
    }

    [Fact]
    public async Task NoFile_LoadsNull()
    {
        Assert.Null(await new FileKeyStore(_dir).LoadSecretAsync());
    }

    public static TheoryData<string, string> BadFiles() => new()
    {
        { "{\"v\":1,\"nsec\":\"" + Nip19.Encode("nsec", new byte[31]) + "\"}", "32 bytes, got 31" },
        { "{\"v\":1,\"nsec\":\"" + Nip19.Encode("nsec", new byte[33]) + "\"}", "32 bytes, got 33" },
        { "{\"v\":1,\"nsec\":\"" + Nip19.Encode("nsec", Convert.FromHexString("fffffffffffffffffffffffffffffffebaaedce6af48a03bbfd25e8cd0364141")) + "\"}", "out of range" },
        { "{\"v\":1,\"nsec\":\"" + Nip19.Encode("nsec", new byte[32]) + "\"}", "out of range" },
        { "{\"v\":2,\"nsec\":\"nsec1vl029mgpspedva04g90vltkh6fvh240zqtv9k0t9af8935ke9laqsnlfe5\"}", "newer version" },
        { "not json", "not valid JSON" },
        { "", "not valid JSON" },
    };

    [Theory]
    [MemberData(nameof(BadFiles))]
    public async Task BadKeyFile_GivesAClearError_AndIsNotOverwritten(string content, string expectedMessagePart)
    {
        Directory.CreateDirectory(_dir);
        await File.WriteAllTextAsync(IdentityPath, content);
        byte[] before = await File.ReadAllBytesAsync(IdentityPath);

        var loadError = await Assert.ThrowsAsync<KeyStoreException>(() => new FileKeyStore(_dir).LoadSecretAsync());
        var bootstrapError = await Assert.ThrowsAsync<KeyStoreException>(
            () => IdentityBootstrap.LoadOrCreateAsync(new FileKeyStore(_dir), FixedRandom(SpecNsecHex)));

        Assert.Contains(expectedMessagePart, loadError.Message);
        Assert.Contains(IdentityPath, loadError.Message);
        Assert.Contains(expectedMessagePart, bootstrapError.Message);
        Assert.Equal(before, await File.ReadAllBytesAsync(IdentityPath));
        Assert.Single(Directory.GetFiles(_dir));
    }

    [Fact]
    public async Task SaveNewSecret_RefusesToOverwriteAnExistingIdentity()
    {
        var store = new FileKeyStore(_dir);
        await store.SaveNewSecretAsync(Convert.FromHexString(SpecNsecHex));
        byte[] before = await File.ReadAllBytesAsync(IdentityPath);

        var ex = await Assert.ThrowsAsync<KeyStoreException>(
            () => store.SaveNewSecretAsync(SecretKey.FromHex("0000000000000000000000000000000000000000000000000000000000000003").ToBytes()));

        Assert.Contains("never overwritten", ex.Message);
        Assert.Equal(before, await File.ReadAllBytesAsync(IdentityPath));
    }

    [Fact]
    public async Task SaveNewSecret_RejectsAnInvalidSecret_AndWritesNothing()
    {
        await Assert.ThrowsAsync<InvalidKeyException>(() => new FileKeyStore(_dir).SaveNewSecretAsync(new byte[32]));

        Assert.False(File.Exists(IdentityPath));
    }
}
```

- [ ] **Step 3: Run the tests to see them fail**

```bash
dotnet test tests/TakEngine.Crypto.Tests
dotnet test tests/TakEngine.Storage.Tests
```
Expected: both builds FAIL: `CS0103 'IdentityDocument' does not exist`, `CS0246 'IdentityFormatException' / 'FileKeyStore' / 'KeyStoreException' could not be found`.

- [ ] **Step 4: Implement**

`src/TakEngine.Crypto/IdentityDocument.cs`:
```csharp
using System.Text.Json;

namespace TakEngine.Crypto;

/// <summary>The stored identity could not be read. The message names the reason; callers never overwrite the stored data.</summary>
public sealed class IdentityFormatException(string message, Exception? innerException = null)
    : FormatException(message, innerException);

/// <summary>
/// The versioned identity blob (pipeline/architecture.md "Data", docs/decisions/0010): <c>{"v":1,"nsec":"nsec1…"}</c>.
/// Same format for the CLI/desktop key file and the browser's <c>tak.identity.v1</c>. Forward versions only:
/// a higher <c>v</c> than this build knows is an error, never rewritten.
/// </summary>
public static class IdentityDocument
{
    public const int CurrentVersion = 1;

    public static string Serialize(SecretKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return $"{{\"v\":{CurrentVersion},\"nsec\":\"{key.ToNsec()}\"}}";
    }

    public static SecretKey Parse(string document)
    {
        ArgumentNullException.ThrowIfNull(document);
        JsonElement root = TryParseJson(document) ?? throw new IdentityFormatException("Identity is not valid JSON.");
        if (root.ValueKind != JsonValueKind.Object)
            throw new IdentityFormatException("Identity is not a JSON object.");

        int version = ReadVersion(root);
        if (version > CurrentVersion)
            throw new IdentityFormatException(
                $"Identity was written by a newer version (v={version}); this build reads v={CurrentVersion}.");
        if (version < CurrentVersion)
            throw new IdentityFormatException($"Unknown identity version v={version}.");

        if (!root.TryGetProperty("nsec", out JsonElement nsec) || nsec.ValueKind != JsonValueKind.String)
            throw new IdentityFormatException("Identity has no \"nsec\".");
        return KeyFromNsec(nsec.GetString()!);
    }

    private static JsonElement? TryParseJson(string document)
    {
        try
        {
            using JsonDocument json = JsonDocument.Parse(document);
            return json.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int ReadVersion(JsonElement root)
    {
        if (root.TryGetProperty("v", out JsonElement v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int version))
            return version;
        throw new IdentityFormatException("Identity has no integer version \"v\".");
    }

    private static SecretKey KeyFromNsec(string nsec)
    {
        try
        {
            return SecretKey.FromNsec(nsec);
        }
        catch (InvalidKeyException ex)
        {
            throw new IdentityFormatException($"Identity key is invalid: {ex.Message}", ex);
        }
    }
}
```

`src/TakEngine.Abstractions/IKeyStore.cs`:
```csharp
using System;
using System.Threading;
using System.Threading.Tasks;

namespace TakEngine.Abstractions;

/// <summary>
/// Seam for the one persisted player secret (D-011). Real: FileKeyStore (CLI/desktop), BrowserKeyStore (M1+).
/// Implementations validate what they read and never overwrite an existing identity.
/// </summary>
public interface IKeyStore
{
    /// <summary>
    /// The stored 32-byte secret, or null when no identity has been saved yet.
    /// Throws <see cref="KeyStoreException"/> (specific message) when an identity exists but cannot be read; the stored data is left untouched.
    /// </summary>
    Task<byte[]?> LoadSecretAsync(CancellationToken cancellationToken = default);

    /// <summary>Saves a new identity. Throws <see cref="KeyStoreException"/> if one already exists: an identity is never overwritten.</summary>
    Task SaveNewSecretAsync(byte[] secret32, CancellationToken cancellationToken = default);
}

/// <summary>The stored identity is unreadable or an identity already exists. The message says which and where.</summary>
public sealed class KeyStoreException(string message, Exception? innerException = null) : Exception(message, innerException);
```

`src/TakEngine.Storage.Local/FileKeyStore.cs` (writes to a temp file, then `File.Move(..., overwrite: false)`, so an existing identity is never replaced even by a racing second process):
```csharp
using TakEngine.Abstractions;
using TakEngine.Crypto;

namespace TakEngine.Storage.Local;

/// <summary>
/// The CLI/desktop identity: <c>&lt;data-dir&gt;/identity.json</c> = <c>{"v":1,"nsec":"nsec1…"}</c> (IdentityDocument).
/// Reads validate fully; a file that cannot be read is reported and never rewritten; an existing file is never overwritten.
/// </summary>
public sealed class FileKeyStore : IKeyStore
{
    public const string FileName = "identity.json";

    public FileKeyStore(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        FilePath = Path.Combine(dataDirectory, FileName);
    }

    public string FilePath { get; }

    public async Task<byte[]?> LoadSecretAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(FilePath))
            return null;

        string document = await File.ReadAllTextAsync(FilePath, cancellationToken).ConfigureAwait(false);
        try
        {
            return IdentityDocument.Parse(document).ToBytes();
        }
        catch (IdentityFormatException ex)
        {
            throw new KeyStoreException($"Cannot read the identity in {FilePath}: {ex.Message} The file was left unchanged.", ex);
        }
    }

    public async Task SaveNewSecretAsync(byte[] secret32, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(secret32);
        SecretKey key = SecretKey.FromBytes(secret32);
        if (File.Exists(FilePath))
            throw new KeyStoreException($"An identity already exists in {FilePath}; it is never overwritten.");

        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        string temp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await File.WriteAllTextAsync(temp, IdentityDocument.Serialize(key), cancellationToken).ConfigureAwait(false);
        try
        {
            File.Move(temp, FilePath, overwrite: false);
        }
        catch (IOException ex)
        {
            File.Delete(temp);
            throw new KeyStoreException($"An identity already exists in {FilePath}; it is never overwritten.", ex);
        }
    }
}
```

`src/TakEngine.Storage.Local/IdentityBootstrap.cs`:
```csharp
using TakEngine.Abstractions;
using TakEngine.Crypto;

namespace TakEngine.Storage.Local;

/// <summary>Shell step 1 of the core-promise path: load the player's key, or create and save it on first run.</summary>
public static class IdentityBootstrap
{
    /// <summary>
    /// Returns the stored key; on first run generates one from <paramref name="random32"/> (the head passes
    /// <c>() =&gt; RandomNumberGenerator.GetBytes(32)</c>) and saves it. An unreadable store throws
    /// <see cref="KeyStoreException"/> and nothing is generated or written.
    /// </summary>
    public static async Task<SecretKey> LoadOrCreateAsync(IKeyStore store, Func<byte[]> random32, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(random32);

        byte[]? stored = await store.LoadSecretAsync(cancellationToken).ConfigureAwait(false);
        if (stored is not null)
            return SecretKey.FromBytes(stored);

        SecretKey created = SecretKey.Generate(random32);
        await store.SaveNewSecretAsync(created.ToBytes(), cancellationToken).ConfigureAwait(false);
        return created;
    }
}
```

- [ ] **Step 5: Run the tests to see them pass**

```bash
dotnet test tests/TakEngine.Crypto.Tests --filter "FullyQualifiedName~IdentityDocumentTests"
dotnet test tests/TakEngine.Storage.Tests
```
Expected: both `Passed!`, 0 failed.

- [ ] **Step 6: Run the four checks**

```bash
export MSBuildEnableWorkloadResolver=false
dotnet restore TakGame.Ci.slnf
dotnet build TakGame.Ci.slnf --no-restore
dotnet format TakGame.Ci.slnf --verify-no-changes --no-restore
dotnet test TakGame.Ci.slnf --no-build --filter "Category!=LiveRelay"
```
Expected: all exit 0; four test assemblies (Core, Crypto, Storage, Transport) print `Passed!`.

- [ ] **Step 7: Commit**

```bash
git add -A src/TakEngine.Crypto/IdentityDocument.cs tests/TakEngine.Crypto.Tests/IdentityDocumentTests.cs src/TakEngine.Abstractions/IKeyStore.cs src/TakEngine.Storage.Local tests/TakEngine.Storage.Tests TakGame.sln TakGame.slnx TakGame.Ci.slnf
git commit -m "M0 F-031: persisted identity file with FileKeyStore and IdentityBootstrap" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: F-031 (part 3) — delete the Ed25519 `CryptoSigner`; sessions and heads use secp256k1

**Files:**
- Create: `src/TakEngine.Core/Cryptography/PayloadSignature.cs`, `tests/TakEngine.Core.Tests/TestKeys.cs`, `tests/TakEngine.Core.Tests/PayloadSignatureTests.cs`
- Modify: `src/TakEngine.Core/Session/TakGameSession.cs`, `src/TakEngine.Core/Session/SpectatorGameSession.cs`, `src/TakEngine.Core/TakEngine.Core.csproj` (drop BouncyCastle), `tests/TakEngine.Core.Tests/CryptoTests.cs` (delete `KeyPair_GeneratesValidEd25519_AndSignsVerifies`), `tests/TakEngine.Core.Tests/SpectatorTests.cs`, `tests/TakEngine.Core.Tests/TakGameSessionTests.cs`, `src/TakApp.Cli/Program.cs`, `src/TakApp.Avalonia/ViewModels/MainViewModel.cs`, `src/TakApp.Blazor/Services/BrowserStorage.cs`
- Delete: `src/TakEngine.Core/Cryptography/CryptoSigner.cs` (also defines the `KeyPair` record)

**Interfaces:**
- Consumes: `SecretKey`, `PublicKey`, `Schnorr` (Tasks 2-3), `IdentityDocument` (Task 6).
- Produces: `PayloadSignature.Sign(SecretKey key, string payload) : string` (128 lowercase hex, deterministic: aux = 32 zero bytes, pure) and `PayloadSignature.Verify(string? publicKeyHex, string? payload, string? signatureHex) : bool` (never throws); `TakGameSession.CreateRemote(GameId id, BoardSize size, PlayerColor localColor, SecretKey localKey, PublicKey opponentPubKey)`. `ProcessRemoteMove(string playerPubKey, string prevStateHash, string ptnMove, string signature)` keeps its signature; it now verifies BIP-340 over SHA-256(UTF-8("{prevStateHash}:{ptnMove}")) until F-033 (A2) replaces the payload with `ActionDigest`.

- [ ] **Step 1: Write the failing tests**

`tests/TakEngine.Core.Tests/TestKeys.cs`:
```csharp
using System.Security.Cryptography;
using System.Text;
using TakEngine.Crypto;

namespace TakEngine.Core.Tests;

/// <summary>Deterministic, independent secp256k1 test keys (no unseeded randomness in tests).</summary>
internal static class TestKeys
{
    public static SecretKey Create(int n) => SecretKey.FromBytes(SHA256.HashData(Encoding.UTF8.GetBytes($"tak-p2p core test key {n}")));
}
```

`tests/TakEngine.Core.Tests/PayloadSignatureTests.cs` (replaces the deleted Ed25519 key test):
```csharp
using TakEngine.Core.Cryptography;
using TakEngine.Crypto;
using Xunit;

namespace TakEngine.Core.Tests;

/// <summary>F-031: session and spectator payloads are signed with the player's secp256k1 key (replaces the Ed25519 test).</summary>
public class PayloadSignatureTests
{
    private const string Payload = "4f2c…:a1";

    [Fact]
    public void SignatureByAlice_VerifiesUnderAliceOnly()
    {
        SecretKey alice = TestKeys.Create(1);
        SecretKey bob = TestKeys.Create(2);

        string signature = PayloadSignature.Sign(alice, Payload);

        Assert.Equal(128, signature.Length);
        Assert.True(PayloadSignature.Verify(alice.PublicKey.ToHex(), Payload, signature));
        Assert.False(PayloadSignature.Verify(bob.PublicKey.ToHex(), Payload, signature));
        Assert.False(PayloadSignature.Verify(alice.PublicKey.ToHex(), Payload + "x", signature));
    }

    [Fact]
    public void Sign_IsDeterministic()
    {
        Assert.Equal(PayloadSignature.Sign(TestKeys.Create(1), Payload), PayloadSignature.Sign(TestKeys.Create(1), Payload));
    }

    [Theory]
    [InlineData(null, Payload, "00")]
    [InlineData("abcd", Payload, "00")]
    [InlineData("eefdea4cdb677750a420fee807eacf21eb9898ae79b9768766e4faa04a2d4a34", Payload, "00")]
    [InlineData("VALIDKEY", null, "SIG")]
    [InlineData("VALIDKEY", Payload, null)]
    [InlineData("VALIDKEY", Payload, "bad_signature_hex")]
    [InlineData("VALIDKEY", Payload, "SIG_NOT_HEX")]
    public void Verify_ReturnsFalse_ForMalformedInput(string? publicKeyHex, string? payload, string? signatureHex)
    {
        SecretKey alice = TestKeys.Create(1);
        string validSignature = PayloadSignature.Sign(alice, Payload);
        publicKeyHex = publicKeyHex == "VALIDKEY" ? alice.PublicKey.ToHex() : publicKeyHex;
        signatureHex = signatureHex switch
        {
            "SIG" => validSignature,
            "SIG_NOT_HEX" => new string('z', 128),
            _ => signatureHex,
        };

        Assert.False(PayloadSignature.Verify(publicKeyHex, payload, signatureHex));
    }
}
```

Apply to the existing Core tests (removes the Ed25519 test and moves the session/spectator tests to independent secp256k1 keys):
```diff
diff --git a/tests/TakEngine.Core.Tests/CryptoTests.cs b/tests/TakEngine.Core.Tests/CryptoTests.cs
index 215ad61..fa3a44e 100644
--- a/tests/TakEngine.Core.Tests/CryptoTests.cs
+++ b/tests/TakEngine.Core.Tests/CryptoTests.cs
@@ -78,33 +78,4 @@ public class CryptoTests
         bool isValid = StateHasher.VerifyChain(tamperedChain, genesisHash);
         Assert.False(isValid);
     }
-
-    [Fact]
-    public void KeyPair_GeneratesValidEd25519_AndSignsVerifies()
-    {
-        var keyPair = CryptoSigner.GenerateKeyPair();
-
-        Assert.NotNull(keyPair.PublicKeyHex);
-        Assert.NotNull(keyPair.PrivateKeyHex);
-        Assert.Equal(64, keyPair.PublicKeyHex.Length); // 32 bytes hex
-        Assert.Equal(64, keyPair.PrivateKeyHex.Length); // 32 bytes hex
-
-        string message = "tak-move-payload-hash-3c3+12";
-        string signature = CryptoSigner.Sign(keyPair.PrivateKeyHex, message);
-
-        Assert.NotNull(signature);
-        Assert.Equal(128, signature.Length); // 64 bytes hex signature
-
-        bool verified = CryptoSigner.Verify(keyPair.PublicKeyHex, message, signature);
-        Assert.True(verified);
-
-        // Verification fails if message is altered
-        bool tampered = CryptoSigner.Verify(keyPair.PublicKeyHex, "tampered-payload", signature);
-        Assert.False(tampered);
-
-        // Verification fails with different key
-        var otherKeyPair = CryptoSigner.GenerateKeyPair();
-        bool wrongKey = CryptoSigner.Verify(otherKeyPair.PublicKeyHex, message, signature);
-        Assert.False(wrongKey);
-    }
 }
```
```diff
diff --git a/tests/TakEngine.Core.Tests/SpectatorTests.cs b/tests/TakEngine.Core.Tests/SpectatorTests.cs
index 9267d38..e34c0ac 100644
--- a/tests/TakEngine.Core.Tests/SpectatorTests.cs
+++ b/tests/TakEngine.Core.Tests/SpectatorTests.cs
@@ -4,6 +4,7 @@ using TakEngine.Abstractions;
 using TakEngine.Core.Cryptography;
 using TakEngine.Core.Serialization;
 using TakEngine.Core.Session;
+using TakEngine.Crypto;
 using Xunit;
 
 namespace TakEngine.Core.Tests;
@@ -70,15 +71,15 @@ public class SpectatorTests
     [Fact]
     public void SpectatorGameSession_IngestsValidMoves_AndAdvancesBoard()
     {
-        var whiteKeys = CryptoSigner.GenerateKeyPair();
-        var blackKeys = CryptoSigner.GenerateKeyPair();
+        var whiteKeys = TestKeys.Create(1);
+        var blackKeys = TestKeys.Create(2);
         var gameId = Guid.NewGuid();
 
         using var session = new SpectatorGameSession(
             gameId,
             BoardSize.Five,
-            whiteKeys.PublicKeyHex,
-            blackKeys.PublicKeyHex,
+            whiteKeys.PublicKey.ToHex(),
+            blackKeys.PublicKey.ToHex(),
             tournamentId: "swiss_round_1",
             whitePlayerElo: 1750,
             blackPlayerElo: 1810);
@@ -115,15 +116,15 @@ public class SpectatorTests
     [Fact]
     public void SpectatorGameSession_DetectsTamperedSignature_AndFiresDesync()
     {
-        var whiteKeys = CryptoSigner.GenerateKeyPair();
-        var blackKeys = CryptoSigner.GenerateKeyPair();
+        var whiteKeys = TestKeys.Create(1);
+        var blackKeys = TestKeys.Create(2);
         var gameId = Guid.NewGuid();
 
         using var session = new SpectatorGameSession(
             gameId,
             BoardSize.Five,
-            whiteKeys.PublicKeyHex,
-            blackKeys.PublicKeyHex);
+            whiteKeys.PublicKey.ToHex(),
+            blackKeys.PublicKey.ToHex());
 
         ProtocolViolationException? caughtViolation = null;
         session.OnStateDesyncDetected += ex => caughtViolation = ex;
@@ -132,7 +133,7 @@ public class SpectatorTests
         var env = new BroadcastEnvelope(
             gameId,
             1,
-            whiteKeys.PublicKeyHex,
+            whiteKeys.PublicKey.ToHex(),
             session.LastStateHash,
             DateTime.UtcNow,
             "a1",
@@ -147,15 +148,15 @@ public class SpectatorTests
     [Fact]
     public void SpectatorGameSession_DetectsBrokenHashChain_AndFiresDesync()
     {
-        var whiteKeys = CryptoSigner.GenerateKeyPair();
-        var blackKeys = CryptoSigner.GenerateKeyPair();
+        var whiteKeys = TestKeys.Create(1);
+        var blackKeys = TestKeys.Create(2);
         var gameId = Guid.NewGuid();
 
         using var session = new SpectatorGameSession(
             gameId,
             BoardSize.Five,
-            whiteKeys.PublicKeyHex,
-            blackKeys.PublicKeyHex);
+            whiteKeys.PublicKey.ToHex(),
+            blackKeys.PublicKey.ToHex());
 
         ProtocolViolationException? caughtViolation = null;
         session.OnStateDesyncDetected += ex => caughtViolation = ex;
@@ -172,16 +173,16 @@ public class SpectatorTests
     [Fact]
     public void SpectatorGameSession_DetectsWrongPlayerPubKey_AndFiresDesync()
     {
-        var whiteKeys = CryptoSigner.GenerateKeyPair();
-        var blackKeys = CryptoSigner.GenerateKeyPair();
-        var imposterKeys = CryptoSigner.GenerateKeyPair();
+        var whiteKeys = TestKeys.Create(1);
+        var blackKeys = TestKeys.Create(2);
+        var imposterKeys = TestKeys.Create(3);
         var gameId = Guid.NewGuid();
 
         using var session = new SpectatorGameSession(
             gameId,
             BoardSize.Five,
-            whiteKeys.PublicKeyHex,
-            blackKeys.PublicKeyHex);
+            whiteKeys.PublicKey.ToHex(),
+            blackKeys.PublicKey.ToHex());
 
         ProtocolViolationException? caughtViolation = null;
         session.OnStateDesyncDetected += ex => caughtViolation = ex;
@@ -198,15 +199,15 @@ public class SpectatorTests
     [Fact]
     public void SpectatorGameSession_DetectsIllegalMove_AndFiresDesync()
     {
-        var whiteKeys = CryptoSigner.GenerateKeyPair();
-        var blackKeys = CryptoSigner.GenerateKeyPair();
+        var whiteKeys = TestKeys.Create(1);
+        var blackKeys = TestKeys.Create(2);
         var gameId = Guid.NewGuid();
 
         using var session = new SpectatorGameSession(
             gameId,
             BoardSize.Four,
-            whiteKeys.PublicKeyHex,
-            blackKeys.PublicKeyHex);
+            whiteKeys.PublicKey.ToHex(),
+            blackKeys.PublicKey.ToHex());
 
         ProtocolViolationException? caughtViolation = null;
         session.OnStateDesyncDetected += ex => caughtViolation = ex;
@@ -223,7 +224,7 @@ public class SpectatorTests
     private static BroadcastEnvelope CreateSignedEnvelope(
         Guid gameId,
         int turnIndex,
-        KeyPair keys,
+        SecretKey keys,
         string prevStateHash,
         string ptnMove,
         DateTime timestamp)
@@ -231,14 +232,14 @@ public class SpectatorTests
         var dummy = new BroadcastEnvelope(
             gameId,
             turnIndex,
-            keys.PublicKeyHex,
+            keys.PublicKey.ToHex(),
             prevStateHash,
             timestamp,
             ptnMove,
             "");
 
         string payload = dummy.GetSigningPayload();
-        string signature = CryptoSigner.Sign(keys.PrivateKeyHex, payload);
+        string signature = PayloadSignature.Sign(keys, payload);
 
         return dummy with { Signature = signature };
     }
```
```diff
diff --git a/tests/TakEngine.Core.Tests/TakGameSessionTests.cs b/tests/TakEngine.Core.Tests/TakGameSessionTests.cs
index 63845e3..613fdd7 100644
--- a/tests/TakEngine.Core.Tests/TakGameSessionTests.cs
+++ b/tests/TakEngine.Core.Tests/TakGameSessionTests.cs
@@ -3,6 +3,7 @@ using System.Collections.Generic;
 using TakEngine.Abstractions;
 using TakEngine.Core.Cryptography;
 using TakEngine.Core.Session;
+using TakEngine.Crypto;
 using Xunit;
 
 namespace TakEngine.Core.Tests;
@@ -160,8 +161,10 @@ public class TakGameSessionTests
     [Fact]
     public void RemoteP2P_SynchronizesMovesAndHashChain()
     {
-        var (alicePub, alicePriv) = CryptoSigner.GenerateKeyPair();
-        var (bobPub, bobPriv) = CryptoSigner.GenerateKeyPair();
+        SecretKey alicePriv = TestKeys.Create(1);
+        SecretKey bobPriv = TestKeys.Create(2);
+        string alicePub = alicePriv.PublicKey.ToHex();
+        string bobPub = bobPriv.PublicKey.ToHex();
         var gameId = GameId.New();
 
         var aliceSession = TakGameSession.CreateRemote(
@@ -169,14 +172,14 @@ public class TakGameSessionTests
             BoardSize.Five,
             PlayerColor.White,
             alicePriv,
-            bobPub);
+            bobPriv.PublicKey);
 
         var bobSession = TakGameSession.CreateRemote(
             gameId,
             BoardSize.Five,
             PlayerColor.Black,
             bobPriv,
-            alicePub);
+            alicePriv.PublicKey);
 
         Assert.Equal(aliceSession.CurrentStateHash, bobSession.CurrentStateHash);
 
@@ -221,9 +224,11 @@ public class TakGameSessionTests
     [Fact]
     public void RemoteP2P_DetectsProtocolViolations()
     {
-        var (alicePub, alicePriv) = CryptoSigner.GenerateKeyPair();
-        var (bobPub, bobPriv) = CryptoSigner.GenerateKeyPair();
-        var (attackerPub, attackerPriv) = CryptoSigner.GenerateKeyPair();
+        SecretKey alicePriv = TestKeys.Create(1);
+        SecretKey bobPriv = TestKeys.Create(2);
+        SecretKey attackerPriv = TestKeys.Create(3);
+        string alicePub = alicePriv.PublicKey.ToHex();
+        string attackerPub = attackerPriv.PublicKey.ToHex();
         var gameId = GameId.New();
 
         var bobSession = TakGameSession.CreateRemote(
@@ -231,13 +236,13 @@ public class TakGameSessionTests
             BoardSize.Five,
             PlayerColor.Black,
             bobPriv,
-            alicePub);
+            alicePriv.PublicKey);
 
         ProtocolViolationException? caughtViolation = null;
         bobSession.OnProtocolViolationDetected += ex => caughtViolation = ex;
 
         // 1. Attacker pubkey violation
-        string fakeSig = CryptoSigner.Sign(attackerPriv, $"{bobSession.CurrentStateHash}:a1");
+        string fakeSig = PayloadSignature.Sign(attackerPriv, $"{bobSession.CurrentStateHash}:a1");
         var res1 = bobSession.ProcessRemoteMove(attackerPub, bobSession.CurrentStateHash, "a1", fakeSig);
         Assert.False(res1.IsSuccess);
         Assert.NotNull(caughtViolation);
@@ -252,7 +257,7 @@ public class TakGameSessionTests
 
         // 3. Hash mismatch violation
         caughtViolation = null;
-        string validSig = CryptoSigner.Sign(alicePriv, $"{bobSession.CurrentStateHash}:a1");
+        string validSig = PayloadSignature.Sign(alicePriv, $"{bobSession.CurrentStateHash}:a1");
         var res3 = bobSession.ProcessRemoteMove(alicePub, "0000000000000000000000000000000000000000000000000000000000000000", "a1", validSig);
         Assert.False(res3.IsSuccess);
         Assert.NotNull(caughtViolation);
```

- [ ] **Step 2: Run the tests to see them fail**

```bash
export MSBuildEnableWorkloadResolver=false
dotnet test tests/TakEngine.Core.Tests
```
Expected: build FAILS with `CS0103: The name 'PayloadSignature' does not exist` and `CS1503: cannot convert from 'TakEngine.Crypto.SecretKey' to 'string'` (the old `CreateRemote` takes hex strings).

- [ ] **Step 3: Implement `PayloadSignature` and rewire the sessions**

`src/TakEngine.Core/Cryptography/PayloadSignature.cs`:
```csharp
using System.Security.Cryptography;
using System.Text;
using TakEngine.Crypto;

namespace TakEngine.Core.Cryptography;

/// <summary>
/// BIP-340 signature over SHA-256(UTF-8(payload)) with the player's secp256k1 key (D-011, which retired the Ed25519
/// signer). Used for the session's "prevHash:ptn" payload and the spectator <c>BroadcastEnvelope</c> payload.
/// Transitional: F-033 moves the session to <c>ActionDigest</c>. Pure: aux is 32 zero bytes, which BIP-340 permits
/// (the nonce is still derived from the secret key and the message).
/// </summary>
public static class PayloadSignature
{
    private static readonly byte[] ZeroAux = new byte[Schnorr.AuxLength];

    public static string Sign(SecretKey key, string payload)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(payload);
        return Convert.ToHexStringLower(Schnorr.Sign(key, Digest(payload), ZeroAux));
    }

    /// <summary>False for any malformed input (bad hex, wrong lengths, off-curve key); never throws.</summary>
    public static bool Verify(string? publicKeyHex, string? payload, string? signatureHex)
    {
        if (payload is null || signatureHex is null || signatureHex.Length != Schnorr.SignatureLength * 2)
            return false;
        if (!PublicKey.TryFromHex(publicKeyHex, out PublicKey? key))
            return false;
        byte[] signature;
        try
        {
            signature = Convert.FromHexString(signatureHex);
        }
        catch (FormatException)
        {
            return false;
        }
        return Schnorr.Verify(key, Digest(payload), signature);
    }

    private static byte[] Digest(string payload) => SHA256.HashData(Encoding.UTF8.GetBytes(payload));
}
```

Apply:
```diff
diff --git a/src/TakEngine.Core/Session/TakGameSession.cs b/src/TakEngine.Core/Session/TakGameSession.cs
index 3cb8651..7f7b7ed 100644
--- a/src/TakEngine.Core/Session/TakGameSession.cs
+++ b/src/TakEngine.Core/Session/TakGameSession.cs
@@ -5,6 +5,7 @@ using TakEngine.Core.Board;
 using TakEngine.Core.Cryptography;
 using TakEngine.Core.Rules;
 using TakEngine.Core.Serialization;
+using TakEngine.Crypto;
 
 namespace TakEngine.Core.Session;
 
@@ -12,8 +13,8 @@ public sealed class TakGameSession : ITakGameSession
 {
     private readonly GameBoard _board;
     private readonly bool _isRemote;
-    private readonly string? _localPrivateKeyHex;
-    private readonly string? _opponentPubKeyHex;
+    private readonly SecretKey? _localKey;
+    private readonly PublicKey? _opponentPubKey;
     private string _currentStateHash;
 
     public GameId Id { get; }
@@ -40,14 +41,14 @@ public sealed class TakGameSession : ITakGameSession
         BoardSize size,
         PlayerColor localColor,
         bool isRemote,
-        string? localPrivateKeyHex = null,
-        string? opponentPubKeyHex = null)
+        SecretKey? localKey = null,
+        PublicKey? opponentPubKey = null)
     {
         Id = id;
         LocalColor = localColor;
         _isRemote = isRemote;
-        _localPrivateKeyHex = localPrivateKeyHex;
-        _opponentPubKeyHex = opponentPubKeyHex;
+        _localKey = localKey;
+        _opponentPubKey = opponentPubKey;
         _board = new GameBoard(size);
         _currentStateHash = StateHasher.ComputeGenesisHash(size);
     }
@@ -65,22 +66,25 @@ public sealed class TakGameSession : ITakGameSession
     }
 
     /// <summary>
-    /// Creates a remote P2P session with deterministic local color and cryptographic signing.
+    /// Creates a remote P2P session: local moves are signed with the player's secp256k1 key, remote moves must come
+    /// from <paramref name="opponentPubKey"/>.
     /// </summary>
     public static TakGameSession CreateRemote(
         GameId id,
         BoardSize size,
         PlayerColor localColor,
-        string localPrivateKeyHex,
-        string opponentPubKeyHex)
+        SecretKey localKey,
+        PublicKey opponentPubKey)
     {
+        ArgumentNullException.ThrowIfNull(localKey);
+        ArgumentNullException.ThrowIfNull(opponentPubKey);
         return new TakGameSession(
             id,
             size,
             localColor,
             isRemote: true,
-            localPrivateKeyHex: localPrivateKeyHex,
-            opponentPubKeyHex: opponentPubKeyHex);
+            localKey: localKey,
+            opponentPubKey: opponentPubKey);
     }
 
     public IReadOnlyList<TakMove> GetLegalMovesForSquare(Coord coord)
@@ -176,7 +180,7 @@ public sealed class TakGameSession : ITakGameSession
             return CommandResult.Fail(ex.Message);
         }
 
-        if (_opponentPubKeyHex != null && !string.Equals(playerPubKey, _opponentPubKeyHex, StringComparison.OrdinalIgnoreCase))
+        if (_opponentPubKey != null && !string.Equals(playerPubKey, _opponentPubKey.ToHex(), StringComparison.OrdinalIgnoreCase))
         {
             var ex = new ProtocolViolationException($"Received move from unauthorized pubkey: {playerPubKey}");
             OnProtocolViolationDetected?.Invoke(ex);
@@ -191,9 +195,9 @@ public sealed class TakGameSession : ITakGameSession
             return CommandResult.Fail(ex.Message);
         }
 
-        // Verify Ed25519 signature over (prevStateHash + ptnMove)
+        // Verify the BIP-340 signature over (prevStateHash + ptnMove); F-033 replaces this payload with ActionDigest
         string payload = $"{prevStateHash}:{ptnMove}";
-        if (!CryptoSigner.Verify(playerPubKey, payload, signature))
+        if (!PayloadSignature.Verify(playerPubKey, payload, signature))
         {
             var ex = new ProtocolViolationException("Invalid cryptographic signature on remote move payload.");
             OnProtocolViolationDetected?.Invoke(ex);
@@ -242,8 +246,8 @@ public sealed class TakGameSession : ITakGameSession
         string ptn = move.ToPtn();
         string tps = TpsSerializer.Serialize(_board);
 
-        string playerPubKey = _isRemote && _localPrivateKeyHex != null
-            ? CryptoSigner.GetPublicKeyHex(_localPrivateKeyHex)
+        string playerPubKey = _isRemote && _localKey != null
+            ? _localKey.PublicKey.ToHex()
             : "local-player";
 
         string prevHash = _currentStateHash;
@@ -251,11 +255,11 @@ public sealed class TakGameSession : ITakGameSession
 
         OnMoveExecuted?.Invoke(snapshot, move);
 
-        if (_isRemote && _localPrivateKeyHex != null)
+        if (_isRemote && _localKey != null)
         {
             // Compute signature over previous state hash + ptn move
             string payload = $"{prevHash}:{ptn}";
-            string sig = CryptoSigner.Sign(_localPrivateKeyHex, payload);
+            string sig = PayloadSignature.Sign(_localKey, payload);
             OnRemoteEnvelopeReady?.Invoke(sig);
         }
```
```diff
diff --git a/src/TakEngine.Core/Session/SpectatorGameSession.cs b/src/TakEngine.Core/Session/SpectatorGameSession.cs
index b65b368..bdf7429 100644
--- a/src/TakEngine.Core/Session/SpectatorGameSession.cs
+++ b/src/TakEngine.Core/Session/SpectatorGameSession.cs
@@ -100,7 +100,7 @@ public sealed class SpectatorGameSession : ISpectatorGameSession
 
         // 5. Verify Cryptographic Signature
         string signingPayload = envelope.GetSigningPayload();
-        if (!CryptoSigner.Verify(envelope.PlayerPubKey, signingPayload, envelope.Signature))
+        if (!PayloadSignature.Verify(envelope.PlayerPubKey, signingPayload, envelope.Signature))
         {
             RaiseDesync($"Invalid cryptographic signature on turn {envelope.TurnIndex} from {envelope.PlayerPubKey}");
             return false;
```

- [ ] **Step 4: Run the Core tests to see them pass**

```bash
dotnet test tests/TakEngine.Core.Tests
```
Expected: `Passed!`, 0 failed (includes `RemoteP2P_SynchronizesMovesAndHashChain` and `RemoteP2P_DetectsProtocolViolations` with secp256k1 keys).

- [ ] **Step 5: Rewire the heads (compile fixes only, no UI change) and delete `CryptoSigner`**

CLI local/AI games keep one throwaway key per game, now secp256k1 (persistent identity in the CLI is F-036):
```diff
diff --git a/src/TakApp.Cli/Program.cs b/src/TakApp.Cli/Program.cs
index 4b65ee6..e7a540e 100644
--- a/src/TakApp.Cli/Program.cs
+++ b/src/TakApp.Cli/Program.cs
@@ -1,5 +1,6 @@
 using System;
 using System.IO;
+using System.Security.Cryptography;
 using System.Threading.Tasks;
 using Spectre.Console;
 using TakApp.Cli.Input;
@@ -10,6 +11,7 @@ using TakEngine.Core.Board;
 using TakEngine.Core.Cryptography;
 using TakEngine.Core.Serialization;
 using TakEngine.Core.Storage;
+using TakEngine.Crypto;
 using TakEngine.Transport.Matchmaking;
 
 namespace TakApp.Cli;
@@ -116,7 +118,7 @@ public static class Program
 
         var board = new GameBoard(size);
         var gameId = Guid.NewGuid();
-        var keyPair = CryptoSigner.GenerateKeyPair();
+        var keyPair = SecretKey.Generate(() => RandomNumberGenerator.GetBytes(SecretKey.Length));
         string genesisHash = StateHasher.ComputeGenesisHash(size);
         string prevStateHash = genesisHash;
         int moveIndex = 1;
@@ -125,7 +127,7 @@ public static class Program
             Id: gameId,
             BoardSize: size,
             LocalPlayerColor: PlayerColor.White,
-            OpponentPubKey: keyPair.PublicKeyHex,
+            OpponentPubKey: keyPair.PublicKey.ToHex(),
             Status: GameStatus.Active,
             WinnerPubKey: null,
             StartedAt: DateTime.UtcNow,
@@ -163,13 +165,13 @@ public static class Program
 
                 lastMoveStr = cmd.Move.ToPtn();
                 string tpsSnapshot = TpsSerializer.Serialize(board);
-                string stateHash = StateHasher.ComputeStateHash(prevStateHash, moveIndex, keyPair.PublicKeyHex, lastMoveStr, tpsSnapshot);
-                string signature = CryptoSigner.Sign(keyPair.PrivateKeyHex, stateHash);
+                string stateHash = StateHasher.ComputeStateHash(prevStateHash, moveIndex, keyPair.PublicKey.ToHex(), lastMoveStr, tpsSnapshot);
+                string signature = PayloadSignature.Sign(keyPair, stateHash);
 
                 var moveEntity = new MoveEntity(
                     GameId: gameId,
                     TurnIndex: moveIndex++,
-                    PlayerPubKey: keyPair.PublicKeyHex,
+                    PlayerPubKey: keyPair.PublicKey.ToHex(),
                     PtnMove: lastMoveStr,
                     TpsSnapshot: tpsSnapshot,
                     StateHash: stateHash,
@@ -233,7 +235,7 @@ public static class Program
         var bot = new MinimaxTakBot(difficulty);
         var board = new GameBoard(size);
         var gameId = Guid.NewGuid();
-        var keyPair = CryptoSigner.GenerateKeyPair();
+        var keyPair = SecretKey.Generate(() => RandomNumberGenerator.GetBytes(SecretKey.Length));
         string genesisHash = StateHasher.ComputeGenesisHash(size);
         string prevStateHash = genesisHash;
         int moveIndex = 1;
@@ -289,13 +291,13 @@ public static class Program
 
                 lastMoveStr = move.ToPtn();
                 string tpsSnapshot = TpsSerializer.Serialize(board);
-                string stateHash = StateHasher.ComputeStateHash(prevStateHash, moveIndex, keyPair.PublicKeyHex, lastMoveStr, tpsSnapshot);
-                string signature = CryptoSigner.Sign(keyPair.PrivateKeyHex, stateHash);
+                string stateHash = StateHasher.ComputeStateHash(prevStateHash, moveIndex, keyPair.PublicKey.ToHex(), lastMoveStr, tpsSnapshot);
+                string signature = PayloadSignature.Sign(keyPair, stateHash);
 
                 var moveEntity = new MoveEntity(
                     GameId: gameId,
                     TurnIndex: moveIndex++,
-                    PlayerPubKey: keyPair.PublicKeyHex,
+                    PlayerPubKey: keyPair.PublicKey.ToHex(),
                     PtnMove: lastMoveStr,
                     TpsSnapshot: tpsSnapshot,
                     StateHash: stateHash,
@@ -334,11 +336,11 @@ public static class Program
         AnsiConsole.MarkupLine("[bold cyan]Quick Play Matchmaking[/]");
         AnsiConsole.MarkupLine("Connecting to public Nostr relays: [grey]wss://relay.damus.io, wss://nos.lol, wss://relay.primal.net[/]...");
 
-        var keyPair = CryptoSigner.GenerateKeyPair();
-        var proposal = QuickPlayMatchmaker.CreateChallenge(BoardSize.Five, keyPair.PublicKeyHex);
-        var broadcast = QuickPlayMatchmaker.CreateBroadcastEvent(keyPair.PublicKeyHex, BoardSize.Five, ["wss://relay.damus.io"]);
+        var keyPair = SecretKey.Generate(() => RandomNumberGenerator.GetBytes(SecretKey.Length));
+        var proposal = QuickPlayMatchmaker.CreateChallenge(BoardSize.Five, keyPair.PublicKey.ToHex());
+        var broadcast = QuickPlayMatchmaker.CreateBroadcastEvent(keyPair.PublicKey.ToHex(), BoardSize.Five, ["wss://relay.damus.io"]);
 
-        AnsiConsole.MarkupLine($"[green]✓[/] Matchmaking ticket created with ephemeral key [grey]{keyPair.PublicKeyHex[..12]}...[/]");
+        AnsiConsole.MarkupLine($"[green]✓[/] Matchmaking ticket created with ephemeral key [grey]{keyPair.PublicKey.ToHex()[..12]}...[/]");
         AnsiConsole.MarkupLine($"[yellow]Broadcast Kind: 20001 (TTL: 60s)[/] looking for opponent on 5x5 pool...");
 
         // Simulate match setup for demonstration
@@ -353,9 +355,9 @@ public static class Program
         RenderHeader();
         AnsiConsole.MarkupLine("[bold cyan]Generate Direct Invite Code / QR[/]");
 
-        var keyPair = CryptoSigner.GenerateKeyPair();
+        var keyPair = SecretKey.Generate(() => RandomNumberGenerator.GetBytes(SecretKey.Length));
         var gameId = Guid.NewGuid();
-        var invite = new InviteCode(gameId, keyPair.PublicKeyHex, BoardSize.Five, ["wss://relay.damus.io"]);
+        var invite = new InviteCode(gameId, keyPair.PublicKey.ToHex(), BoardSize.Five, ["wss://relay.damus.io"]);
 
         AnsiConsole.WriteLine();
         AnsiConsole.MarkupLine("[bold yellow]Shareable Link (URI):[/]");
```

Avalonia's simulated remote mode (two throwaway keys in one process, replaced at M6):
```diff
diff --git a/src/TakApp.Avalonia/ViewModels/MainViewModel.cs b/src/TakApp.Avalonia/ViewModels/MainViewModel.cs
index 77d9c47..5cb0083 100644
--- a/src/TakApp.Avalonia/ViewModels/MainViewModel.cs
+++ b/src/TakApp.Avalonia/ViewModels/MainViewModel.cs
@@ -1,7 +1,8 @@
+using System.Security.Cryptography;
 using CommunityToolkit.Mvvm.ComponentModel;
 using TakEngine.Abstractions;
-using TakEngine.Core.Cryptography;
 using TakEngine.Core.Session;
+using TakEngine.Crypto;
 
 namespace TakApp.Avalonia.ViewModels;
 
@@ -20,9 +21,10 @@ public partial class MainViewModel : ViewModelBase
         ITakGameSession session;
         if (isRemote)
         {
-            var (pubA, privA) = CryptoSigner.GenerateKeyPair();
-            var (pubB, privB) = CryptoSigner.GenerateKeyPair();
-            session = TakGameSession.CreateRemote(GameId.New(), size, PlayerColor.White, privA, pubB);
+            // Simulated remote mode (two throwaway keys in one process) until M6 replaces it.
+            var localKey = SecretKey.Generate(() => RandomNumberGenerator.GetBytes(SecretKey.Length));
+            var opponentKey = SecretKey.Generate(() => RandomNumberGenerator.GetBytes(SecretKey.Length));
+            session = TakGameSession.CreateRemote(GameId.New(), size, PlayerColor.White, localKey, opponentKey.PublicKey);
         }
         else
         {
```

Blazor: the identity moves to `tak.identity.v1` in the `{"v":1,"nsec":…}` format; the old Ed25519 entries are never read, only removed by `ClearIdentityAsync` (decision 0010). Method signatures used by the razor pages are unchanged (`SetKeypairAsync` had no caller outside this file and is removed):
```diff
diff --git a/src/TakApp.Blazor/Services/BrowserStorage.cs b/src/TakApp.Blazor/Services/BrowserStorage.cs
index d0c9c4e..409dbd5 100644
--- a/src/TakApp.Blazor/Services/BrowserStorage.cs
+++ b/src/TakApp.Blazor/Services/BrowserStorage.cs
@@ -1,14 +1,19 @@
 using System;
+using System.Security.Cryptography;
 using System.Text.Json;
 using System.Threading.Tasks;
 using Microsoft.JSInterop;
-using TakEngine.Core.Cryptography;
 using TakEngine.Crypto;
 
 namespace TakApp.Blazor.Services;
 
 public sealed class BrowserStorage
 {
+    // The secp256k1 identity (D-011) lives under a new name, in the same {"v":1,"nsec":...} format as the CLI key file.
+    // The old Ed25519 entries (tak_p2p_privkey / tak_p2p_pubkey) are never read: an Ed25519 secret is also a valid
+    // secp256k1 scalar and would silently become a different npub (docs/decisions/0010). Only ClearIdentityAsync removes them.
+    private const string IdentityKey = "tak.identity.v1";
+
     private readonly IJSRuntime _js;
 
     public BrowserStorage(IJSRuntime js)
@@ -40,45 +45,34 @@ public sealed class BrowserStorage
         }
     }
 
+    /// <summary>
+    /// Loads the identity, or creates and stores one on first use. A stored identity that cannot be read throws
+    /// <see cref="IdentityFormatException"/> and is left untouched (never silently replaced).
+    /// </summary>
     public async Task<(string PrivKeyHex, string PubKeyHex)> GetOrCreateKeypairAsync()
     {
-        string? privKey = await GetItemAsync("tak_p2p_privkey");
-        string? pubKey = await GetItemAsync("tak_p2p_pubkey");
-
-        if (!string.IsNullOrEmpty(privKey) && !string.IsNullOrEmpty(pubKey))
+        string? document = await GetItemAsync(IdentityKey);
+        if (!string.IsNullOrEmpty(document))
         {
-            return (privKey, pubKey);
+            SecretKey stored = IdentityDocument.Parse(document);
+            return (stored.ToHex(), stored.PublicKey.ToHex());
         }
 
-        var (generatedPriv, generatedPub) = CryptoSigner.GenerateKeyPair();
-        await SetItemAsync("tak_p2p_privkey", generatedPriv);
-        await SetItemAsync("tak_p2p_pubkey", generatedPub);
-
-        return (generatedPriv, generatedPub);
-    }
-
-    public async Task SetKeypairAsync(string privKeyHex, string pubKeyHex)
-    {
-        await SetItemAsync("tak_p2p_privkey", privKeyHex);
-        await SetItemAsync("tak_p2p_pubkey", pubKeyHex);
+        SecretKey created = SecretKey.Generate(() => RandomNumberGenerator.GetBytes(SecretKey.Length));
+        await SetItemAsync(IdentityKey, IdentityDocument.Serialize(created));
+        return (created.ToHex(), created.PublicKey.ToHex());
     }
 
+    /// <summary>Replaces the identity with an imported nsec or 64-char hex secret; throws <see cref="InvalidKeyException"/> if invalid.</summary>
     public async Task<(string PrivKeyHex, string PubKeyHex)> ImportPrivateKeyAsync(string privateKeyOrNsec)
     {
-        string privHex;
-        if (privateKeyOrNsec.StartsWith("nsec1", StringComparison.OrdinalIgnoreCase))
-        {
-            var (_, hex) = Nip19.Decode(privateKeyOrNsec);
-            privHex = hex;
-        }
-        else
-        {
-            privHex = privateKeyOrNsec.Trim().ToLowerInvariant();
-        }
+        string input = privateKeyOrNsec.Trim();
+        SecretKey key = input.StartsWith("nsec1", StringComparison.OrdinalIgnoreCase)
+            ? SecretKey.FromNsec(input)
+            : SecretKey.FromHex(input);
 
-        string pubHex = CryptoSigner.GetPublicKeyHex(privHex);
-        await SetKeypairAsync(privHex, pubHex);
-        return (privHex, pubHex);
+        await SetItemAsync(IdentityKey, IdentityDocument.Serialize(key));
+        return (key.ToHex(), key.PublicKey.ToHex());
     }
 
     public async Task<string?> GetNicknameAsync()
@@ -129,6 +123,7 @@ public sealed class BrowserStorage
     {
         try
         {
+            await _js.InvokeVoidAsync("localStorage.removeItem", IdentityKey);
             await _js.InvokeVoidAsync("localStorage.removeItem", "tak_p2p_privkey");
             await _js.InvokeVoidAsync("localStorage.removeItem", "tak_p2p_pubkey");
             await _js.InvokeVoidAsync("localStorage.removeItem", "tak_p2p_nickname");
```

Replace `src/TakEngine.Core/TakEngine.Core.csproj` with (BouncyCastle now comes only through Crypto):
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <ProjectReference Include="..\TakEngine.Abstractions\TakEngine.Abstractions.csproj" />
    <ProjectReference Include="..\TakEngine.Crypto\TakEngine.Crypto.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Data.Sqlite" Version="10.0.12" />
  </ItemGroup>

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

</Project>
```

```bash
git rm src/TakEngine.Core/Cryptography/CryptoSigner.cs
git grep -n "CryptoSigner" -- src tests   # expected: no output
git grep -n "KeyPair\b" -- src tests      # expected: no output (the Ed25519 KeyPair record lived in CryptoSigner.cs)
```

- [ ] **Step 6: Run the four checks**

```bash
export MSBuildEnableWorkloadResolver=false
dotnet restore TakGame.Ci.slnf
dotnet build TakGame.Ci.slnf --no-restore
dotnet format TakGame.Ci.slnf --verify-no-changes --no-restore
dotnet test TakGame.Ci.slnf --no-build --filter "Category!=LiveRelay"
```
Expected: all exit 0 (Blazor, Avalonia.Desktop and CLI build; four test assemblies `Passed!`).

- [ ] **Step 7: Smoke-run the CLI menu (it must still start and exit)**

```bash
printf '6\n' | dotnet run --project src/TakApp.Cli --no-build; echo "exit=$?"
```
Expected: the menu prints and `exit=0` (same as the stage-6 proof in playbook.md).

- [ ] **Step 8: Commit**

```bash
git add -A src/TakEngine.Core src/TakApp.Cli src/TakApp.Avalonia src/TakApp.Blazor tests/TakEngine.Core.Tests
git commit -m "M0 F-031: delete Ed25519 CryptoSigner; sessions and heads use secp256k1 keys" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: Mutation check, Python cross-check and evidence (F-015, F-031, F-032)

**Files:**
- Modify (only if a survivor needs a new test): the owning test file in `tests/TakEngine.Crypto.Tests/` or `tests/TakEngine.Transport.Tests/`
- Modify: `pipeline/features.json` (`evidence` of F-015, F-031, F-032)
- Throwaway, outside the repo (never committed): `sign-events.cs`, `verify-events.py`, `csharp-events.jsonl`

**Interfaces:**
- Consumes: everything produced by Tasks 2-7.
- Produces: mutation scores >= 80 % for the Crypto module and for Transport `Nip01Serializer.cs` + `NostrEvents.cs`; evidence text; a green CI run on the pushed branch.

- [ ] **Step 1: Run Stryker on the Crypto module**

```bash
export MSBuildEnableWorkloadResolver=false
dotnet tool restore
cd tests/TakEngine.Crypto.Tests
dotnet stryker --project TakEngine.Crypto.csproj --break-at 80
cd ../..
```
Expected: `The final mutation score is` >= 80 % (prototype: 86.73 %; per file: SecretKey 96 %, IdentityDocument 94 %, Nip19 89 %, PublicKey 85 %, Nip44 81 %, Schnorr 73 %). No `Safe Mode!` line. The HTML report is under `tests/TakEngine.Crypto.Tests/StrykerOutput/` (git-ignored).

- [ ] **Step 2: Run Stryker on the NIP-01 files of Transport**

```bash
cd tests/TakEngine.Transport.Tests
dotnet stryker --project TakEngine.Transport.csproj --mutate "**/Nip01Serializer.cs" --mutate "**/NostrEvents.cs" --break-at 80
cd ../..
```
Expected: >= 80 % (prototype: 89.36 %).

- [ ] **Step 3: Triage every surviving mutant**

Open each report (`StrykerOutput/<timestamp>/reports/mutation-report.html`) and compare the survivors with this list of survivors the prototype left on purpose (each is equivalent or message-only):

| File | Survivor | Why it is acceptable |
|---|---|---|
| Nip44.cs | exception message strings (any `"..."` -> `""`) | callers and tests use `Nip44Exception.Error`, not the text |
| Nip44.cs | `unpaddedLength <= 32` -> `< 32`; `nextPower <= 256` -> `< 256`; `unpaddedLength - 1` -> `+ 1` | equivalent: the boundary values give the same padded length (24 official vectors pass either way) |
| Nip44.cs | `engine.Init(true, …)` -> `false` | ChaCha20 is a stream cipher: encrypt == decrypt |
| Nip44.cs | removing the conversation-key length throw; `ThrowIfNull(plaintext)` | the BCL HKDF / `Encoding.GetBytes` throw the same exception type anyway |
| Schnorr.cs | removing the digest-length throw; `\|\|` -> `&&` in the length check of `Verify`; message strings | NBitcoin rejects the same inputs itself (ArgumentException / false) |
| PublicKey.cs | `Length * 2` in the hex-length message; removing the "not valid hex" throw | message-only / the following `FromBytes` rejects the zero buffer's off-curve point |
| SecretKey.cs | message of the "64 invalid draws" exception | message-only |
| Nip19.cs | `>>` -> `>>>`, `StringBuilder` capacity arithmetic, separator-position arithmetic | equivalent for non-negative values / capacity is only a hint / the checksum rejects the same strings |
| Nip01Serializer.cs, NostrEvents.cs | `ArgumentNullException.ThrowIfNull(...)` removals where a later dereference throws; message strings | equivalent exception type / message-only |

Any survivor NOT covered by this table is a missing test: add a test to the owning test file that fails with the mutant and passes without it (break the line by hand, see the new test fail, restore), then re-run Steps 1-2. Do not lower `--break-at`.

- [ ] **Step 4: Cross-check C#-signed events in the spike's Python BIP-340 reference verifier**

Make a temp directory outside the repository (for example `EVID=$(mktemp -d)`) and note the repo root (`git rev-parse --show-toplevel`). Create `$EVID/sign-events.cs`, replacing `REPO_ROOT` on the first line with that repo root path:
```csharp
#:project REPO_ROOT/src/TakEngine.Transport/TakEngine.Transport.csproj
#:property JsonSerializerIsReflectionEnabledByDefault=true
#:property TreatWarningsAsErrors=false
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using TakEngine.Crypto;
using TakEngine.Transport.Nostr;

// F-032 evidence: events signed by the real build (fresh random key and aux), for the Python BIP-340 reference verifier.
var key = SecretKey.Generate(() => RandomNumberGenerator.GetBytes(32));
string[] contents = ["plain", "a1>+ <b2 & \"q\" \\ / end", "line1\nline2\r\n\tend", "ctl \b \f \u0001 \u001f \u007f end", "emoji \U0001F984 sep " + (char)0x2028 + " end", ""];
List<List<string>> tags = [["p", key.PublicKey.ToHex()], ["g", "0b7e3d4c-1f2a-4b5c-8d9e-0f1a2b3c4d5e"]];
var options = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
var lines = contents.Select((c, i) => JsonSerializer.Serialize(
    NostrEvents.Sign(key, 1_700_000_000 + i, 3825, tags, c, RandomNumberGenerator.GetBytes(32)),
    options));
File.WriteAllLines(args[0], lines);
Console.WriteLine($"wrote {contents.Length} events signed by {key.PublicKey.ToHex()} to {args[0]}");
```
Create `$EVID/verify-events.py`:
```python
"""F-032 evidence: verify C#-signed events with the bitcoin/bips BIP-340 reference and a json/hashlib NIP-01 id.
Usage: python verify-events.py <repo root> <events.jsonl>"""
import hashlib
import json
import os
import sys

sys.path.insert(0, os.path.join(sys.argv[1], "pipeline", "spikes", "R-002-R-003-secp256k1-crypto", "vectors"))
from bip340_reference import schnorr_verify  # noqa: E402

bad = 0
lines = [line for line in open(sys.argv[2], encoding="utf-8") if line.strip()]
for line in lines:
    e = json.loads(line)
    serialized = json.dumps([0, e["pubkey"], e["created_at"], e["kind"], e["tags"], e["content"]], separators=(",", ":"), ensure_ascii=False)
    id_ok = hashlib.sha256(serialized.encode("utf-8")).hexdigest() == e["id"]
    sig_ok = schnorr_verify(bytes.fromhex(e["id"]), bytes.fromhex(e["pubkey"]), bytes.fromhex(e["sig"]))
    print("id_ok=%s sig_ok=%s id=%s content=%r" % (id_ok, sig_ok, e["id"], e["content"][:30]))
    bad += not (id_ok and sig_ok)
print("C# -> Python reference: %d ok, %d bad" % (len(lines) - bad, bad))
sys.exit(1 if bad else 0)
```
Run:
```bash
export MSBuildEnableWorkloadResolver=false
REPO=$(git rev-parse --show-toplevel)
cd "$EVID"
dotnet run sign-events.cs -- csharp-events.jsonl
PYTHONIOENCODING=utf8 python verify-events.py "$REPO" csharp-events.jsonl
cd "$REPO"
```
Expected: `wrote 6 events signed by <pubkey>`, six lines `id_ok=True sig_ok=True`, `C# -> Python reference: 6 ok, 0 bad`, exit 0. Keep the printed event ids for the evidence. Nothing here is published anywhere.

- [ ] **Step 5: Run the four checks, commit any new tests, push, and get the green CI run**

```bash
export MSBuildEnableWorkloadResolver=false
dotnet restore TakGame.Ci.slnf
dotnet build TakGame.Ci.slnf --no-restore
dotnet format TakGame.Ci.slnf --verify-no-changes --no-restore
dotnet test TakGame.Ci.slnf --no-build --filter "Category!=LiveRelay"
git status --short    # StrykerOutput/ is git-ignored; only test files you added may show
```
If Step 3 added tests: `git add <those files>` and `git commit -m "M0 F-015 F-031 F-032: tests that kill surviving mutants" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"`. Then:
```bash
git push
SHA=$(git rev-parse HEAD)
gh run list --workflow ci.yml --commit "$SHA" --json databaseId,status,conclusion
```
Poll until the run appears, then `gh run watch <databaseId> --exit-status` (expected exit 0) and note the run id and url.

- [ ] **Step 6: Record the evidence in `pipeline/features.json`**

Fill `<…>` with what you observed in Steps 1-5 (date, scores, run id/url). No test counts. Leave `"passes": false` everywhere.

F-015: replace
```json
        "The old test that decrypts with the sender's own keys is deleted"
      ],
      "passes": false,
      "evidence": null
```
with
```json
        "The old test that decrypts with the sender's own keys is deleted"
      ],
      "passes": false,
      "evidence": "Seen running <YYYY-MM-DD>: tests/TakEngine.Crypto.Tests Nip44VectorTests (paulmillr/nip44 nip44.vectors.json: conversation key 35 valid + 8 invalid, message keys, padded length, encrypt_decrypt exact payloads with the key derived in both directions, long messages, 12 invalid payloads each failing with the reason the vector names, invalid plaintext lengths) and Nip44TwoPeerTests (seeded independent keypairs: conv(a,B) == conv(b,A), Bob decrypts Alice and Alice decrypts Bob with their own secrets; third key, flipped nonce/ciphertext/MAC bit, wrong version byte and bad padding under a valid MAC all fail). Deleted: src/TakEngine.Transport/Nostr/Nip44Encryption.cs, Nip44EncryptionTests (incl. Nip44_EncryptAndDecrypt_RoundTripsSuccessfully) and TransportBenchmarkTests. Stryker 5.0.0 TakEngine.Crypto <score> %. CI run <id> (<url>) green."
```

F-031: replace
```json
        "Pubkeys not 32 bytes or not on the curve are rejected on input"
      ],
      "passes": false,
      "evidence": null
```
with
```json
        "Pubkeys not 32 bytes or not on the curve are rejected on input"
      ],
      "passes": false,
      "evidence": "Seen running <YYYY-MM-DD>: tests/TakEngine.Crypto.Tests KeyTests (bip340 test-vectors.csv secret -> x-only pubkey for all 8 rows with a secret; 31/33-byte and off-curve pubkeys and secrets 0, n, 2^256-1 rejected at the boundary), Nip19Tests (NIP-19 spec npub/nsec pair decodes to the documented hex; round trips), IdentityDocumentTests; tests/TakEngine.Storage.Tests FileKeyStoreTests (first run generates and saves identity.json {\"v\":1,\"nsec\":...}, a new store over the same directory loads the same key; wrong length, out-of-range scalar, newer v, not JSON -> KeyStoreException naming file and reason, file bytes unchanged, never overwritten). `git grep CryptoSigner -- src` empty; sessions, spectator, CLI, Avalonia and Blazor use SecretKey/PayloadSignature. Not yet: the CLI loading identity.json on restart needs --profile/--data-dir (F-036). Stryker 5.0.0 TakEngine.Crypto <score> %. CI run <id> (<url>) green."
```

F-032: replace
```json
        "Failure: flipped id, flipped sig bit, tampered content or pubkey of length != 32 all fail verification and are never decrypted"
      ],
      "passes": false,
      "evidence": null
```
with
```json
        "Failure: flipped id, flipped sig bit, tampered content or pubkey of length != 32 all fail verification and are never decrypted"
      ],
      "passes": false,
      "evidence": "Seen running <YYYY-MM-DD>: SchnorrTests (BIP-340 csv: the 15 vectors with 32-byte messages incl. the invalid ones; the 4 sign vectors give the exact signature). Nip01SerializerTests: ids and serializations equal nip01-vectors.json (make_nip01_vectors.py: Python json/hashlib + bip340_reference.py) for + < > & quotes backslash, newline/CR/tab, \\b \\f, U+0001, U+001F, DEL, emoji surrogate pair, U+2028/2029, non-ASCII tag, empty content, empty tags, a kind-3825 p+g event; and the spike's Python-verified out/events.jsonl and py-events.jsonl. NostrEventsTests: C# signatures byte-identical to the Python reference for every vector; flipped id, flipped sig bits, tampered content/created_at/kind/tag/pubkey, 31/33/0-byte and off-curve pubkey, uppercase hex and relay junk all fail Verify without throwing. Python reference cross-check: 6 events signed by the build with a fresh key -> verify-events.py (bip340_reference.py) 6 ok, 0 bad (ids <first 8 chars of each>). 'Never decrypted' is enforced by A2's EnvelopeCodec decode order (row 1 before row 3). Stryker 5.0.0 Nip01Serializer+NostrEvents <score> %. CI run <id> (<url>) green."
```

- [ ] **Step 7: Commit and push the evidence**

```bash
git add pipeline/features.json
git commit -m "M0 F-015 F-031 F-032: record mutation scores and evidence" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
git push
```

---

## Spec coverage (acceptance criterion -> where it is proven)

| Feature / criterion | Task | Test or step |
|---|---|---|
| F-029 ci.yml runs tests on push excluding LiveRelay; red then green on a throwaway branch, run ids recorded | 1 | Steps 3-8 |
| F-029 deploy `needs: test` | 1 | Step 2 (static; live proof needs main) |
| F-029 restore on ubuntu without workloads | 1 | Step 3 (green run on `TakGame.Ci.slnf`) |
| F-029 required status check on main | — | owed to the user |
| F-031 csv secret -> pubkey 8/8 | 2 | `KeyTests.SecretKey_DerivesXOnlyPublicKey_FromBip340Vectors`, `Bip340Csv_HasEightSecretKeyRows` |
| F-031 NIP-19 round trip + spec pair | 2 | `Nip19Tests.SpecExamplePair_*`, `RandomKey_RoundTripsThroughNsecAndNpub` |
| F-031 restart loads same key; bad length / out-of-range -> clear error, not overwritten | 6 | `FileKeyStoreTests` (CLI wiring: F-036) |
| F-031 no CryptoSigner in src | 7 | Step 5 `git grep` |
| F-031 pubkeys not 32 bytes / off-curve rejected | 2 | `KeyTests.PublicKey_RejectsPointsNotOnTheCurve`, `PublicKey_RejectsWrongByteLength` |
| F-032 BIP-340 verify vectors (15) | 3 | `SchnorrTests.Verify_MatchesBip340Vectors`, `Csv_Has15VerifyVectorsAnd4SignVectors` |
| F-032 known-good ids for + < > & quotes newline U+0001 DEL emoji | 5 | `Nip01SerializerTests.*` |
| F-032 C#-signed event verifies in the Python reference | 5, 8 | `NostrEventsTests.Sign_ProducesExactlyThePythonReferenceEvent`; Task 8 Step 4 |
| F-032 flipped id / sig bit / tampered content / pubkey length != 32 fail | 5 | `NostrEventsTests.*` ("never decrypted": A2 EnvelopeCodec) |
| F-015 official vectors (35 + 8, message keys, padded len, encrypt/decrypt both directions, long) | 4 | `Nip44VectorTests.*` |
| F-015 two independent keypairs both directions | 4 | `Nip44TwoPeerTests.IndependentPeers_DeriveTheSameKey_AndDecryptEachOther` |
| F-015 third key, MAC bit, version byte, bad padding fail | 4 | `Nip44TwoPeerTests.ThirdKey_*`, `FlippedBit_*`, `WrongVersionByte_*`, `BadPadding_*` |
| F-015 old wrong-reason test deleted | 4 | Step 5 |
| Mutation check >= 80 % per pure module | 8 | Steps 1-3 |
