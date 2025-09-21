using System.Data;
using System.Data.Common;
using System.Threading;

#pragma warning disable CS8765
#pragma warning disable CA2007

namespace CineBoutique.Inventory.Api.Tests.Infrastructure;

public sealed class ConnectionCounter
{
    private int _commandCount;

    public int CommandCount => Volatile.Read(ref _commandCount);

    public void Increment() => Interlocked.Increment(ref _commandCount);

    public void Reset() => Interlocked.Exchange(ref _commandCount, 0);
}

internal sealed class CountingDbConnection : DbConnection
{
    private readonly DbConnection _inner;
    private readonly ConnectionCounter _counter;

    public CountingDbConnection(DbConnection inner, ConnectionCounter counter)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _counter = counter ?? throw new ArgumentNullException(nameof(counter));
    }

    public override string ConnectionString
    {
        get => _inner.ConnectionString;
        set => _inner.ConnectionString = value;
    }

    public override string Database => _inner.Database;

    public override string DataSource => _inner.DataSource;

    public override string ServerVersion => _inner.ServerVersion;

    public override ConnectionState State => _inner.State;

    public override void ChangeDatabase(string databaseName) => _inner.ChangeDatabase(databaseName);

    public override void Close() => _inner.Close();

    public override void Open() => _inner.Open();

    public override Task OpenAsync(CancellationToken cancellationToken) => _inner.OpenAsync(cancellationToken);

    public override async ValueTask DisposeAsync()
    {
        await _inner.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }

    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => _inner.BeginTransaction(isolationLevel);

    protected override DbCommand CreateDbCommand()
    {
        _counter.Increment();
        return _inner.CreateCommand();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }

    public override void EnlistTransaction(System.Transactions.Transaction? transaction) => _inner.EnlistTransaction(transaction);
}
