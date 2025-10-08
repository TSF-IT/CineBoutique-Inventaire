using System;
using System.Data;
using System.Data.Common;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Tests.Infrastructure;
using CineBoutique.Inventory.Infrastructure.Database;
using Microsoft.Extensions.DependencyInjection;

namespace CineBoutique.Inventory.Api.Tests.Infrastructure;

public abstract class InventoryApiTestBase : IAsyncLifetime
{
    private readonly PostgresTestContainerFixture _postgres;
    private IServiceScope? _scope;

    protected InventoryApiTestBase(PostgresTestContainerFixture postgres)
    {
        _postgres = postgres;
    }

    protected InventoryApiApplicationFactory Factory { get; private set; } = default!;

    protected HttpClient Client { get; private set; } = default!;

    protected IDbConnection Connection { get; private set; } = default!;

    protected InventoryTestDataBuilder Data { get; private set; } = default!;

    public virtual async Task InitializeAsync()
    {
        Factory = new InventoryApiApplicationFactory(_postgres.ConnectionString);
        await Factory.EnsureMigratedAsync().ConfigureAwait(false);

        Client = Factory.CreateClient();
        _scope = Factory.Services.CreateScope();
        var connectionFactory = _scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        Connection = connectionFactory.CreateConnection();
        await EnsureConnectionOpenAsync(Connection).ConfigureAwait(false);
        await DatabaseCleaner.CleanAsync(Connection).ConfigureAwait(false);
        Data = await InventoryTestDataBuilder.CreateAsync(Connection).ConfigureAwait(false);
    }

    public virtual async Task DisposeAsync()
    {
        if (Connection is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        }
        else
        {
            Connection.Dispose();
        }

        _scope?.Dispose();
        Client.Dispose();
        Factory.Dispose();
    }

    protected Task ResetDatabaseAsync() => DatabaseCleaner.CleanAsync(Connection);

    private static async Task EnsureConnectionOpenAsync(IDbConnection connection)
    {
        switch (connection)
        {
            case DbConnection dbConnection when dbConnection.State != ConnectionState.Open:
                await dbConnection.OpenAsync().ConfigureAwait(false);
                break;
            case { State: ConnectionState.Closed }:
                connection.Open();
                break;
        }
    }
}
