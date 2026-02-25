using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using ShiftSoftware.ShiftIdentity.Data.Cosmos.Options;
using ShiftSoftware.ShiftIdentity.Data.Cosmos.Services;

namespace ShiftSoftware.ShiftIdentity.Data.Cosmos.Extensions;

public static class IServiceCollectionExtensions
{
    public static IServiceCollection AddIdentityReferenceCosmosDataService<TCosmosClient>(
        this IServiceCollection services,
        Action<IdentityReferenceCosmosDataOptions>? setupAction = null)
        where TCosmosClient : CosmosClient
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));

        if (setupAction is not null)
            services.Configure(setupAction);

        services.AddScoped<IIdentityReferenceCosmosDataService, IdentityReferenceCosmosDataService<TCosmosClient>>();

        return services;
    }
}
