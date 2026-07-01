using System.Security.Cryptography;
using System.Text;

namespace TechVisionMaroc.Services;

/// <summary>TOTP RFC 6238 (compatible Google Authenticator / Authy / Microsoft Authenticator).</summary>
public static class TotpService
{
    private const int Digits = 6;
    private const int PeriodeSec = 30;

    public static string GenererSecret(int bytes = 20)
    {
        var raw = RandomNumberGenerator.GetBytes(bytes);
        return Base32Encode(raw);
    }

    public static string GenererUriQrCode(string secret, string compte, string emetteur)
    {
        var c = Uri.EscapeDataString(compte);
        var e = Uri.EscapeDataString(emetteur);
        return $"otpauth://totp/{e}:{c}?secret={secret}&issuer={e}&algorithm=SHA1&digits={Digits}&period={PeriodeSec}";
    }

    public static bool VerifierCode(string secret, string code, int fenetre = 1)
    {
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(code)) return false;
        code = code.Replace(" ", "").Trim();
        var t = (long)Math.Floor((DateTimeOffset.UtcNow - DateTimeOffset.UnixEpoch).TotalSeconds / PeriodeSec);
        var key = Base32Decode(secret);
        for (int dec = -fenetre; dec <= fenetre; dec++)
        {
            if (CalculerCode(key, t + dec) == code) return true;
        }
        return false;
    }

    private static string CalculerCode(byte[] key, long compteur)
    {
        var buf = BitConverter.GetBytes(compteur);
        if (BitConverter.IsLittleEndian) Array.Reverse(buf);
        using var hmac = new HMACSHA1(key);
        var hash = hmac.ComputeHash(buf);
        var offset = hash[^1] & 0x0F;
        var bin = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        var code = bin % (int)Math.Pow(10, Digits);
        return code.ToString().PadLeft(Digits, '0');
    }

    // Base32 (RFC 4648) — utilisé par les apps authenticator
    private const string Base32Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    private static string Base32Encode(byte[] data)
    {
        var sb = new StringBuilder();
        int bits = 0, val = 0;
        foreach (var b in data)
        {
            val = (val << 8) | b; bits += 8;
            while (bits >= 5)
            {
                sb.Append(Base32Chars[(val >> (bits - 5)) & 0x1F]);
                bits -= 5;
            }
        }
        if (bits > 0) sb.Append(Base32Chars[(val << (5 - bits)) & 0x1F]);
        return sb.ToString();
    }

    private static byte[] Base32Decode(string s)
    {
        s = s.Trim().TrimEnd('=').ToUpperInvariant();
        var output = new List<byte>(s.Length * 5 / 8);
        int bits = 0, val = 0;
        foreach (var ch in s)
        {
            var i = Base32Chars.IndexOf(ch);
            if (i < 0) continue;
            val = (val << 5) | i; bits += 5;
            if (bits >= 8) { output.Add((byte)((val >> (bits - 8)) & 0xFF)); bits -= 8; }
        }
        return output.ToArray();
    }
}
