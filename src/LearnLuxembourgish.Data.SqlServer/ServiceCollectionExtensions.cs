using LearnLuxembourgish.Data.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LearnLuxembourgish.Data.SqlServer;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSqlServerDataStore(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<LearnLuxembourgishDbContext>(options =>
            options.UseSqlServer(connectionString,
                b => b.MigrationsAssembly("LearnLuxembourgish.Data.SqlServer")));
        return services;
    }
}
