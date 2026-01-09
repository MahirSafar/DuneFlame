using DuneFlame.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace DuneFlame.UnitTests;

public class DbContextTests
{
    [Fact]
    public void DbContext_Should_Initialize_And_Build_Model_Successfully()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString()) // Test üçün yaddaşda baza
            .Options;

        // Act
        using var context = new AppDbContext(options);

        // Assert
        // Bu metod bazaya qoşulmağı və modeli yaratmağı yoxlayır
        context.Database.EnsureCreated().Should().BeTrue();

        // Entity-lərin mövcudluğunu yoxlayaq
        context.Users.Should().NotBeNull();
        context.Products.Should().NotBeNull();
    }
}
