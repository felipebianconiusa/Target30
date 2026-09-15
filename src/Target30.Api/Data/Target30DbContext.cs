using Microsoft.EntityFrameworkCore;
using Target30.Api.Models;

namespace Target30.Api.Data;

public class Target30DbContext(DbContextOptions<Target30DbContext> options) : DbContext(options)
{
    public DbSet<PlaidItem> PlaidItems => Set<PlaidItem>();
}
