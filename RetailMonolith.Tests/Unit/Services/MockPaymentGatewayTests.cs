using FluentAssertions;
using RetailMonolith.Services;
using Xunit;

namespace RetailMonolith.Tests.Unit.Services;

[Trait("Category", "Unit")]
public class MockPaymentGatewayTests
{
    private readonly MockPaymentGateway _sut;

    public MockPaymentGatewayTests()
    {
        _sut = new MockPaymentGateway();
    }

    [Fact]
    public async Task ChargeAsync_AlwaysSucceeds()
    {
        // Arrange
        var request = new PaymentRequest(100.00m, "GBP", "tok_test");

        // Act
        var result = await _sut.ChargeAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Succeeded.Should().BeTrue();
        result.Error.Should().BeNull();
    }

    [Fact]
    public async Task ChargeAsync_ReturnsProviderReference()
    {
        // Arrange
        var request = new PaymentRequest(50.00m, "GBP", "tok_test");

        // Act
        var result = await _sut.ChargeAsync(request);

        // Assert
        result.ProviderRef.Should().NotBeNullOrEmpty();
        result.ProviderRef.Should().StartWith("MOCK-");
    }

    [Fact]
    public async Task ChargeAsync_DifferentCalls_ReturnDifferentReferences()
    {
        // Arrange
        var request1 = new PaymentRequest(100.00m, "GBP", "tok_test1");
        var request2 = new PaymentRequest(200.00m, "GBP", "tok_test2");

        // Act
        var result1 = await _sut.ChargeAsync(request1);
        var result2 = await _sut.ChargeAsync(request2);

        // Assert
        result1.ProviderRef.Should().NotBe(result2.ProviderRef);
    }

    [Theory]
    [InlineData(0.01, "GBP", "tok_min")]
    [InlineData(999999.99, "USD", "tok_max")]
    [InlineData(50.00, "EUR", "tok_euro")]
    public async Task ChargeAsync_VariousAmountsAndCurrencies_AlwaysSucceeds(decimal amount, string currency, string token)
    {
        // Arrange
        var request = new PaymentRequest(amount, currency, token);

        // Act
        var result = await _sut.ChargeAsync(request);

        // Assert
        result.Succeeded.Should().BeTrue();
        result.ProviderRef.Should().StartWith("MOCK-");
    }
}
