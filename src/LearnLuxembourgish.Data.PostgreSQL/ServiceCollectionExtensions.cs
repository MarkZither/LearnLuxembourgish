using LearnLuxembourgish.Data.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LearnLuxembourgish.Data.PostgreSQL;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPostgreSQLDataStore(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<LearnLuxembourgishDbContext>(options =>
            options.UseNpgsql(connectionString));
        return services;
    }
}
