using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Features.Automation.ProcessMessage;
using NexFlow.Application.Features.Automation.ProcessMessage.Services;
using System.Timers;

namespace NexFlow.Tests.Features;

public class IncomingMessageGuardTests
{
    private readonly Mock<IInstanceResolver> _instanceResolverMock;
    private readonly Mock<IProcessedMessageRepository> _processedMessageRepoMock;
    private readonly Mock<IEntitlementService> _entitlementServiceMock;
    private readonly Mock<ILogger<IncomingMessageGuard>> _loggerMock;
    private readonly IncomingMessageGuard _guard;

    public IncomingMessageGuardTests()
    {
        // Configuramos los Mocks (Simuladores de dependencias)
        _instanceResolverMock = new Mock<IInstanceResolver>();
        _processedMessageRepoMock = new Mock<IProcessedMessageRepository>();
        _entitlementServiceMock = new Mock<IEntitlementService>();
        _loggerMock = new Mock<ILogger<IncomingMessageGuard>>();

        // Inyectamos los simuladores en nuestro Guard
        _guard = new IncomingMessageGuard(
            _instanceResolverMock.Object,
            _processedMessageRepoMock.Object,
            _entitlementServiceMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task CheckMessageAsync_DebeRechazar_SiLaLicenciaDelWorkspaceEsInvalida()
    {
        // Arrange (Preparar el escenario)
        var workspaceId = Guid.NewGuid();
        var command = new ProcessIncomingMessageCommand
        {
            InstanceName = "mi-instancia",
            CustomerPhone = "51999888777",
            MessageId = "msg_123"
        };

        _instanceResolverMock
            .Setup(x => x.ResolveInstanceAsync(command.InstanceName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workspaceId); // Simulamos que la instancia pertenece a un Workspace válido

        _entitlementServiceMock
            .Setup(x => x.IsLicenseValidAsync(workspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false); // 🔥 SIMULAMOS QUE LA LICENCIA ESTÁ VENCIDA O ES INVÁLIDA

        // Act (Ejecutar la acción)
        var result = await _guard.CheckMessageAsync(command, CancellationToken.None);

        // Assert (Verificar el resultado con FluentAssertions)
        result.IsValid.Should().BeFalse("porque la licencia del workspace no es válida");
        result.WorkspaceId.Should().Be(Guid.Empty, "porque por seguridad no debemos procesar datos de un tenant inactivo");

        // Verificamos que NUNCA se intentó bloquear el mensaje en BD (ahorrando recursos)
        _processedMessageRepoMock.Verify(x => x.BeginProcessingAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CheckMessageAsync_DebeAceptar_SiTodoEsValido()
    {
        // Arrange
        var workspaceId = Guid.NewGuid();
        var command = new ProcessIncomingMessageCommand { InstanceName = "mi-instancia", CustomerPhone = "51999888777", MessageId = "msg_123" };

        _instanceResolverMock.Setup(x => x.ResolveInstanceAsync(command.InstanceName, It.IsAny<CancellationToken>())).ReturnsAsync(workspaceId);
        _entitlementServiceMock.Setup(x => x.IsLicenseValidAsync(workspaceId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _processedMessageRepoMock.Setup(x => x.BeginProcessingAsync(workspaceId, command.MessageId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        // Act
        var result = await _guard.CheckMessageAsync(command, CancellationToken.None);

        // Assert
        result.IsValid.Should().BeTrue();
        result.WorkspaceId.Should().Be(workspaceId);
        result.NormalizedPhone.Should().Be("+51999888777");
    }
}