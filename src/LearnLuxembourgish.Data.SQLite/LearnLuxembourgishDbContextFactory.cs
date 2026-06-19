using LearnLuxembourgish.Data.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LearnLuxembourgish.Data.SQLite;

public class LearnLuxembourgishDbContextFactory : IDesignTimeDbContextFactory<LearnLuxembourgishDbContext>
{
    public LearnLuxembourgishDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<LearnLuxembourgishDbContext>();
        optionsBuilder.UseSqlite("Data Source=learnluxembourgish.db",
            b => b.MigrationsAssembly("LearnLuxembourgish.Data.SQLite"));

        return new LearnLuxembourgishDbContext(optionsBuilder.Options);
    }
}
