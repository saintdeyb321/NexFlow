using Grpc.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Engines.AI;
using NexFlow.Application.Features.AI.Interpretation;
using NexFlow.Application.Features.AI.Router;
using NexFlow.Application.Features.Business;
using NexFlow.Domain.Enums;
using NexFlow.Infrastructure.Persistence.Firestore;
using NexFlow.Tests.Fakes;
using Xunit;

namespace NexFlow.Tests;

public sealed class Rc3Sprint1ProfileTests
{
    [Theory]
    [InlineData(StatusCode.Unavailable, AiFailureKind.Unavailable)]
    [InlineData(StatusCode.ResourceExhausted, AiFailureKind.Unavailable)]
    [InlineData(StatusCode.DeadlineExceeded, AiFailureKind.Timeout)]
    public async Task Transient_profile_read_is_controlled_without_ai_and_recovers_on_the_next_inbound(StatusCode code, AiFailureKind failure)
    {
        var db = new InMemoryFirestore(); var workspace = Guid.NewGuid();
        var profiles = new FirestoreBusinessProfileRepository(db.Db);
        await profiles.SaveProfileAsync(workspace, new("Real business", "", "", "", "", "America/Lima"), default);
        db.ReadFailure = new RpcException(new Status(code, "private provider detail"));
        var router = new Mock<IAiRouter>(MockBehavior.Strict);
        var clock = Mock.Of<IClock>(c => c.UtcNow == new DateTime(2026, 10, 19, 4, 0, 0, DateTimeKind.Utc));
        var interpreter = new AiInterpreter(router.Object, clock, profiles, NullLogger<AiInterpreter>.Instance);
        var result = await interpreter.InterpretAsync(workspace, "una pregunta", "ORDER", "COLLECT_DATE", new() { "ORDERS" }, default);
        Assert.Equal(ConversationIntent.ProviderUnavailable, result.Intent); Assert.Equal(failure, result.Failure);
        router.VerifyNoOtherCalls();
        db.ReadFailure = null;
        router.Setup(r => r.ExecuteTaskAsync(AiTaskType.IntentExtraction,
            It.Is<string>(p => p.Contains("2026-10-18") && p.Contains("23:00")), "una pregunta", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"Intent\":\"General\"}");
        Assert.Equal(ConversationIntent.General, (await interpreter.InterpretAsync(workspace, "una pregunta", "ORDER", "COLLECT_DATE", new() { "ORDERS" }, default)).Intent);
        router.VerifyAll();
    }

    [Theory]
    [InlineData(StatusCode.PermissionDenied)] [InlineData(StatusCode.Unauthenticated)]
    [InlineData(StatusCode.InvalidArgument)] [InlineData(StatusCode.FailedPrecondition)] [InlineData(StatusCode.Aborted)]
    public async Task Critical_profile_errors_propagate_instead_of_becoming_an_ai_outage(StatusCode code)
    {
        var error = new RpcException(new Status(code, "private detail"));
        var db = new InMemoryFirestore { ReadFailure = error }; var router = new Mock<IAiRouter>(MockBehavior.Strict);
        var interpreter = new AiInterpreter(router.Object, Mock.Of<IClock>(), new FirestoreBusinessProfileRepository(db.Db), NullLogger<AiInterpreter>.Instance);
        Assert.Same(error, await Assert.ThrowsAsync<RpcException>(() => interpreter.InterpretAsync(Guid.NewGuid(), "pregunta", "ORDER", null, new(), default)));
        router.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Normalized_profile_failure_preserves_the_existing_http_503_contract(bool timedOut)
    {
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext(); context.Response.Body = new MemoryStream();
        var middleware = new NexFlow.API.Middleware.GlobalExceptionMiddleware(_ => throw new BusinessProfileUnavailableException(timedOut, new IOException("private connection detail")),
            NullLogger<NexFlow.API.Middleware.GlobalExceptionMiddleware>.Instance);
        await middleware.InvokeAsync(context);
        Assert.Equal(503, context.Response.StatusCode); context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.Contains("Dependency.Unavailable", body); Assert.DoesNotContain("private", body);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Caller_cancellation_propagates_but_a_dependency_timeout_is_controlled(bool cancelCaller)
    {
        using var stop = new CancellationTokenSource(); if (cancelCaller) stop.Cancel();
        var profiles = new Mock<IBusinessProfileRepository>(MockBehavior.Strict);
        profiles.Setup(p => p.GetProfileAsync(It.IsAny<Guid>(), stop.Token)).ThrowsAsync(new OperationCanceledException(stop.Token));
        var router = new Mock<IAiRouter>(MockBehavior.Strict);
        var interpreter = new AiInterpreter(router.Object, Mock.Of<IClock>(), profiles.Object, NullLogger<AiInterpreter>.Instance);
        var call = interpreter.InterpretAsync(Guid.NewGuid(), "pregunta", "ORDER", null, new(), stop.Token);
        if (cancelCaller) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
        else { var result = await call; Assert.Equal(ConversationIntent.ProviderUnavailable, result.Intent); Assert.Equal(AiFailureKind.Timeout, result.Failure); }
        router.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Invalid_configured_zone_is_not_silently_replaced_with_an_invented_business_time()
    {
        var profiles = new Mock<IBusinessProfileRepository>();
        profiles.Setup(p => p.GetProfileAsync(It.IsAny<Guid>(), default)).ReturnsAsync(new BusinessProfileDto("Business", "", "", "", "", "Invalid/ConfiguredZone"));
        var router = new Mock<IAiRouter>(MockBehavior.Strict);
        var interpreter = new AiInterpreter(router.Object, Mock.Of<IClock>(), profiles.Object, NullLogger<AiInterpreter>.Instance);
        await Assert.ThrowsAsync<TimeZoneNotFoundException>(() => interpreter.InterpretAsync(Guid.NewGuid(), "pregunta", "", null, new(), default));
        router.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Profile_outage_result_preserves_the_real_draft_and_licensed_options()
    {
        var f = new AssistantConversationTests.Fixture(); await f.SeedDraft("ORDER"); f.Modules.Remove("RESERVATIONS"); f.Modules.Remove("SERVICES");
        var before = AssistantConversationTests.Fixture.BusinessDraft(await f.Context());
        var db = new InMemoryFirestore { ReadFailure = new RpcException(new Status(StatusCode.Unavailable, "outage")) };
        var interpreter = new AiInterpreter(Mock.Of<IAiRouter>(MockBehavior.Strict), Mock.Of<IClock>(), new FirestoreBusinessProfileRepository(db.Db), NullLogger<AiInterpreter>.Instance);
        f.Interpretations.Enqueue(await interpreter.InterpretAsync(f.Workspace, "pregunta", "ORDER", "COLLECT_DATE", f.Modules, default));
        await f.Turn("una consulta ambigua", "profile-outage");
        Assert.Equal(before, AssistantConversationTests.Fixture.BusinessDraft(await f.Context()));
        Assert.Single(f.Sent); Assert.Empty(f.Bookings); Assert.Empty(f.Orders);
        Assert.DoesNotContain((await f.Context()).RecoveryOptions, o => o.Key == "Reservation");
    }
}
