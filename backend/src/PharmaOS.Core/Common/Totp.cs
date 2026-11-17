using System.Security.Cryptography;

namespace PharmaOS.Core.Common;

/// <summary>RFC 6238 time-based one-time passwords (30 s step, 6 digits, HMAC-SHA1) — compatible with common authenticator apps.</summary>
public static class Totp
{
    public static byte[] NewSecret() => RandomNumberGenerator.GetBytes(20);

    public static string Code(byte[] secret, DateTimeOffset at)
    {
        var counter = at.ToUnixTimeSeconds() / 30;
        Span<byte> msg = stackalloc byte[8];
        for (var i = 7; i >= 0; i--) { msg[i] = (byte)(counter & 0xFF); counter >>= 8; }
        var hash = HMACSHA1.HashData(secret, msg);
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6");
    }

    /// <summary>Accepts the current step and one step either side (clock drift).</summary>
    public static bool Verify(byte[] secret, string? code, DateTimeOffset now)
    {
        if (code is null || code.Length != 6 || !code.All(char.IsDigit)) return false;
        for (var d = -1; d <= 1; d++)
            if (CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(Code(secret, now.AddSeconds(d * 30))), System.Text.Encoding.ASCII.GetBytes(code)))
                return true;
        return false;
    }

    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Base32(byte[] data)
    {
        var sb = new System.Text.StringBuilder();
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b; bits += 8;
            while (bits >= 5) { sb.Append(Alphabet[(buffer >> (bits - 5)) & 31]); bits -= 5; }
        }
        if (bits > 0) sb.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        return sb.ToString();
    }

    public static byte[] FromBase32(string s)
    {
        var bytes = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var ch in s.TrimEnd('=').ToUpperInvariant())
        {
            var v = Alphabet.IndexOf(ch);
            if (v < 0) throw new FormatException("Invalid base32.");
            buffer = (buffer << 5) | v; bits += 5;
            if (bits >= 8) { bytes.Add((byte)((buffer >> (bits - 8)) & 0xFF)); bits -= 8; }
        }
        return bytes.ToArray();
    }

    public static string Uri(string issuer, string account, byte[] secret) =>
        $"otpauth://totp/{System.Uri.EscapeDataString(issuer)}:{System.Uri.EscapeDataString(account)}?secret={Base32(secret)}&issuer={System.Uri.EscapeDataString(issuer)}&digits=6&period=30";
}

/// <summary>Encrypts small secrets at rest (MFA seeds). Implemented in the API with ASP.NET Data Protection.</summary>
public interface ISecretProtector
{
    string Protect(byte[] secret);
    byte[] Unprotect(string protectedValue);
}
