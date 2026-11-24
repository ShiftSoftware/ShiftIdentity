using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using ShiftIdentity.Tests.Infrastructure;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Extentsions;
using ShiftSoftware.ShiftIdentity.Data.IRepositories;
using ShiftSoftware.ShiftIdentity.Data.Repositories;
using Xunit;

namespace ShiftIdentity.Tests;

/// <summary>
/// The dashboard's registration gives a scope one user repository, whichever of its two names a class asks for. The
/// User form's upsert hook asks the repository it was given for a verification send, and only that repository's save
/// hands the send on: a second repository in the same scope would save the user without it. No database is involved.
/// </summary>
public sealed class UserRepositoryRegistrationTests
{
    private static ShiftIdentityConfiguration Configuration()
    {
        using var rsa = RSA.Create(2048);

        return new()
        {
            Token = new() { Issuer = "https://identity.invalid", Audience = "registration-test", ExpireSeconds = 600,
                RSAPrivateKeyBase64 = Convert.ToBase64String(rsa.ExportRSAPrivateKey()) },
            RefreshToken = new() { Issuer = "https://identity.invalid", Audience = "registration-refresh", ExpireSeconds = 3600,
                Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)) },
            TemporaryTokenSettings = new() { Issuer = "https://identity.invalid", Audience = "registration-temporary", ExpireSeconds = 300,
                Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)) },
            Security = new(), SASToken = new() { Key = "sas", ExpiresInSeconds = 3600 },
            HashIdSettings = new() { AcceptUnencodedIds = true, UserIdsSalt = "synthetic", UserIdsMinHashLength = 5 },
            MfaSettings = new() { Enabled = false }, ActionTrees = [],
        };
    }

    [Fact]
    public async Task A_scope_has_one_user_repository_for_both_of_its_names_and_another_scope_has_its_own()
    {
        var configuration = Configuration();
        using var rsa = RSA.Create(2048);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalization();
        services.AddSingleton<IHashIdService>(new HashIdService(Options.Create(new ShiftEntityOptions())));

        // What a repository needs to be built, which a web host's own registration brings.
        services.AddSingleton(Substitute.For<ICurrentUserProvider>());
        services.AddScoped<IdentityClaimProvider>();
        services.AddSingleton(Substitute.For<IDefaultDataLevelAccess>());
        services.AddDbContext<SqliteIdentityTestDbContext>(options => options.UseSqlite("DataSource=:memory:"));
        services.AddControllers()
            .AddShiftIdentity(configuration.Token.Issuer, Convert.ToBase64String(rsa.ExportRSAPublicKey()))
            .AddShiftIdentityDashboard<SqliteIdentityTestDbContext>(configuration);

        await using (var provider = services.BuildServiceProvider(validateScopes: true))
        {
            await using var scope = provider.CreateAsyncScope();
            var repository = scope.ServiceProvider.GetRequiredService<UserRepository>();
            Assert.Same(repository, scope.ServiceProvider.GetRequiredService<IUserRepository>());

            await using var other = provider.CreateAsyncScope();
            var ofTheOther = other.ServiceProvider.GetRequiredService<IUserRepository>();
            Assert.NotSame(repository, ofTheOther);
            Assert.Same(ofTheOther, other.ServiceProvider.GetRequiredService<UserRepository>());
        }

        // The class is registered once, and the interface resolves to it.
        Assert.Single(services, service => service.ServiceType == typeof(UserRepository));
    }
}
