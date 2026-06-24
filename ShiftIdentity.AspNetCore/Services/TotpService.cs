using Net.Codecrete.QrCodeGenerator;
using OtpNet;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.Models;
using System.Security.Cryptography;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Services;

public class TotpService
{
    private readonly TotpSettingsModel settings;

    public TotpService(ShiftIdentityConfiguration config)
    {
        this.settings = config.MfaSettings.Totp;
    }

    private byte[] GenerateSecretKey()
    {
        return RandomNumberGenerator.GetBytes(20);
    }

    public (string Secret, string Uri) GenerateSecret(string user)
    {
        var digits = settings.Digits;
        var period = settings.Period;
        var issuer = settings.IssuerName == null ? user : $"{settings.IssuerName} ({user})";
        var secretKey = GenerateSecretKey();
        var uri = new OtpUri(OtpType.Totp, secretKey, issuer, digits: digits, period: period).ToString();

        return (Base32Encoding.ToString(secretKey), uri);
    }

    public string GenerateQrCode(string uri)
    {
        var qr = QrCode.EncodeText(uri, QrCode.Ecc.Medium);
        return qr.ToSvgString(4);
    }

    public bool Validate(string code, byte[] secretKey)
    {
        var totp = new Totp(secretKey, settings.Period, totpSize: settings.Digits);
        var window = new VerificationWindow(settings.VerificationWindowPast, settings.VerificationWindowFuture);
        return totp.VerifyTotp(code, out long _, window);
    }

    public bool Validate(string code, string secret)
    {
        return Validate(code, DecodeSecret(secret));
    }

    // Converts a Base32-encoded shared secret (as embedded in the otpauth:// URI) into the raw key bytes.
    public byte[] DecodeSecret(string secret)
    {
        return Base32Encoding.ToBytes(secret);
    }
}
