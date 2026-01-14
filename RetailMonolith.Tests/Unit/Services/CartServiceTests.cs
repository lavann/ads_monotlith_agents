using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RetailMonolith.Data;
using RetailMonolith.Models;
using RetailMonolith.Services;
using Xunit;

namespace RetailMonolith.Tests.Unit.Services;

[Trait("Category", "Unit")]
public class CartServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly CartService _sut;

    public CartServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _db = new AppDbContext(options);
        _sut = new CartService(_db);
    }

    public void Dispose()
    {
        _db.Database.EnsureDeleted();
        _db.Dispose();
    }

    [Fact]
    public async Task GetOrCreateCartAsync_NewCustomer_CreatesCart()
    {
        // Arrange
        var customerId = "test-customer";

        // Act
        var cart = await _sut.GetOrCreateCartAsync(customerId);

        // Assert
        cart.Should().NotBeNull();
        cart.CustomerId.Should().Be(customerId);
        cart.Lines.Should().BeEmpty();
        
        var savedCart = await _db.Carts.FirstOrDefaultAsync(c => c.CustomerId == customerId);
        savedCart.Should().NotBeNull();
    }

    [Fact]
    public async Task GetOrCreateCartAsync_ExistingCustomer_ReturnsExistingCart()
    {
        // Arrange
        var customerId = "test-customer";
        var existingCart = new Cart { CustomerId = customerId };
        _db.Carts.Add(existingCart);
        await _db.SaveChangesAsync();

        // Act
        var cart = await _sut.GetOrCreateCartAsync(customerId);

        // Assert
        cart.Should().NotBeNull();
        cart.Id.Should().Be(existingCart.Id);
        cart.CustomerId.Should().Be(customerId);
    }

    [Fact]
    public async Task AddToCartAsync_NewProduct_AddsCartLine()
    {
        // Arrange
        var product = new Product
        {
            Sku = "TEST-001",
            Name = "Test Product",
            Price = 9.99m,
            Currency = "GBP",
            IsActive = true
        };
        _db.Products.Add(product);
        await _db.SaveChangesAsync();

        // Act
        await _sut.AddToCartAsync("guest", product.Id, 1);

        // Assert
        var cart = await _db.Carts
            .Include(c => c.Lines)
            .FirstOrDefaultAsync(c => c.CustomerId == "guest");
        
        cart.Should().NotBeNull();
        cart!.Lines.Should().HaveCount(1);
        cart.Lines[0].Sku.Should().Be("TEST-001");
        cart.Lines[0].Name.Should().Be("Test Product");
        cart.Lines[0].UnitPrice.Should().Be(9.99m);
        cart.Lines[0].Quantity.Should().Be(1);
    }

    [Fact]
    public async Task AddToCartAsync_ExistingProduct_IncrementsQuantity()
    {
        // Arrange
        var product = new Product
        {
            Sku = "TEST-001",
            Name = "Test Product",
            Price = 9.99m,
            Currency = "GBP",
            IsActive = true
        };
        _db.Products.Add(product);
        await _db.SaveChangesAsync();

        // Add product first time
        await _sut.AddToCartAsync("guest", product.Id, 2);

        // Act - Add same product again
        await _sut.AddToCartAsync("guest", product.Id, 3);

        // Assert
        var cart = await _db.Carts
            .Include(c => c.Lines)
            .FirstOrDefaultAsync(c => c.CustomerId == "guest");
        
        cart.Should().NotBeNull();
        cart!.Lines.Should().HaveCount(1);
        cart.Lines[0].Quantity.Should().Be(5); // 2 + 3
    }

    [Fact]
    public async Task AddToCartAsync_InvalidProduct_ThrowsException()
    {
        // Arrange
        var invalidProductId = 999;

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.AddToCartAsync("guest", invalidProductId, 1)
        );
    }

    [Fact]
    public async Task GetCartWithLinesAsync_ExistingCart_ReturnsCartWithLines()
    {
        // Arrange
        var cart = new Cart { CustomerId = "guest" };
        var cartLine = new CartLine
        {
            Sku = "TEST-001",
            Name = "Test Product",
            UnitPrice = 9.99m,
            Quantity = 2
        };
        cart.Lines.Add(cartLine);
        _db.Carts.Add(cart);
        await _db.SaveChangesAsync();

        // Act
        var result = await _sut.GetCartWithLinesAsync("guest");

        // Assert
        result.Should().NotBeNull();
        result.Lines.Should().HaveCount(1);
        result.Lines[0].Sku.Should().Be("TEST-001");
    }

    [Fact]
    public async Task GetCartWithLinesAsync_NoCart_ReturnsNewCartInstance()
    {
        // Act
        var result = await _sut.GetCartWithLinesAsync("guest");

        // Assert
        result.Should().NotBeNull();
        result.CustomerId.Should().Be("guest");
        result.Lines.Should().BeEmpty();
        result.Id.Should().Be(0); // Not saved to database
    }

    [Fact]
    public async Task ClearCartAsync_ExistingCart_RemovesCart()
    {
        // Arrange
        var cart = new Cart { CustomerId = "guest" };
        var cartLine = new CartLine
        {
            Sku = "TEST-001",
            Name = "Test Product",
            UnitPrice = 9.99m,
            Quantity = 2
        };
        cart.Lines.Add(cartLine);
        _db.Carts.Add(cart);
        await _db.SaveChangesAsync();

        // Act
        await _sut.ClearCartAsync("guest");

        // Assert
        var result = await _db.Carts.FirstOrDefaultAsync(c => c.CustomerId == "guest");
        result.Should().BeNull();
    }

    [Fact]
    public async Task ClearCartAsync_NoCart_DoesNotThrow()
    {
        // Act & Assert - Should not throw
        await _sut.ClearCartAsync("guest");
    }
}
