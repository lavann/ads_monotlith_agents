using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RetailMonolith.Data;
using RetailMonolith.Models;
using RetailMonolith.Tests.Integration.Helpers;
using Xunit;

namespace RetailMonolith.Tests.Integration.Api;

[Trait("Category", "Integration")]
public class OrdersApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public OrdersApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetOrder_ExistingOrder_ReturnsOrderWithLines()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        
        var order = new Order
        {
            CustomerId = "guest",
            Status = "Paid",
            Total = 150.00m,
            CreatedUtc = DateTime.UtcNow
        };
        order.Lines.Add(new OrderLine
        {
            Sku = "TEST-001",
            Name = "Test Product",
            UnitPrice = 50.00m,
            Quantity = 3
        });
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        var orderId = order.Id;

        // Act
        var response = await _client.GetAsync($"/api/orders/{orderId}");

        // Assert
        response.IsSuccessStatusCode.Should().BeTrue();
        
        var jsonResponse = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<OrderResponse>(jsonResponse, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        result.Should().NotBeNull();
        result!.Id.Should().Be(orderId);
        result.Status.Should().Be("Paid");
        result.Total.Should().Be(150.00m);
        result.CustomerId.Should().Be("guest");
        result.Lines.Should().HaveCount(1);
        result.Lines[0].Sku.Should().Be("TEST-001");
        result.Lines[0].Quantity.Should().Be(3);
    }

    [Fact]
    public async Task GetOrder_NonExistentOrder_ReturnsNotFound()
    {
        // Arrange
        var nonExistentOrderId = 99999;

        // Act
        var response = await _client.GetAsync($"/api/orders/{nonExistentOrderId}");

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetOrder_MultipleLines_ReturnsAllLines()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        
        var order = new Order
        {
            CustomerId = "guest",
            Status = "Paid",
            Total = 275.00m,
            CreatedUtc = DateTime.UtcNow
        };
        order.Lines.Add(new OrderLine
        {
            Sku = "TEST-001",
            Name = "Product 1",
            UnitPrice = 100.00m,
            Quantity = 2
        });
        order.Lines.Add(new OrderLine
        {
            Sku = "TEST-002",
            Name = "Product 2",
            UnitPrice = 75.00m,
            Quantity = 1
        });
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        var orderId = order.Id;

        // Act
        var response = await _client.GetAsync($"/api/orders/{orderId}");

        // Assert
        response.IsSuccessStatusCode.Should().BeTrue();
        
        var jsonResponse = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<OrderResponse>(jsonResponse, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        result.Should().NotBeNull();
        result!.Lines.Should().HaveCount(2);
        result.Lines.Should().Contain(l => l.Sku == "TEST-001" && l.Quantity == 2);
        result.Lines.Should().Contain(l => l.Sku == "TEST-002" && l.Quantity == 1);
    }

    private class OrderResponse
    {
        public int Id { get; set; }
        public DateTime CreatedUtc { get; set; }
        public string CustomerId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public decimal Total { get; set; }
        public List<OrderLineResponse> Lines { get; set; } = new();
    }

    private class OrderLineResponse
    {
        public int Id { get; set; }
        public int OrderId { get; set; }
        public string Sku { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public decimal UnitPrice { get; set; }
        public int Quantity { get; set; }
    }
}
