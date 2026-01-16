using Microsoft.EntityFrameworkCore;
using ProductService.Data;
using ProductService.Models;
using Serilog;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Distributed;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

builder.Host.UseSerilog();

// Add services to the container
builder.Services.AddDbContext<ProductServiceDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Add Redis cache
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("Redis");
    options.InstanceName = "Products:";
});

// Add health checks
builder.Services.AddHealthChecks()
    .AddDbContextCheck<ProductServiceDbContext>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

// Configure JSON options to handle reference loops
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
});

var app = builder.Build();

// Apply migrations and seed data on startup
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ProductServiceDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    
    try
    {
        logger.LogInformation("Applying database migrations...");
        await dbContext.Database.MigrateAsync();
        logger.LogInformation("Database migrations applied successfully");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error applying database migrations");
    }
}

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseSerilogRequestLogging();

// Health check endpoint
app.MapHealthChecks("/health");

// Product API endpoints
app.MapGet("/api/products", async (ProductServiceDbContext db, Microsoft.Extensions.Caching.Distributed.IDistributedCache cache, ILogger<Program> logger) =>
{
    try
    {
        // Try to get from cache
        var cachedProducts = await cache.GetStringAsync("all-products");
        if (!string.IsNullOrEmpty(cachedProducts))
        {
            logger.LogInformation("Returning products from cache");
            var products = System.Text.Json.JsonSerializer.Deserialize<List<Product>>(cachedProducts);
            return Results.Ok(products);
        }

        // If not in cache, get from database
        logger.LogInformation("Cache miss, fetching products from database");
        var productsFromDb = await db.Products.Where(p => p.IsActive).ToListAsync();
        
        // Cache the result for 1 hour
        var options = new Microsoft.Extensions.Caching.Distributed.DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1)
        };
        await cache.SetStringAsync("all-products", System.Text.Json.JsonSerializer.Serialize(productsFromDb), options);
        
        return Results.Ok(productsFromDb);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error retrieving products");
        return Results.Problem("Error retrieving products");
    }
})
.WithName("GetProducts")
.WithOpenApi();

app.MapGet("/api/products/{id:int}", async (int id, ProductServiceDbContext db, Microsoft.Extensions.Caching.Distributed.IDistributedCache cache, ILogger<Program> logger) =>
{
    try
    {
        var cacheKey = $"product-{id}";
        var cachedProduct = await cache.GetStringAsync(cacheKey);
        
        if (!string.IsNullOrEmpty(cachedProduct))
        {
            logger.LogInformation("Returning product {ProductId} from cache", id);
            var product = System.Text.Json.JsonSerializer.Deserialize<Product>(cachedProduct);
            return product is not null ? Results.Ok(product) : Results.NotFound();
        }

        logger.LogInformation("Cache miss, fetching product {ProductId} from database", id);
        var productFromDb = await db.Products.FindAsync(id);
        
        if (productFromDb is null)
        {
            return Results.NotFound();
        }

        // Cache for 1 hour
        var options = new Microsoft.Extensions.Caching.Distributed.DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1)
        };
        await cache.SetStringAsync(cacheKey, System.Text.Json.JsonSerializer.Serialize(productFromDb), options);
        
        return Results.Ok(productFromDb);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error retrieving product {ProductId}", id);
        return Results.Problem("Error retrieving product");
    }
})
.WithName("GetProductById")
.WithOpenApi();

app.MapGet("/api/products/sku/{sku}", async (string sku, ProductServiceDbContext db, Microsoft.Extensions.Caching.Distributed.IDistributedCache cache, ILogger<Program> logger) =>
{
    try
    {
        var cacheKey = $"product-sku-{sku}";
        var cachedProduct = await cache.GetStringAsync(cacheKey);
        
        if (!string.IsNullOrEmpty(cachedProduct))
        {
            logger.LogInformation("Returning product with SKU {Sku} from cache", sku);
            var product = System.Text.Json.JsonSerializer.Deserialize<Product>(cachedProduct);
            return product is not null ? Results.Ok(product) : Results.NotFound();
        }

        logger.LogInformation("Cache miss, fetching product with SKU {Sku} from database", sku);
        var productFromDb = await db.Products.FirstOrDefaultAsync(p => p.Sku == sku);
        
        if (productFromDb is null)
        {
            return Results.NotFound();
        }

        // Cache for 1 hour
        var options = new Microsoft.Extensions.Caching.Distributed.DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1)
        };
        await cache.SetStringAsync(cacheKey, System.Text.Json.JsonSerializer.Serialize(productFromDb), options);
        
        return Results.Ok(productFromDb);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error retrieving product with SKU {Sku}", sku);
        return Results.Problem("Error retrieving product");
    }
})
.WithName("GetProductBySku")
.WithOpenApi();

app.Run();
