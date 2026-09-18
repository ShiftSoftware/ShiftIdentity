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
                t.HasCheckConstraint("CK_UserSecurityState_Version", "[SecurityVersion] >= 1 AND [FactorGeneration] >= 1 AND [FailedProofs] >= 0 AND [TotpProtectionVersion] IN (0,1)"));
            e.HasKey(x => x.UserID);
            e.HasOne<User>().WithOne().HasForeignKey<UserSecurityState>(x => x.UserID).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.Property(x => x.TotpProtectionVersion).HasDefaultValue(0);
            e.Property(x => x.ContactRevision).HasDefaultValue(1L);
            e.Property(x => x.RecoveryEmailProvenance).HasDefaultValue(RecoveryEmailProvenance.Unknown);
            e.Property(x => x.DeliveryCount).HasDefaultValue(0);
            e.Property(x => x.UsernameLookupKey).HasMaxLength(255).UseCollation("Latin1_General_100_BIN2");
            e.Property(x => x.EmailLookupKey).HasMaxLength(255).UseCollation("Latin1_General_100_BIN2");
            e.HasIndex(x => x.UsernameLookupKey).HasDatabaseName("IX_UserSecurityStates_UsernameLookupKey").IsUnique().HasFilter("[UsernameLookupKey] IS NOT NULL");
            e.HasIndex(x => x.EmailLookupKey).HasDatabaseName("IX_UserSecurityStates_EmailLookupKey").IsUnique().HasFilter("[EmailLookupKey] IS NOT NULL");
            e.ToTable(t => t.HasCheckConstraint("CK_UserSecurityState_Lookup", "([UsernameLookupKey] IS NULL AND [EmailLookupKey] IS NULL) OR ([UsernameLookupKey] IS NOT NULL AND DATALENGTH([UsernameLookupKey]) > 0 AND ([EmailLookupKey] IS NULL OR DATALENGTH([EmailLookupKey]) > 0))"));
            e.Property(x => x.RecoveryEmail).HasMaxLength(255);
            e.ToTable(t => t.HasCheckConstraint("CK_UserSecurityState_ContactDelivery", "[ContactRevision] >= 1 AND [DeliveryCount] >= 0 AND [RecoveryEmailProvenance] BETWEEN 0 AND 4"));
        });
        builder.Entity<AuthenticationOperation>(e =>
        {
            e.ToTable("AuthenticationOperations", "ShiftIdentity", t =>
            {
                t.HasCheckConstraint("CK_AuthenticationOperation_State", "[State] IN (1,2,3,4,5,6,7,8,9,10,11) AND [Purpose] IN (1,2,3,4,5,6,7,8,9,10,11,12)");
                t.HasCheckConstraint("CK_AuthenticationOperation_App", "([State] <> 11 OR ([Purpose] = 10 AND [External] = 1 AND [AppBinding] IS NOT NULL AND [SessionAuthenticatedAt] IS NOT NULL AND [SessionMfaSatisfied] IS NOT NULL)) AND ([Purpose] <> 10 OR [State] IN (2,3,6,9,11))");
                t.HasCheckConstraint("CK_AuthenticationOperation_LegacyRefresh", "[Purpose] <> 11 OR ([State] = 2 AND [External] = 0 AND DATALENGTH([HandleDigest]) = 32 AND [CompletedAt] IS NOT NULL)");
                // A pre-cutover MFA row is keyed by its credential digest for its whole life: pending, locked, completed or cancelled.
                t.HasCheckConstraint("CK_AuthenticationOperation_LegacyMfa", "[Purpose] <> 12 OR ([State] IN (1,2,3,6) AND [External] = 0 AND DATALENGTH([HandleDigest]) = 32 AND [CodeChallenge] = '' AND ([State] = 1 OR [CompletedAt] IS NOT NULL))");
                t.HasCheckConstraint("CK_AuthenticationOperation_SessionCompatibility", "[SessionLegacyCompatibilityExpiresAt] IS NULL OR ([Purpose] = 10 AND [State] = 11)");
                t.HasCheckConstraint("CK_AuthenticationOperation_Link", "([State] <> 10 OR [Purpose] IN (7,8,9)) AND ([OutstandingLinkSlot] IS NULL OR ([Purpose] IN (7,8,9) AND [State] = 10))");
                t.HasCheckConstraint("CK_AuthenticationOperation_Password", "([PasswordChangeOrigin] IS NULL OR [PasswordChangeOrigin] IN (1,2)) AND (([PendingPasswordHash] IS NULL AND [PendingPasswordSalt] IS NULL) OR ([Purpose] = 4 AND [State] IN (1,7) AND [PendingPasswordHash] IS NOT NULL AND [PendingPasswordSalt] IS NOT NULL))");
                t.HasCheckConstraint("CK_AuthenticationOperation_Factor", "[ProtectedPendingTotpSecret] IS NULL OR ([Purpose] IN (3,4,5,6) AND [State] = 7)");
                t.HasCheckConstraint("CK_AuthenticationOperation_Recovery", "([RecoveryCodeDigest] IS NULL OR ([Purpose] = 6 AND [State] = 8 AND [ParentID] IS NULL)) AND ([OutstandingRecoveryUserID] IS NULL OR ([Purpose] = 6 AND [ParentID] IS NULL AND [OutstandingRecoveryUserID] = [UserID]))");
                t.HasCheckConstraint("CK_AuthenticationOperation_Version", "[SecurityVersion] >= 1 AND [FactorGeneration] >= 1 AND [PolicyRevision] >= 1 AND [FailedAttempts] BETWEEN 0 AND 5 AND [ExpiresAt] > [CreatedAt]");
            });
            e.HasKey(x => x.ID);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserID).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.ClientID).HasMaxLength(255);
            e.Property(x => x.Audience).HasMaxLength(255);
            e.Property(x => x.HandleDigest).HasMaxLength(32);
            e.Property(x => x.CodeChallenge).HasMaxLength(128);
            e.Property(x => x.AppBinding).HasMaxLength(64);
            e.Property(x => x.PendingPasswordHash).HasMaxLength(256);
            e.Property(x => x.PendingPasswordSalt).HasMaxLength(128);
            e.Property(x => x.ProtectedPendingTotpSecret).HasMaxLength(1024);
            e.Property(x => x.RecoveryCodeDigest).HasMaxLength(32);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.Property(x => x.Destination).HasMaxLength(255);
            e.Property(x => x.OutstandingLinkSlot).HasMaxLength(80);
            e.HasIndex(x => x.OutstandingLinkSlot).IsUnique().HasFilter("[OutstandingLinkSlot] IS NOT NULL");
            e.HasIndex(x => new { x.ExpiresAt, x.State });
            e.HasIndex(x => new { x.UserID, x.Purpose, x.State });
            e.HasIndex(x => x.HandleDigest).IsUnique().HasFilter("[Purpose] IN (11,12)");
            e.HasIndex(x => x.ParentID);
            e.HasIndex(x => x.OutstandingRecoveryUserID).IsUnique().HasFilter("[OutstandingRecoveryUserID] IS NOT NULL");
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
            e.Property(x => x.VerificationReference).HasMaxLength(200);
            e.HasIndex(x => x.CreatedAt);
        });
        builder.Entity<AuthThrottleBucket>(e =>
        {
            e.ToTable("AuthThrottleBuckets", "ShiftIdentity", t => t.HasCheckConstraint("CK_AuthThrottleBucket", "[Count] >= 0"));
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(64);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasIndex(x => x.WindowStart);
        });
    }
}
