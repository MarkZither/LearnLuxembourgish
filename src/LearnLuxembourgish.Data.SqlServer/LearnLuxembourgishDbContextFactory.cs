using LearnLuxembourgish.Data.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LearnLuxembourgish.Data.SqlServer;

public class LearnLuxembourgishDbContextFactory : IDesignTimeDbContextFactory<LearnLuxembourgishDbContext>
{
    public LearnLuxembourgishDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<LearnLuxembourgishDbContext>();
        optionsBuilder.UseSqlServer(
            "Server=(localdb)\\mssqllocaldb;Database=learnluxembourgish;Trusted_Connection=True;",
            b => b.MigrationsAssembly("LearnLuxembourgish.Data.SqlServer"));

        return new LearnLuxembourgishDbContext(optionsBuilder.Options);
    }
}
