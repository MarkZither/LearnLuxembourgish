using LearnLuxembourgish.Data.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LearnLuxembourgish.Data.SQLite;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSQLiteDataStore(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<LearnLuxembourgishDbContext>(options =>
            options.UseSqlite(connectionString, b => b.MigrationsAssembly("LearnLuxembourgish.Data.SQLite")));
        return services;
    }
}
