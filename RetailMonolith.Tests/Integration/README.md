# Integration Tests - TODO

## Current Status
The integration test structure is complete but encounters an EF Core provider conflict when running.

## Problem
When using `WebApplicationFactory<Program>`, both the SqlServer provider (from `Program.cs`) and the InMemory provider (from `TestWebApplicationFactory.cs`) are registered, causing this error:

```
Services for database providers 'Microsoft.EntityFrameworkCore.SqlServer', 
'Microsoft.EntityFrameworkCore.InMemory' have been registered in the service provider. 
Only a single database provider can be registered in a service provider.
```

## Solution Options

### Option 1: Environment-Based Configuration (RECOMMENDED)
Modify `Program.cs` to check for a "Testing" environment and skip SQL Server registration:

```csharp
// In Program.cs, replace the AddDbContext call with:
if (builder.Environment.EnvironmentName != "Testing")
{
    builder.Services.AddDbContext<AppDbContext>(o =>
        o.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection") ??
                       "Server=(localdb)\\MSSQLLocalDB;Database=RetailMonolith;Trusted_Connection=True;MultipleActiveResultSets=true"));
}
```

Then in `TestWebApplicationFactory.cs`, the `UseEnvironment("Testing")` call will prevent SQL Server from being registered.

### Option 2: Manual Database Seeding
Instead of relying on WebApplicationFactory to handle seeding, manually seed the database in each test:

```csharp
[Fact]
public async Task CheckoutApi_SuccessfulCheckout_ReturnsOrderWithPaidStatus()
{
    // Arrange - manually seed
    var product = new Product { /* ... */ };
    var inventory = new InventoryItem { /* ... */ };
    var cart = new Cart { /* ... */ };
    
    using var scope = _factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Products.Add(product);
    db.Inventory.Add(inventory);
    db.Carts.Add(cart);
    await db.SaveChangesAsync();
    
    // Act
    var response = await _client.PostAsync("/api/checkout", null);
    
    // Assert
    response.IsSuccessStatusCode.Should().BeTrue();
}
```

### Option 3: Use Real Database for Integration Tests
Create a test-specific appsettings.Testing.json with a dedicated test database connection string.

## Files Affected
- `/RetailMonolith.Tests/Integration/Helpers/TestWebApplicationFactory.cs`
- `/Program.cs` (if using Option 1)
- Integration test files in `/RetailMonolith.Tests/Integration/Api/`

## Current Workaround
Only unit tests are currently running in CI. The integration tests are skipped until this issue is resolved.
