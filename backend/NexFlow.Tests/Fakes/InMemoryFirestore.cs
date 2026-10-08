using Google.Cloud.Firestore;
using Google.Cloud.Firestore.V1;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Value = Google.Cloud.Firestore.V1.Value;
using Timestamp = Google.Protobuf.WellKnownTypes.Timestamp;
using WriteResult = Google.Cloud.Firestore.V1.WriteResult;

namespace NexFlow.Tests.Fakes;

// Runs the real Firestore SDK serializers and repositories against an optimistic in-memory RPC transport.
// There is no emulator, credential lookup, database connection or external request.
public sealed class InMemoryFirestore : CallInvoker
{
    private readonly object _sync = new();
    private readonly Dictionary<string, Document> _documents = new();
    private readonly Dictionary<ByteString, Dictionary<string, long>> _transactions = new();
    private readonly Dictionary<string, long> _versions = new();
    private long _revision;
    private (string Path, int Remaining, TaskCompletionSource Gate)? _readBarrier;
    public int AbortedCommits { get; private set; }
    public Func<CommitRequest, Exception?>? BeforeCommit { get; set; }
    public Func<CommitRequest, Exception?>? AfterCommit { get; set; }
    public FirestoreDb Db { get; }

    public InMemoryFirestore() => Db = FirestoreDb.Create("pdf-unit-tests", new FirestoreClientBuilder { CallInvoker = this }.Build());

    public void SynchronizeReads(string relativePath, int readers = 2)
    {
        lock (_sync) _readBarrier = (FullPath(relativePath), readers, new(TaskCreationOptions.RunContinuationsAsynchronously));
    }

    public string FullPath(string relative) => $"projects/pdf-unit-tests/databases/(default)/documents/{relative}";
    public Document? Read(string relative)
    {
        lock (_sync) return _documents.GetValueOrDefault(FullPath(relative))?.Clone();
    }
    public void Seed(string relative, IDictionary<string, Value> fields)
    {
        lock (_sync)
        {
            var doc = new Document { Name = FullPath(relative) };
            foreach (var field in fields) doc.Fields.Add(field.Key, field.Value.Clone());
            Store(doc);
        }
    }

    private Timestamp Now() => Timestamp.FromDateTime(DateTime.UtcNow.AddTicks(++_revision * 10));
    private void Store(Document doc)
    {
        doc.UpdateTime = Now();
        doc.CreateTime ??= doc.UpdateTime;
        _documents[doc.Name] = doc;
        _versions[doc.Name] = _revision;
    }

    private object Unary(object request)
    {
        lock (_sync)
        {
            switch (request)
            {
                case BeginTransactionRequest:
                    var id = ByteString.CopyFromUtf8(Guid.NewGuid().ToString());
                    _transactions.Add(id, new());
                    return new BeginTransactionResponse { Transaction = id };
                case RollbackRequest rollback:
                    _transactions.Remove(rollback.Transaction);
                    return new Empty();
                case CommitRequest commit:
                    var failure = BeforeCommit?.Invoke(commit);
                    if (failure != null) throw failure;
                    if (!commit.Transaction.IsEmpty && _transactions.TryGetValue(commit.Transaction, out var reads)
                        && reads.Any(r => _versions.GetValueOrDefault(r.Key) != r.Value))
                    {
                        AbortedCommits++;
                        throw new RpcException(new Status(StatusCode.Aborted, "Concurrent transaction"));
                    }
                    foreach (var write in commit.Writes)
                    {
                        if (write.OperationCase == Write.OperationOneofCase.Delete)
                        {
                            _documents.Remove(write.Delete);
                            _versions[write.Delete] = ++_revision;
                            continue;
                        }
                        if (write.OperationCase != Write.OperationOneofCase.Update) throw new NotSupportedException("Unexpected write");
                        var doc = write.Update.Clone();
                        if (write.UpdateMask != null)
                        {
                            doc = _documents.GetValueOrDefault(doc.Name)?.Clone() ?? new Document { Name = doc.Name };
                            foreach (var path in write.UpdateMask.FieldPaths)
                            {
                                if (write.Update.Fields.TryGetValue(path, out var value)) doc.Fields[path] = value.Clone();
                                else doc.Fields.Remove(path);
                            }
                        }
                        Store(doc);
                    }
                    _transactions.Remove(commit.Transaction);
                    var result = new CommitResponse { CommitTime = Now() };
                    foreach (var write in commit.Writes) result.WriteResults.Add(new WriteResult { UpdateTime = result.CommitTime });
                    failure = AfterCommit?.Invoke(commit);
                    if (failure != null) throw failure;
                    return result;
                default: throw new NotSupportedException(request.GetType().Name);
            }
        }
    }

    private (IReadOnlyList<object> Responses, Task Gate) Stream(object request)
    {
        lock (_sync)
        {
            var results = new List<object>();
            Task gate = Task.CompletedTask;
            if (request is BatchGetDocumentsRequest batch)
            {
                foreach (var name in batch.Documents)
                {
                    if (!batch.Transaction.IsEmpty) _transactions[batch.Transaction][name] = _versions.GetValueOrDefault(name);
                    var response = new BatchGetDocumentsResponse { ReadTime = Now() };
                    if (_documents.TryGetValue(name, out var doc)) response.Found = doc.Clone();
                    else response.Missing = name;
                    results.Add(response);
                    if (!batch.Transaction.IsEmpty && _readBarrier is { } barrier && barrier.Path == name)
                    {
                        gate = barrier.Gate.Task;
                        if (--barrier.Remaining == 0) { barrier.Gate.SetResult(); _readBarrier = null; }
                        else _readBarrier = barrier;
                    }
                }
            }
            else if (request is RunQueryRequest query)
            {
                var structured = query.StructuredQuery;
                var prefix = query.Parent + "/" + structured.From[0].CollectionId + "/";
                var filter = structured.Where?.FieldFilter;
                var docs = _documents.Values.Where(d => d.Name.StartsWith(prefix, StringComparison.Ordinal)
                    && !d.Name[prefix.Length..].Contains('/'));
                if (filter != null) docs = docs.Where(d => d.Fields.TryGetValue(filter.Field.FieldPath, out var value)
                    && value.TimestampValue != null && value.TimestampValue.ToDateTime() <= filter.Value.TimestampValue.ToDateTime());
                if (structured.Limit != null) docs = docs.Take(structured.Limit.Value);
                results.AddRange(docs.Select(d => (object)new RunQueryResponse { Document = d.Clone(), ReadTime = Now() }));
                if (results.Count == 0) results.Add(new RunQueryResponse { ReadTime = Now() });
            }
            else throw new NotSupportedException(request.GetType().Name);
            return (results, gate);
        }
    }

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
    {
        async Task<TResponse> Respond() { await Task.Yield(); options.CancellationToken.ThrowIfCancellationRequested(); return (TResponse)Unary(request!); }
        return new(Respond(), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { });
    }
    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
    {
        var (responses, gate) = Stream(request!);
        return new(new Reader<TResponse>(responses, gate), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { });
    }
    public override TResponse BlockingUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) => (TResponse)Unary(request!);
    public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options) => throw new NotSupportedException();
    public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options) => throw new NotSupportedException();

    private sealed class Reader<T>(IReadOnlyList<object> values, Task gate) : IAsyncStreamReader<T>
    {
        private int _index = -1;
        public T Current => (T)values[_index];
        public async Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            await gate.WaitAsync(cancellationToken);
            await Task.Yield();
            return ++_index < values.Count;
        }
    }
}
