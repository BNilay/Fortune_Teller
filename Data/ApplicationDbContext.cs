using fortune.Models;
using Microsoft.EntityFrameworkCore;
namespace fortune.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        // ✅ bunu ekle:
        public DbSet<Kart> Kartlar { get; set; }
    }

}
