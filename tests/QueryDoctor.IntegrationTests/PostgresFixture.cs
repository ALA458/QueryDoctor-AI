using Microsoft.EntityFrameworkCore;
using QueryDoctor.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace QueryDoctor.IntegrationTests;

/// <summary>
/// Un PostgreSQL desechable por corrida, con la misma imagen del compose (ADR-03) y todas las migraciones aplicadas.
/// Cada prueba trabaja con datos propios (servidores distintos) para no depender del orden.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("pgvector/pgvector:pg16")
        .WithDatabase("querydoctor_it")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString).Options);

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition(Name)]
public sealed class PostgresGroup : ICollectionFixture<PostgresFixture>
{
    public const string Name = "PostgreSQL";
}
