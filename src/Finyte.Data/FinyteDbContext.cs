using Microsoft.EntityFrameworkCore;

namespace Finyte.Data;

public class FinyteDbContext(DbContextOptions<FinyteDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
    }
}
