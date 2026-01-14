using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using RetailMonolith.Data;
using RetailMonolith.Models;
using RetailMonolith.Services;
using Xunit;

namespace RetailMonolith.Tests.Unit.Services;

[Trait("Category", "Unit")]
public class CheckoutServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly Mock<IPaymentGateway> _mockPaymentGateway;
    private readonly CheckoutService _sut;

    public CheckoutServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _db = new AppDbContext(options);
        _mockPaymentGateway = new Mock<IPaymentGateway>();
        _sut = new CheckoutService(_db, _mockPaymentGateway.Object);
    }

    public void Dispose()
    {
        _db.Database.EnsureDeleted();
        _db.Dispose();
    }

    [Fact]
    public async Task CheckoutAsync_SuccessfulPayment_CreatesOrderWithPaidStatus()
    {
        // Arrange
        var cart = new Cart { CustomerId = "guest" };
        cart.Lines.Add(new CartLine
        {
            Sku = "TEST-001",
            Name = "Test Product",
            UnitPrice = 10.00m,
            Quantity = 2
        });
        _db.Carts.Add(cart);

        var inventory = new InventoryItem { Sku = "TEST-001", Quantity = 10 };
        _db.Inventory.Add(inventory);
        await _db.SaveChangesAsync();

        _mockPaymentGateway
            .Setup(x => x.ChargeAsync(It.IsAny<PaymentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentResult(true, "MOCK-12345", null));

        // Act
        var order = await _sut.CheckoutAsync("guest", "tok_test");

        // Assert
        order.Should().NotBeNull();
        order.Status.Should().Be("Paid");
        order.Total.Should().Be(20.00m);
        order.Lines.Should().HaveCount(1);
        order.Lines[0].Sku.Should().Be("TEST-001");
        order.Lines[0].Quantity.Should().Be(2);
    }

    [Fact]
    public async Task CheckoutAsync_SuccessfulPayment_DecrementsInventory()
    {
        // Arrange
        var cart = new Cart { CustomerId = "guest" };
        cart.Lines.Add(new CartLine
        {
            Sku = "TEST-001",
            Name = "Test Product",
            UnitPrice = 10.00m,
            Quantity = 3
        });
        _db.Carts.Add(cart);

        var inventory = new InventoryItem { Sku = "TEST-001", Quantity = 10 };
        _db.Inventory.Add(inventory);
        await _db.SaveChangesAsync();

        _mockPaymentGateway
            .Setup(x => x.ChargeAsync(It.IsAny<PaymentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentResult(true, "MOCK-12345", null));

        // Act
        await _sut.CheckoutAsync("guest", "tok_test");

        // Assert
        var updatedInventory = await _db.Inventory.FirstAsync(i => i.Sku == "TEST-001");
        updatedInventory.Quantity.Should().Be(7); // 10 - 3
    }

    [Fact]
    public async Task CheckoutAsync_SuccessfulPayment_ClearsCart()
    {
        // Arrange
        var cart = new Cart { CustomerId = "guest" };
        cart.Lines.Add(new CartLine
        {
            Sku = "TEST-001",
            Name = "Test Product",
            UnitPrice = 10.00m,
            Quantity = 1
        });
        _db.Carts.Add(cart);

        var inventory = new InventoryItem { Sku = "TEST-001", Quantity = 10 };
        _db.Inventory.Add(inventory);
        await _db.SaveChangesAsync();

        _mockPaymentGateway
            .Setup(x => x.ChargeAsync(It.IsAny<PaymentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentResult(true, "MOCK-12345", null));

        // Act
        await _sut.CheckoutAsync("guest", "tok_test");

        // Assert
        var updatedCart = await _db.Carts.Include(c => c.Lines).FirstAsync(c => c.CustomerId == "guest");
        updatedCart.Lines.Should().BeEmpty();
    }

    [Fact]
    public async Task CheckoutAsync_FailedPayment_CreatesOrderWithFailedStatus()
    {
        // Arrange
        var cart = new Cart { CustomerId = "guest" };
        cart.Lines.Add(new CartLine
        {
            Sku = "TEST-001",
            Name = "Test Product",
            UnitPrice = 10.00m,
            Quantity = 1
        });
        _db.Carts.Add(cart);

        var inventory = new InventoryItem { Sku = "TEST-001", Quantity = 10 };
        _db.Inventory.Add(inventory);
        await _db.SaveChangesAsync();

        _mockPaymentGateway
            .Setup(x => x.ChargeAsync(It.IsAny<PaymentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentResult(false, null, "Insufficient funds"));

        // Act
        var order = await _sut.CheckoutAsync("guest", "tok_test");

        // Assert
        order.Status.Should().Be("Failed");
        order.Total.Should().Be(10.00m);
    }

    [Fact]
    public async Task CheckoutAsync_NoCart_ThrowsInvalidOperationException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.CheckoutAsync("guest", "tok_test")
        );
    }

    [Fact]
    public async Task CheckoutAsync_OutOfStock_ThrowsInvalidOperationException()
    {
        // Arrange
        var cart = new Cart { CustomerId = "guest" };
        cart.Lines.Add(new CartLine
        {
            Sku = "TEST-001",
            Name = "Test Product",
            UnitPrice = 10.00m,
            Quantity = 5
        });
        _db.Carts.Add(cart);

        var inventory = new InventoryItem { Sku = "TEST-001", Quantity = 2 }; // Not enough stock
        _db.Inventory.Add(inventory);
        await _db.SaveChangesAsync();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.CheckoutAsync("guest", "tok_test")
        );
        exception.Message.Should().Contain("Out of stock");
    }

    [Fact]
    public async Task CheckoutAsync_MultipleItems_CalculatesCorrectTotal()
    {
        // Arrange
        var cart = new Cart { CustomerId = "guest" };
        cart.Lines.Add(new CartLine
        {
            Sku = "TEST-001",
            Name = "Product 1",
            UnitPrice = 10.00m,
            Quantity = 2
        });
        cart.Lines.Add(new CartLine
        {
            Sku = "TEST-002",
            Name = "Product 2",
            UnitPrice = 15.50m,
            Quantity = 1
        });
        _db.Carts.Add(cart);

        _db.Inventory.Add(new InventoryItem { Sku = "TEST-001", Quantity = 10 });
        _db.Inventory.Add(new InventoryItem { Sku = "TEST-002", Quantity = 10 });
        await _db.SaveChangesAsync();

        _mockPaymentGateway
            .Setup(x => x.ChargeAsync(It.IsAny<PaymentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentResult(true, "MOCK-12345", null));

        // Act
        var order = await _sut.CheckoutAsync("guest", "tok_test");

        // Assert
        order.Total.Should().Be(35.50m); // (10 * 2) + (15.50 * 1)
        order.Lines.Should().HaveCount(2);
    }

    [Fact]
    public async Task CheckoutAsync_CallsPaymentGatewayWithCorrectAmount()
    {
        // Arrange
        var cart = new Cart { CustomerId = "guest" };
        cart.Lines.Add(new CartLine
        {
            Sku = "TEST-001",
            Name = "Test Product",
            UnitPrice = 12.50m,
            Quantity = 3
        });
        _db.Carts.Add(cart);

        var inventory = new InventoryItem { Sku = "TEST-001", Quantity = 10 };
        _db.Inventory.Add(inventory);
        await _db.SaveChangesAsync();

        _mockPaymentGateway
            .Setup(x => x.ChargeAsync(It.IsAny<PaymentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentResult(true, "MOCK-12345", null));

        // Act
        await _sut.CheckoutAsync("guest", "tok_test");

        // Assert
        _mockPaymentGateway.Verify(
            x => x.ChargeAsync(
                It.Is<PaymentRequest>(r => r.Amount == 37.50m && r.Currency == "GBP" && r.Token == "tok_test"),
                It.IsAny<CancellationToken>()
            ),
            Times.Once
        );
    }
}
