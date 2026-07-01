namespace DigitalBrain.Core;

// Publisher-trust decision for gated publishing. A pack is trusted iff it carries a valid author signature
// (integrity — code untampered since signing) AND its author public key is on the trusted allowlist.
// VerifyPack alone does NOT establish publisher trust: a stranger's self-signed pack passes integrity but
// must still fail the allowlist. Unsigned or tampered packs fail VerifyPack and are never trusted.
public static class PublisherTrust
{
    public static bool IsTrusted(NeuroPack pack, IReadOnlyCollection<string> trustedPublisherKeys)
    {
        if (!PackSignatureVerifier.VerifyPack(pack)) return false;
        return trustedPublisherKeys.Contains(pack.AuthorPublicKeyBase64);
    }
}
