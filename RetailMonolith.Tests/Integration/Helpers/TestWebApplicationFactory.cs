using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailMonolith.Data;

namespace RetailMonolith.Tests.Integration.Helpers;

public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        
        builder.ConfigureServices(services =>
        {
            // Remove existing DbContext Options
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType.IsGenericType &&
                     d.ServiceType.GetGenericTypeDefinition() == typeof(DbContextOptions<>) &&
                     d.ServiceType.GenericTypeArguments[0] == typeof(AppDbContext));
            if (descriptor != null)
            {
                services.Remove(descriptor);
            }

            // Remove DbContextOptions<AppDbContext>
            descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (descriptor != null)
            {
                services.Remove(descriptor);
            }

            // Remove DbContext
            descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(AppDbContext));
            if (descriptor != null)
            {
                services.Remove(descriptor);
            }

            // Add InMemory DbContext for testing
            services.AddDbContext<AppDbContext>(options =>
            {
                options.UseInMemoryDatabase($"InMemoryDbForTesting_{Guid.NewGuid()}");
            });
        });
    }
}
