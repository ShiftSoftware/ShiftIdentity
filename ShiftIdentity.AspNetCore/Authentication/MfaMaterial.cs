using System.Security.Cryptography;
using Net.Codecrete.QrCodeGenerator;
using OtpNet;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Data.Authentication;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;

/// <summary>Protects factor material separately from replicated users and binds new ciphertext to its owner.</summary>
internal static class MfaMaterial
{
    internal static NewAuthenticatorSetup Prepare(IdentityAdmissionServices services, IdentitySecurityTransaction unit, AuthenticationOperation op)
    {
        var secret = RandomNumberGenerator.GetBytes(20);
        try
        {
            op.ProtectedPendingTotpSecret = PendingProtector(services, op).Protect(secret);
            var uri = new OtpUri(OtpType.Totp, secret, unit.User.Username, "Shift Identity", OtpHashMode.Sha1,
                digits: unit.Policy.TotpDigits, period: unit.Policy.TotpPeriodSeconds).ToString();
            return new(Base32Encoding.ToString(secret), uri, QrCode.EncodeText(uri, QrCode.Ecc.Medium).ToSvgString(4));
        }
        finally { CryptographicOperations.ZeroMemory(secret); }
    }

    internal static byte[] ReadPending(IdentityAdmissionServices services, AuthenticationOperation op) =>
        PendingProtector(services, op).Unprotect(op.ProtectedPendingTotpSecret ?? throw new CryptographicException("Missing pending factor."));

    internal static byte[] ReadActive(IdentityAdmissionServices services, UserSecurityState security) =>
        ReadActive(services.FactorProtector, security);

    internal static byte[] ReadActive(IdentityMaterialProtector protector, UserSecurityState security) =>
        security.TotpProtectionVersion == 1
            ? ActiveProtector(protector, security).Unprotect(security.ProtectedTotpSecret ?? throw new CryptographicException("Missing active factor."))
            : throw new CryptographicException("Unknown factor protection format.");

    internal static void ProtectActive(IdentityMaterialProtector protector, UserSecurityState security, byte[] secret)
    {
        security.ProtectedTotpSecret = ActiveProtector(protector, security).Protect(secret);
        security.TotpProtectionVersion = 1;
    }

    internal static void Activate(IdentityAdmissionServices services, UserSecurityState security, byte[] secret, long step)
    {
        security.FactorGeneration = checked(security.FactorGeneration + 1);
        ProtectActive(services.FactorProtector, security, secret);
        security.LastAcceptedTotpStep = step;
    }

    private static IdentityMaterialProtector ActiveProtector(IdentityMaterialProtector protector, UserSecurityState security) =>
        protector.CreateProtector(FormattableString.Invariant($"Active.v1:{security.UserID}:{security.FactorGeneration}"));

    private static IdentityMaterialProtector PendingProtector(IdentityAdmissionServices services, AuthenticationOperation op) =>
        services.FactorProtector.CreateProtector(FormattableString.Invariant($"Pending.v1:{op.ID:N}:{op.UserID}:{op.SecurityVersion}:{op.FactorGeneration}:{op.PolicyRevision}:{(int)op.Purpose}"));
}
