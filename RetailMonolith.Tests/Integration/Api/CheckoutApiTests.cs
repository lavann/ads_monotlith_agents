using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RetailMonolith.Data;
using RetailMonolith.Models;
using RetailMonolith.Tests.Integration.Helpers;
using Xunit;

namespace RetailMonolith.Tests.Integration.Api;

[Trait("Category", "Integration")]
public class CheckoutApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public CheckoutApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task CheckoutApi_SuccessfulCheckout_ReturnsOrderWithPaidStatus()
    {
        // Arrange - Seed data
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        
        var product = new Product
        {
            Sku = "TEST-001",
            Name = "Test Product",
            Price = 50.00m,
            Currency = "GBP",
            IsActive = true
        };
        db.Products.Add(product);

        var inventory = new InventoryItem { Sku = "TEST-001", Quantity = 10 };
        db.Inventory.Add(inventory);

        var cart = new Cart { CustomerId = "guest" };
        cart.Lines.Add(new CartLine
        {
            Sku = "TEST-001",
            Name = "Test Product",
            UnitPrice = 50.00m,
            Quantity = 2
        });
        db.Carts.Add(cart);
        await db.SaveChangesAsync();

        // Act
        var response = await _client.PostAsync("/api/checkout", null);

        // Assert
        response.IsSuccessStatusCode.Should().BeTrue();
        
        var jsonResponse = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<CheckoutResponse>(jsonResponse, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        result.Should().NotBeNull();
        result!.Status.Should().Be("Paid");
        result.Total.Should().Be(100.00m);
        result.Id.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task CheckoutApi_DecrementsInventory()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        
        var product = new Product
        {
            Sku = "TEST-002",
            Name = "Test Product 2",
            Price = 25.00m,
            Currency = "GBP",
            IsActive = true
        };
        db.Products.Add(product);

        var inventory = new InventoryItem { Sku = "TEST-002", Quantity = 20 };
        db.Inventory.Add(inventory);

        var cart = new Cart { CustomerId = "guest" };
        cart.Lines.Add(new CartLine
        {
            Sku = "TEST-002",
            Name = "Test Product 2",
            UnitPrice = 25.00m,
            Quantity = 5
        });
        db.Carts.Add(cart);
        await db.SaveChangesAsync();

        // Act
        var response = await _client.PostAsync("/api/checkout", null);

        // Assert
        response.IsSuccessStatusCode.Should().BeTrue();
        
        // Verify inventory was decremented
        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var updatedInventory = await verifyDb.Inventory.FindAsync(inventory.Id);
        updatedInventory!.Quantity.Should().Be(15); // 20 - 5
    }

    [Fact]
    public async Task CheckoutApi_ClearsCart()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        
        var product = new Product
        {
            Sku = "TEST-003",
            Name = "Test Product 3",
            Price = 10.00m,
            Currency = "GBP",
            IsActive = true
        };
        db.Products.Add(product);

        var inventory = new InventoryItem { Sku = "TEST-003", Quantity = 10 };
        db.Inventory.Add(inventory);

        var cart = new Cart { CustomerId = "guest" };
        cart.Lines.Add(new CartLine
        {
            Sku = "TEST-003",
            Name = "Test Product 3",
            UnitPrice = 10.00m,
            Quantity = 1
        });
        db.Carts.Add(cart);
        await db.SaveChangesAsync();

        var cartId = cart.Id;

        // Act
        var response = await _client.PostAsync("/api/checkout", null);

        // Assert
        response.IsSuccessStatusCode.Should().BeTrue();
        
        // Verify cart lines were cleared
        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var updatedCart = await verifyDb.Carts.FindAsync(cartId);
        // Cart entity might be removed or lines cleared - both are acceptable
        if (updatedCart != null)
        {
            verifyDb.Entry(updatedCart).Collection(c => c.Lines).Load();
            updatedCart.Lines.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task CheckoutApi_CreatesOrder()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        
        var product = new Product
        {
            Sku = "TEST-004",
            Name = "Test Product 4",
            Price = 75.00m,
            Currency = "GBP",
            IsActive = true
        };
        db.Products.Add(product);

        var inventory = new InventoryItem { Sku = "TEST-004", Quantity = 50 };
        db.Inventory.Add(inventory);

        var cart = new Cart { CustomerId = "guest" };
        cart.Lines.Add(new CartLine
        {
            Sku = "TEST-004",
            Name = "Test Product 4",
            UnitPrice = 75.00m,
            Quantity = 3
        });
        db.Carts.Add(cart);
        await db.SaveChangesAsync();

        // Act
        var response = await _client.PostAsync("/api/checkout", null);

        // Assert
        response.IsSuccessStatusCode.Should().BeTrue();
        
        var jsonResponse = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<CheckoutResponse>(jsonResponse, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        // Verify order was created
        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var order = await verifyDb.Orders.FindAsync(result!.Id);
        order.Should().NotBeNull();
        order!.Total.Should().Be(225.00m);
        order.Status.Should().Be("Paid");
        order.CustomerId.Should().Be("guest");
    }

    private class CheckoutResponse
    {
        public int Id { get; set; }
        public string Status { get; set; } = string.Empty;
        public decimal Total { get; set; }
    }
}
