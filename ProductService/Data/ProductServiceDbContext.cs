using Microsoft.EntityFrameworkCore;
using ProductService.Models;

namespace ProductService.Data
{
    public class ProductServiceDbContext : DbContext
    {
        public ProductServiceDbContext(DbContextOptions<ProductServiceDbContext> options) : base(options)
        {
        }

        public DbSet<Product> Products => Set<Product>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Product>().HasIndex(p => p.Sku).IsUnique();
        }
    }
}
