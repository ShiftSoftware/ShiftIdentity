using Microsoft.EntityFrameworkCore;
using ShiftSoftware.ShiftIdentity.Data.Entities;

namespace ShiftSoftware.ShiftIdentity.Data.Authentication;

/// <summary>
/// Explicit model configuration for the foundation's isolated hosts. Production contexts do not
/// call this until every issuer and sensitive writer has adopted the new authority boundary.
/// </summary>
public static class IdentitySecurityModel
{
    public static void ConfigureIdentitySecurity(this ModelBuilder builder)
    {
        builder.Entity<UserSecurityState>(e =>
        {
            e.ToTable("UserSecurityStates", "ShiftIdentity", t =>
                t.HasCheckConstraint("CK_UserSecurityState_Version", "[SecurityVersion] >= 1 AND [FactorGeneration] >= 1 AND [FailedProofs] >= 0"));
            e.HasKey(x => x.UserID);
            e.HasOne<User>().WithOne().HasForeignKey<UserSecurityState>(x => x.UserID).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.RowVersion).IsRowVersion();
        });
        builder.Entity<AuthenticationOperation>(e =>
        {
            e.ToTable("AuthenticationOperations", "ShiftIdentity", t =>
            {
                t.HasCheckConstraint("CK_AuthenticationOperation_State", "[State] IN (1,2,3) AND [Purpose] IN (1,2,3)");
                t.HasCheckConstraint("CK_AuthenticationOperation_Version", "[SecurityVersion] >= 1 AND [FactorGeneration] >= 1 AND [PolicyRevision] >= 1 AND [FailedAttempts] BETWEEN 0 AND 5 AND [ExpiresAt] > [CreatedAt]");
            });
            e.HasKey(x => x.ID);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserID).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.ClientID).HasMaxLength(255);
            e.Property(x => x.Audience).HasMaxLength(255);
            e.Property(x => x.HandleDigest).HasMaxLength(32);
            e.Property(x => x.CodeChallenge).HasMaxLength(43);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasIndex(x => new { x.ExpiresAt, x.State });
            e.HasIndex(x => new { x.UserID, x.Purpose, x.State });
        });
        builder.Entity<AuthenticationPolicyState>(e =>
        {
            e.ToTable("AuthenticationPolicyStates", "ShiftIdentity", t =>
            {
                t.HasCheckConstraint("CK_AuthenticationPolicyState", "[ID] = 1 AND [Revision] >= 1");
                t.HasCheckConstraint("CK_AuthenticationPolicyState_Totp", "[TotpDigits] BETWEEN 6 AND 8 AND [TotpPeriodSeconds] BETWEEN 1 AND 300 AND [TotpWindowPast] BETWEEN 0 AND 2 AND [TotpWindowFuture] BETWEEN 0 AND 2");
            });
            e.HasKey(x => x.ID);
            e.Property(x => x.ID).ValueGeneratedNever();
            e.Property(x => x.RowVersion).IsRowVersion();
        });
        builder.Entity<AuthenticationAuditEvent>(e =>
        {
            e.ToTable("AuthenticationAuditEvents", "ShiftIdentity");
            e.HasKey(x => x.ID);
            e.Property(x => x.Outcome).HasMaxLength(80);
            e.HasIndex(x => x.CreatedAt);
        });
    }
}
