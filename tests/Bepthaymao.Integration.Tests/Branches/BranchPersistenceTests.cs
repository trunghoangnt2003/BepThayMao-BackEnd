using Bepthaymao.Infrastructure.Persistence;
using Bepthaymao.Integration.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Bepthaymao.Integration.Tests.Branches;

[Collection(InfrastructureGroup.Name)]
public sealed class BranchPersistenceTests(InfrastructureFixture infrastructure)
{
    [Fact]
    public async Task DatabaseRejectsCloseTimeNotAfterOpenTime()
    {
        string database = await infrastructure.CreateDatabaseAsync();
        await using AppDbContext context = InfrastructureFixture.CreateContext(database);
        Guid id = Guid.CreateVersion7();
        TimeOnly open = new(21, 30);
        TimeOnly close = new(6, 0);

        PostgresException exception = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlAsync(
            $"INSERT INTO branches (id, name, address, phone, open_time, close_time, is_active, created_at) VALUES ({id}, {"Cơ sở"}, {"Số 1"}, {"02800000001"}, {open}, {close}, {true}, {DateTimeOffset.UtcNow})",
            TestContext.Current.CancellationToken));

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Equal("ck_branches_open_before_close", exception.ConstraintName);
    }
}
