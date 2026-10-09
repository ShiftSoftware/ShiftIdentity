using Microsoft.EntityFrameworkCore;
using ShiftSoftware.ShiftIdentity.Data.Entities;

namespace ShiftSoftware.ShiftIdentity.Data.Authentication;

/// <summary>
/// The authority's schema: security state, operations, policy, audit, throttle and device sign-in tables in the ShiftIdentity schema.
/// <see cref="ShiftIdentityDbContext"/> configures it for every host, so a host's next migration carries the additive
/// expansion whether or not it has enabled the authority; the tables stay empty until it does. Calling this again on a
/// model that already has the schema is a no-op, so a context that configured it explicitly before keeps working.
/// </summary>
public static class IdentitySecurityModel
{
    public static void ConfigureIdentitySecurity(this ModelBuilder builder)
    {
        if (builder.Model.FindEntityType(typeof(UserSecurityState))?.FindPrimaryKey() is not null) return;
        builder.Entity<UserSecurityState>(e =>
        {
            e.ToTable("UserSecurityStates", "ShiftIdentity", t =>
                t.HasCheckConstraint("CK_UserSecurityState_Version", "[SecurityVersion] >= 1 AND [FactorGeneration] >= 1 AND [FailedProofs] >= 0 AND [TotpProtectionVersion] IN (0,1)"));
            e.HasKey(x => x.UserID);
            e.HasOne<User>().WithOne(x => x.SecurityState).HasForeignKey<UserSecurityState>(x => x.UserID).OnDelete(DeleteBehavior.Restrict);
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
                t.HasCheckConstraint("CK_AuthenticationOperation_State", "[State] IN (1,2,3,4,5,6,7,8,9,10,11,12) AND [Purpose] IN (1,2,3,4,5,6,7,8,9,10,11,12,13,14)");
                t.HasCheckConstraint("CK_AuthenticationOperation_App", "([State] <> 11 OR ([Purpose] = 10 AND [External] = 1 AND [AppBinding] IS NOT NULL AND [SessionAuthenticatedAt] IS NOT NULL AND [SessionMfaSatisfied] IS NOT NULL)) AND ([Purpose] <> 10 OR [State] IN (2,3,6,9,11))");
                t.HasCheckConstraint("CK_AuthenticationOperation_LegacyRefresh", "[Purpose] <> 11 OR ([State] = 2 AND [External] = 0 AND DATALENGTH([HandleDigest]) = 32 AND [CompletedAt] IS NOT NULL)");
                // A pre-cutover MFA row is keyed by its credential digest for its whole life: pending, locked, completed or cancelled.
                t.HasCheckConstraint("CK_AuthenticationOperation_LegacyMfa", "[Purpose] <> 12 OR ([State] IN (1,2,3,6) AND [External] = 0 AND DATALENGTH([HandleDigest]) = 32 AND [CodeChallenge] = '' AND ([State] = 1 OR [CompletedAt] IS NOT NULL))");
                t.HasCheckConstraint("CK_AuthenticationOperation_SessionCompatibility", "[SessionLegacyCompatibilityExpiresAt] IS NULL OR ([Purpose] = 10 AND [State] = 11)");
                t.HasCheckConstraint("CK_AuthenticationOperation_Link", "([State] <> 10 OR [Purpose] IN (7,8,9)) AND ([OutstandingLinkSlot] IS NULL OR ([Purpose] IN (7,8,9) AND [State] = 10))");
                t.HasCheckConstraint("CK_AuthenticationOperation_Password", "([PasswordChangeOrigin] IS NULL OR [PasswordChangeOrigin] IN (1,2)) AND (([PendingPasswordHash] IS NULL AND [PendingPasswordSalt] IS NULL) OR ([Purpose] = 4 AND [State] IN (1,7) AND [PendingPasswordHash] IS NOT NULL AND [PendingPasswordSalt] IS NOT NULL))");
                t.HasCheckConstraint("CK_AuthenticationOperation_Factor", "[ProtectedPendingTotpSecret] IS NULL OR ([Purpose] IN (3,4,5,6) AND [State] = 7)");
                t.HasCheckConstraint("CK_AuthenticationOperation_Recovery", "([RecoveryCodeDigest] IS NULL OR ([Purpose] = 6 AND [State] = 8 AND [ParentID] IS NULL)) AND ([OutstandingRecoveryUserID] IS NULL OR ([Purpose] = 6 AND [ParentID] IS NULL AND [OutstandingRecoveryUserID] = [UserID]))");
                // A provider proof continues only into a sign-in step (login MFA or enrollment) or an app code; it is pending only as a provider completion.
                t.HasCheckConstraint("CK_AuthenticationOperation_Provider", "([SessionProvider] IS NULL OR ([SessionProvider] IN (1, 2) AND [Purpose] IN (1,3,10,14))) AND ([State] <> 12 OR ([Purpose] = 14 AND [SessionProvider] IS NOT NULL)) AND ([Purpose] <> 14 OR [State] IN (2,3,6,12))");
                t.HasCheckConstraint("CK_AuthenticationOperation_Version", "[SecurityVersion] >= 1 AND [FactorGeneration] >= 1 AND [PolicyRevision] >= 1 AND [FailedAttempts] BETWEEN 0 AND 5 AND [ExpiresAt] > [CreatedAt]");
            });
            e.HasKey(x => x.ID);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserID).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.ClientID).HasMaxLength(255);
            e.Property(x => x.Audience).HasMaxLength(255);
            e.Property(x => x.HandleDigest).HasMaxLength(32);
            e.Property(x => x.CodeChallenge).HasMaxLength(128);
            e.Property(x => x.SourceSessionDigest).HasMaxLength(32);
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
        builder.Entity<UserProviderLink>(e =>
        {
            e.ToTable("UserProviderLinks", "ShiftIdentity", t => t.HasCheckConstraint("CK_UserProviderLink", "[Provider] IN (1, 2) AND DATALENGTH([TenantID]) > 0 AND DATALENGTH([ObjectID]) > 0 AND DATALENGTH([EmailLookupKey]) > 0"));
            e.HasKey(x => x.ID);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserID).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.TenantID).HasMaxLength(64).UseCollation("Latin1_General_100_BIN2");
            e.Property(x => x.ObjectID).HasMaxLength(255).UseCollation("Latin1_General_100_BIN2");
            e.Property(x => x.EmailLookupKey).HasMaxLength(255).UseCollation("Latin1_General_100_BIN2");
            e.Property(x => x.Email).HasMaxLength(255);
            // One provider identity reaches one account. An account may have several.
            e.HasIndex(x => new { x.Provider, x.TenantID, x.ObjectID }).IsUnique();
            e.HasIndex(x => x.UserID);
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
        builder.Entity<DeviceAuthorization>(e =>
        {
            e.ToTable("DeviceAuthorizations", "ShiftIdentity", t =>
            {
                t.HasCheckConstraint("CK_DeviceAuthorization_State", "[State] IN (1,2,3,4,5) AND [ExpiresAt] > [CreatedAt] AND [Interval] BETWEEN 1 AND 3600 AND ([SessionProvider] IS NULL OR [SessionProvider] IN (1, 2))");
                // Pending and approved rows hold both codes; consumed and expired rows hold neither; a denied row keeps
                // them until its deadline so that the device keeps hearing access_denied.
                t.HasCheckConstraint("CK_DeviceAuthorization_Codes", "([State] IN (1,2) AND DATALENGTH([DeviceCodeDigest]) = 32 AND DATALENGTH([UserCodeDigest]) = 32) OR ([State] IN (4,5) AND [DeviceCodeDigest] IS NULL AND [UserCodeDigest] IS NULL) OR ([State] = 3 AND ((DATALENGTH([DeviceCodeDigest]) = 32 AND DATALENGTH([UserCodeDigest]) = 32) OR ([DeviceCodeDigest] IS NULL AND [UserCodeDigest] IS NULL)))");
                // A pending row has no account. An approved or consumed row has the account and everything pinned at approval.
                t.HasCheckConstraint("CK_DeviceAuthorization_Account", "([State] = 1 AND [UserID] IS NULL AND [ApprovedAt] IS NULL) OR ([State] IN (2,4) AND [UserID] IS NOT NULL AND [SecurityVersion] >= 1 AND [PolicyRevision] >= 1 AND [FactorGeneration] >= 1 AND [MfaSatisfied] IS NOT NULL AND [AuthenticatedAt] IS NOT NULL AND [ApprovedAt] IS NOT NULL) OR [State] IN (3,5)");
            });
            e.HasKey(x => x.ID);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserID).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.DeviceCodeDigest).HasMaxLength(32);
            e.Property(x => x.UserCodeDigest).HasMaxLength(32);
            e.Property(x => x.ClientID).HasMaxLength(64);
            e.Property(x => x.Audience).HasMaxLength(255);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasIndex(x => x.UserCodeDigest).IsUnique().HasFilter("[UserCodeDigest] IS NOT NULL");
            e.HasIndex(x => new { x.ExpiresAt, x.State });
            e.HasIndex(x => x.UserID);
        });
    }
}
