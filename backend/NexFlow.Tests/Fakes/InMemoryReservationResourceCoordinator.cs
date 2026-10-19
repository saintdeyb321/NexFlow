using System.Collections.Concurrent;
using System.Text.Json;
using System.Transactions;
using NexFlow.Application.Features.Reservations;
using NexFlow.Domain.Exceptions;

namespace NexFlow.Tests.Fakes;

// Deterministic SQL-boundary double shared by independent API/worker objects.
// Semaphores model row locks ONLY in tests; they do not certify PostgreSQL locks.
public sealed class InMemoryReservationResourceCoordinator : IReservationResourceCoordinator
{
    public sealed class Store
    {
        internal readonly ConcurrentDictionary<ReservationResourceKey, Row> Rows = new();
        public Func<ReservationResourceKey, ReservationResourceChange, bool> HasFuture { get; set; } = (_, _) => false;
    }
    internal sealed class Row
    {
        public SemaphoreSlim Gate { get; } = new(1);
        public long Version; public ReservationResourceMutation? Pending;
    }
    public Store Durable { get; }
    public Func<Task>? BeforeValidate { get; set; }
    public Func<ReservationResourceMutation, Exception?>? BeforeBeginCommit { get; set; }
    public Func<ReservationResourceMutation, Exception?>? AfterBeginCommit { get; set; }
    public Func<ReservationResourceMutation, Exception?>? BeforeComplete { get; set; }
    public InMemoryReservationResourceCoordinator(Store? store = null) => Durable = store ?? new();

    public async Task<ReservationResourceStamp> ReadAsync(ReservationResourceKey key, CancellationToken ct)
    {
        var row = Durable.Rows.GetOrAdd(key, _ => new()); await row.Gate.WaitAsync(ct);
        try { return new(key, row.Version, row.Pending?.OperationId); } finally { row.Gate.Release(); }
    }
    public async Task<ReservationResourceMutation> BeginAsync(ReservationResourceStamp observed, ReservationResourceChange change, CancellationToken ct)
    {
        var row = Durable.Rows[observed.Key]; await row.Gate.WaitAsync(ct);
        try
        {
            var json = JsonSerializer.Serialize(change);
            if (row.Pending != null)
            {
                if (row.Pending.PayloadJson != json) throw new ConcurrencyException("Resource pending.");
                return row.Pending;
            }
            if (row.Version != observed.Version) throw new ConcurrencyException("Stale resource observation.");
            if (Durable.HasFuture(observed.Key, change)) throw new ConcurrencyException("Future reservations.");
            var operation = new ReservationResourceMutation(observed.Key, row.Version + 1, Guid.NewGuid(), json);
            var failure = BeforeBeginCommit?.Invoke(operation); if (failure != null) throw failure;
            row.Version = operation.Version; row.Pending = operation;
            failure = AfterBeginCommit?.Invoke(operation); if (failure != null) throw failure;
            return operation;
        }
        finally { row.Gate.Release(); }
    }
    public async Task ValidateAsync(IReadOnlyList<ReservationResourceStamp> observed, CancellationToken ct)
    {
        if (BeforeValidate != null) await BeforeValidate();
        var acquired = new List<SemaphoreSlim>();
        try
        {
            foreach (var stamp in observed.OrderBy(s => s.Key.Kind).ThenBy(s => s.Key.Id, StringComparer.Ordinal))
            {
                var row = Durable.Rows[stamp.Key]; await row.Gate.WaitAsync(ct); acquired.Add(row.Gate);
                if (row.Pending != null || row.Version != stamp.Version || stamp.PendingOperationId != null)
                    throw new ConcurrencyException("Resource changed.");
            }
            var tx = Transaction.Current ?? throw new InvalidOperationException("SQL transaction required.");
            tx.EnlistVolatile(new ReleaseOnCompletion(acquired.ToArray()), EnlistmentOptions.None);
            acquired.Clear();
        }
        finally { foreach (var gate in acquired) gate.Release(); }
    }
    public async Task CompleteAsync(ReservationResourceMutation operation, CancellationToken ct)
    {
        var failure = BeforeComplete?.Invoke(operation); if (failure != null) throw failure;
        var row = Durable.Rows[operation.Key]; await row.Gate.WaitAsync(ct);
        try { if (row.Pending?.OperationId == operation.OperationId && row.Version == operation.Version) row.Pending = null; }
        finally { row.Gate.Release(); }
    }
    public Task<IReadOnlyList<ReservationResourceMutation>> ClaimPendingAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ReservationResourceMutation>>(Durable.Rows.Values.Where(r => r.Pending != null).Select(r => r.Pending!).ToArray());

    private sealed class ReleaseOnCompletion(SemaphoreSlim[] gates) : IEnlistmentNotification
    {
        private void Finish(Enlistment enlistment) { foreach (var gate in gates) gate.Release(); enlistment.Done(); }
        public void Prepare(PreparingEnlistment preparingEnlistment) => preparingEnlistment.Prepared();
        public void Commit(Enlistment enlistment) => Finish(enlistment);
        public void Rollback(Enlistment enlistment) => Finish(enlistment);
        public void InDoubt(Enlistment enlistment) => Finish(enlistment);
    }
}
