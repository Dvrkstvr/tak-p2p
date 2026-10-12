using System;
using System.Collections.Generic;
using System.Text.Json;
using TakEngine.Transport;
using TakEngine.Transport.Nostr;
using Xunit;

namespace TakEngine.Transport.Tests;

public class NostrMessageTests
{
    [Fact]
    public void Nip01Serializer_ComputeId_ReturnsSha256Hex()
    {
        var evt = new NostrEvent
        {
            Pubkey = "3bf0c63fcb93463407af97b5e0918838e64edd9d071293ad011663f1d7b6af94",
            CreatedAt = 1726110499,
            Kind = 4,
            Tags = [["p", "opponent_pubkey_hex"]],
            Content = "test content"
        };

        string id = Nip01Serializer.ComputeId(evt);

        Assert.Equal(64, id.Length);
        Assert.Matches("^[0-9a-f]{64}$", id);
    }
}
