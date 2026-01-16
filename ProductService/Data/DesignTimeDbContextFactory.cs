using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ProductService.Data
{
    public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ProductServiceDbContext>
    {
        public ProductServiceDbContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<ProductServiceDbContext>();
            optionsBuilder.UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=ProductServiceDB;Trusted_Connection=True;MultipleActiveResultSets=true");
            return new ProductServiceDbContext(optionsBuilder.Options);
        }
    }
}
