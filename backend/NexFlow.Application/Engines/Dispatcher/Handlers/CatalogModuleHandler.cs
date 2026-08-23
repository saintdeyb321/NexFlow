using NexFlow.Application.Abstractions;

namespace NexFlow.Application.Engines.Dispatcher.Handlers;

public class CatalogModuleHandler : IModuleHandler
{
    public string ModuleCode => "CATALOG";

    private readonly ICatalogRepository _catalogRepository;

    public CatalogModuleHandler(ICatalogRepository catalogRepository)
    {
        _catalogRepository = catalogRepository;
    }

    public string[] SupportedCapabilities => new[] { "READ" };

    public async Task<string> ExecuteCapabilityAsync(Guid workspaceId, CapabilityRequest request, CancellationToken cancellationToken)
    {
        if (request.CapabilityCode != "READ")
            return "SISTEMA: Capacidad no soportada por el módulo CATALOG.";

        var products = await _catalogRepository.GetProductsAsync(workspaceId, cancellationToken);
        var activeProducts = products.Where(p => p.IsActive).ToList();

        if (!activeProducts.Any())
            return "SISTEMA: Informa cortésmente que actualmente no hay productos disponibles en el catálogo.";

        var productsText = string.Join("\n", activeProducts.Select(p =>
        {
            var desc = !string.IsNullOrWhiteSpace(p.Description) ? $" - {p.Description}" : "";
            return $"- {p.Name}: {p.Currency} {p.Price}{desc}";
        }));

        return $@"SISTEMA: Utiliza la siguiente lista de productos y sus precios para responder la duda del cliente. NO ofrezcas productos que no estén en esta lista:
{productsText}";
    }
}