using LearnLuxembourgish.Data.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LearnLuxembourgish.Data.PostgreSQL;

public class LearnLuxembourgishDbContextFactory : IDesignTimeDbContextFactory<LearnLuxembourgishDbContext>
{
    public LearnLuxembourgishDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<LearnLuxembourgishDbContext>();
        optionsBuilder.UseNpgsql(
            "Host=localhost;Database=learnluxembourgish;Username=postgres;Password=postgres",
            b => b.MigrationsAssembly("LearnLuxembourgish.Data.PostgreSQL"));

        return new LearnLuxembourgishDbContext(optionsBuilder.Options);
    }
}
