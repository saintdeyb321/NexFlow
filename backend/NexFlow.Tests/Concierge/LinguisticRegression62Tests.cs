using Moq;
using NexFlow.Application.Features.Automation.Dialogue.Understanding;
using NexFlow.Domain.Entities.Catalog;
using NexFlow.Domain.Enums;
using NexFlow.Domain.ValueObjects;
using Xunit;

namespace NexFlow.Tests.Concierge;

// Development regressions, not a blind corpus or a provider-quality benchmark.
public sealed class LinguisticRegression62Tests
{
    private static AssistantConversationTests.Fixture Create()
    {
        var fixture = new AssistantConversationTests.Fixture(conciergeCatalog: true, intelligentCommerce: true) {
            Now = new(2026, 10, 20, 21, 0, 0, DateTimeKind.Utc)
        };
        fixture.UseCatalogKnowledge();
        return fixture;
    }

    [Theory]
    [InlineData("Quiero cinco panes integrales porfa.", 5, 0)]
    [InlineData("Dame tres panes integrales, por favor.", 3, 0)]
    [InlineData("Me das dos queques, gracias.", 0, 2)]
    [InlineData("Agrégame cuatro panes integrales, porfa.", 4, 0)]
    [InlineData("Quisiera dos queques si puedes.", 0, 2)]
    [InlineData("Dame dos panes integrales y tres queques, porfa.", 2, 3)]
    [InlineData("Me das un queque y cuatro panes integrales, gracias.", 4, 1)]
    public async Task Courtesy_outside_catalog_names_preserves_exact_quantities_locally(string text, int bread, int cake)
    {
        var f = Create(); await f.HandleTurn(text, "draft");
        var state = await f.Context();
        Assert.Equal(bread, state.OrderDraftItems.SingleOrDefault(i => i.ProductId == "product-a")?.Quantity ?? 0);
        Assert.Equal(cake, state.OrderDraftItems.SingleOrDefault(i => i.ProductId == "product-b")?.Quantity ?? 0);
        Assert.Equal(0, f.AiCalls); Assert.Empty(f.Orders); Assert.Empty(f.Bookings);
    }

    [Theory]
    [InlineData("Pan Porfa")]
    [InlineData("Pan Gracias")]
    [InlineData("Pan Por Favor")]
    [InlineData("Si Puedes")]
    [InlineData("Queque Tres Leches")]
    [InlineData("Menú")]
    [InlineData("Catálogo")]
    [InlineData("Pan y Gracias")]
    public async Task Literal_catalog_names_containing_courtesy_or_control_words_are_not_erased(string name)
    {
        var f = Create(); f.Products[0].Name = name;
        await f.HandleTurn($"Dame dos {name}, porfa", "draft");
        var item = Assert.Single((await f.Context()).OrderDraftItems);
        Assert.Equal("product-a", item.ProductId); Assert.Equal(2, item.Quantity);
        Assert.Equal(0, f.AiCalls); Assert.Empty(f.Orders);
    }

    [Fact]
    public async Task A_courtesy_in_a_longer_real_name_is_not_silently_mapped_to_a_shorter_product()
    {
        var f = Create(); f.Products[0].Name = "Pan"; f.Products[1].Name = "Pan Por Favor";
        await f.HandleTurn("Dame dos Pan Por Favor", "draft");
        Assert.Equal("product-b", Assert.Single((await f.Context()).OrderDraftItems).ProductId);
        Assert.Empty(f.Orders);
    }

    [Fact]
    public async Task Courtesy_in_an_authorized_alias_is_preserved()
    {
        var f = Create(); f.Products[0].Metadata["aliases"] = new[] { "Rollo Gracias" };
        await f.HandleTurn("Me das dos Rollos Gracias, por favor", "draft");
        var item = Assert.Single((await f.Context()).OrderDraftItems);
        Assert.Equal("product-a", item.ProductId); Assert.Equal(2, item.Quantity); Assert.Equal(0, f.AiCalls);
    }

    [Fact]
    public async Task A_verb_like_token_inside_a_catalog_name_is_not_a_quantity_prefix()
    {
        foreach (var (text, quantity) in new[] { ("Dame Das Tres, porfa", 1), ("Dame dos Das Tres, porfa", 2) })
        {
            var f = Create(); f.Products[0].Name = "Das Tres";
            await f.HandleTurn(text, "draft");
            Assert.Equal(quantity, Assert.Single((await f.Context()).OrderDraftItems).Quantity);
            Assert.Empty(f.Orders);
        }
    }

    [Theory]
    [InlineData("No quiero comprar dos panes integrales porfa")]
    [InlineData("¿Y si comprara dos panes integrales, por favor?")]
    [InlineData("¿Cuánto cuestan dos panes integrales porfa?")]
    [InlineData("Quiero pan porfa")]
    [InlineData("¿Me das el precio del pan integral, porfa?")]
    [InlineData("¿Me das información del queque, gracias?")]
    public async Task Negation_hypothesis_information_and_ambiguity_do_not_create_an_order(string text)
    {
        var f = Create(); await f.HandleTurn(text, "read");
        Assert.Empty(f.Orders); Assert.Empty(f.Bookings); Assert.Empty((await f.Context()).OrderDraftItems);
    }

    [Theory]
    [InlineData("Mándame el folleto de productos.", false)]
    [InlineData("¿Me compartes su catálogo?", true)]
    [InlineData("Quiero ver el catálogo completo.", true)]
    [InlineData("¿Tienes una lista de precios?", true)]
    [InlineData("¿Me puedes pasar el PDF?", true)]
    [InlineData("¿Qué productos tienen?", false)]
    public async Task Broad_document_requests_use_safe_factual_fallback_after_required_scope_choice(string text, bool requiresScope)
    {
        var f = Create(); await f.HandleTurn(text, "catalog");
        var responseSource = "catalog";
        if (requiresScope)
        {
            await ChooseProducts(f, responseSource, "catalog-choice"); responseSource = "catalog-choice";
        }
        Assert.Contains("Pan integral", f.Sent[responseSource].Content);
        Assert.Contains("2.50", f.Sent[responseSource].Content);
        Assert.Equal(0, f.AiCalls); Assert.Empty(f.Orders); Assert.Empty((await f.Context()).OrderDraftItems);
        Assert.DoesNotContain(f.Outbound.Invocations, i => i.Method.Name == "SendDocumentAsync");
        Assert.All(f.CatalogReads.Invocations, i => Assert.Equal("GetArtifactAsync", i.Method.Name));
    }

    [Theory]
    [InlineData("PRODUCT", MessageStatus.Sent)]
    [InlineData("PRODUCT", MessageStatus.UnknownDelivery)]
    [InlineData("SERVICE", MessageStatus.Sent)]
    [InlineData("SERVICE", MessageStatus.UnknownDelivery)]
    public async Task Current_pdf_uses_durable_outbound_and_uncertain_replays_do_not_resend(string scope, MessageStatus delivery)
    {
        var f = Create(); var artifact = CatalogArtifact.Initialize(f.Workspace, scope);
        artifact.CompleteUpload("https://example.test/current.pdf", "synthetic-hash");
        f.CatalogReads.Setup(c => c.GetArtifactAsync(f.Workspace, scope, It.IsAny<CancellationToken>())).ReturnsAsync(artifact);
        f.DeliveryStatus = delivery;
        var text = scope == "PRODUCT" ? "Mándame el folleto de productos" : "¿Me compartes el PDF de servicios?";
        await f.HandleTurn(text, "pdf"); await f.HandleTurn(text, "pdf");
        var call = Assert.Single(f.Outbound.Invocations, i => i.Method.Name == "SendDocumentAsync");
        Assert.Equal(f.Workspace, call.Arguments[0]); Assert.Equal(scope, call.Arguments[6]);
        Assert.Equal(delivery, f.Sent["pdf"].Status);
        Assert.DoesNotContain("enviado", f.Sent["pdf"].Content, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(f.Orders); Assert.Empty(f.Bookings); Assert.Equal(0, f.AiCalls);
    }

    [Fact]
    public async Task Generating_pdf_after_scope_choice_keeps_verified_text_without_sending_a_previous_file()
    {
        var f = Create(); var artifact = CatalogArtifact.Initialize(f.Workspace, "PRODUCT");
        artifact.CompleteUpload("https://example.test/previous.pdf", "hash");
        artifact.MarkAsGenerating("new-source", "new-generation", ArtifactDesign.Create("MODERN", "OCEAN", "BALANCED"), f.Now);
        f.CatalogReads.Setup(c => c.GetArtifactAsync(f.Workspace, "PRODUCT", It.IsAny<CancellationToken>())).ReturnsAsync(artifact);
        await f.HandleTurn("¿Me puedes pasar el PDF?", "pdf");
        await ChooseProducts(f, "pdf", "pdf-choice");
        Assert.Contains("Pan integral", f.Sent["pdf-choice"].Content);
        Assert.DoesNotContain(f.Outbound.Invocations, i => i.Method.Name == "SendDocumentAsync");
        Assert.All(f.CatalogReads.Invocations, i => Assert.Equal("GetArtifactAsync", i.Method.Name));
    }

    // Versioned product-policy contract: an unscoped request must be clarified before lookup.
    // The pre-change tests and their failing TRX are retained in the closure evidence.
    private static async Task ChooseProducts(AssistantConversationTests.Fixture f, string question, string source)
    {
        Assert.Contains("productos", f.Sent[question].Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("servicios", f.Sent[question].Content, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new[] { "ProductQuery", "ServiceQuery" }, (await f.Context()).RecoveryOptions.Select(o => o.Key));
        Assert.Empty(f.CatalogReads.Invocations);
        Assert.DoesNotContain(f.Outbound.Invocations, i => i.Method.Name == "SendDocumentAsync");
        Assert.Empty(f.Orders); Assert.Empty(f.Bookings);
        await f.HandleTurn("Productos", source);
    }

    [Theory]
    [InlineData("Muéstrame qué opciones tengo.")]
    [InlineData("¿Qué puedo hacer por aquí?")]
    [InlineData("¿Qué cosas me puedes ayudar a gestionar?")]
    [InlineData("Quiero ver el menú.")]
    [InlineData("¿Me muestras las opciones disponibles?")]
    public async Task Optional_menu_requests_use_only_current_licensed_options_without_starting_operations(string text)
    {
        var f = Create(); f.Modules.IntersectWith(["CATALOG", "ORDERS"]);
        await f.HandleTurn(text, "menu");
        Assert.Equal(new[] { "ProductQuery", "Order", "Support" }, (await f.Context()).RecoveryOptions.Select(o => o.Key));
        await f.HandleTurn("la primera", "select");
        Assert.Contains("Pan integral", f.Sent["select"].Content);
        Assert.Equal(0, f.AiCalls); Assert.Empty(f.Orders); Assert.Empty(f.Bookings);
        Assert.Null((await f.Context()).CurrentGoal);
    }

    [Fact]
    public async Task Specific_service_options_are_not_a_global_menu_and_preserve_booking_data()
    {
        var f = Create(); await f.HandleTurn("Quiero reservar limpieza dental en Concepción pasado mañana", "booking");
        var before = AssistantConversationTests.Fixture.BusinessDraft(await f.Context());
        await f.HandleTurn("¿Qué opciones de limpieza tienen?", "specific");
        Assert.Equal(before, AssistantConversationTests.Fixture.BusinessDraft(await f.Context()));
        Assert.DoesNotContain("Consultar productos", f.Sent["specific"].Content);
        Assert.DoesNotContain((await f.Context()).RecoveryOptions, o => o.Kind == "Action");
        Assert.Empty(f.Orders); Assert.Empty(f.Bookings);
    }

    [Fact]
    public async Task Contextual_menu_revalidation_and_empty_license_preserve_human_assistance()
    {
        var f = Create(); await f.HandleTurn("¿Me muestras las opciones disponibles?", "menu");
        var index = (await f.Context()).RecoveryOptions.FindIndex(o => o.Key == "Order") + 1;
        Assert.True(index > 0); f.Modules.Remove("ORDERS");
        await f.HandleTurn(index.ToString(), "revoked"); Assert.Empty(f.Orders);
        Assert.Contains("ya no está disponible", f.Sent["revoked"].Content);
        f.Modules.Clear(); await f.HandleTurn("Quiero ver el menú", "empty");
        Assert.Equal("Support", Assert.Single((await f.Context()).RecoveryOptions).Key);
        await f.HandleTurn("1", "human"); Assert.Equal(1, f.Handoffs);
    }

    [Fact]
    public async Task Courtesy_drafts_require_explicit_confirmation_and_replays_are_idempotent()
    {
        var f = Create(); await f.HandleTurn("Me das dos queques, gracias", "draft");
        await f.HandleTurn("Me das dos queques, gracias", "draft");
        Assert.Equal(2, Assert.Single((await f.Context()).OrderDraftItems).Quantity); Assert.Empty(f.Orders);
        await f.HandleTurn("finalizar", "consent-review");
        await f.HandleTurn("Confirmar", "commit"); await f.HandleTurn("Confirmar", "commit");
        Assert.Single(f.Orders); Assert.Single(f.OrderWrites.Invocations, i => i.Method.Name == "CreateOrderAsync");
    }

    [Fact]
    public async Task Reservation_weekday_uses_authorized_future_policy_and_later_corrections_do_not_commit()
    {
        var f = Create(); var date = new BusinessDateResolver().Resolve("el jueves", new DateTimeOffset(f.Now).ToOffset(TimeSpan.FromHours(-5)));
        Assert.Equal(NexFlow.Application.Features.Automation.Dialogue.Contracts.ResolutionStatus.Ambiguous, date.Status);
        Assert.Equal(new[] { "2026-10-22", "2026-10-29" }, date.Candidates.Select(c => c.Value.ToString("yyyy-MM-dd")));
        await f.HandleTurn("Quisiera limpieza dental en San Jerónimo el jueves por la tarde", "ambiguous");
        Assert.Equal("2026-10-22", (await f.Context()).TargetDate); Assert.Empty(f.Bookings);
        await f.HandleTurn("el 22 de octubre", "date"); Assert.Equal("2026-10-22", (await f.Context()).TargetDate);
        await f.HandleTurn("mejor el 24 de octubre", "change"); Assert.Equal("2026-10-24", (await f.Context()).TargetDate); Assert.Empty(f.Bookings);
    }

    [Fact]
    public async Task Same_name_in_two_workspaces_uses_each_catalog_and_rechecks_revoked_order_license()
    {
        var a = Create(); var b = Create(); b.Products[0].PriceMinorUnits = 999;
        foreach (var f in new[] { a, b }) {
            await f.HandleTurn("Quiero dos panes integrales porfa", "draft");
            Assert.Equal("product-a", Assert.Single((await f.Context()).OrderDraftItems).ProductId);
            Assert.Contains((2 * f.Products[0].PriceMinorUnits / 100m).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), f.Sent["draft"].Content);
            f.Modules.Remove("ORDERS"); await f.HandleTurn("enviar pedido", "denied"); Assert.Empty(f.Orders);
            Assert.All(f.OfferingReads.Invocations, i => Assert.Equal(f.Workspace, i.Arguments[0]));
        }
    }

    [Fact]
    public async Task Equal_catalog_names_remain_ambiguous_after_courtesy_removal()
    {
        var f = Create(); f.Products[0].Name = f.Products[1].Name = "Pan Porfa";
        await f.HandleTurn("Dame dos Pan Porfa, por favor", "ambiguous");
        Assert.Empty((await f.Context()).OrderDraftItems); Assert.Empty(f.Orders);
        Assert.Contains("varias opciones", f.Sent["ambiguous"].Content); Assert.Equal(0, f.AiCalls);
    }

    [Fact]
    public async Task Polite_price_request_without_question_marks_preserves_an_existing_order()
    {
        var f = Create(); await f.HandleTurn("Quiero dos queques", "draft");
        var before = AssistantConversationTests.Fixture.BusinessDraft(await f.Context());
        await f.HandleTurn("Me das el precio del pan integral, gracias", "price");
        Assert.Equal(before, AssistantConversationTests.Fixture.BusinessDraft(await f.Context()));
        Assert.Contains("2.50", f.Sent["price"].Content); Assert.Empty(f.Orders); Assert.Equal(0, f.AiCalls);
    }

    [Fact]
    public async Task Stale_offering_reference_never_returns_deactivated_product_facts()
    {
        var f = Create(); await f.HandleTurn("¿Cuánto cuesta el pan integral?", "price");
        Assert.Contains("2.50", f.Sent["price"].Content); f.Products[0].IsActive = false;
        await f.HandleTurn("¿Cuánto cuesta ese producto?", "stale");
        Assert.DoesNotContain("2.50", f.Sent["stale"].Content); Assert.Empty(f.Orders);
        Assert.Empty((await f.Context()).OrderDraftItems);
    }

    [Fact]
    public async Task A_specific_offering_in_a_document_request_stays_specific()
    {
        var f = Create(); await f.HandleTurn("Mándame el folleto del pan integral", "specific");
        Assert.Contains("Pan integral", f.Sent["specific"].Content);
        Assert.DoesNotContain("Queque", f.Sent["specific"].Content);
        Assert.Empty(f.CatalogReads.Invocations);
        Assert.DoesNotContain(f.Outbound.Invocations, i => i.Method.Name == "SendDocumentAsync");
    }

    [Fact]
    public async Task Revocation_during_catalog_lookup_prevents_restricted_media_and_text()
    {
        var f = Create(); var artifact = CatalogArtifact.Initialize(f.Workspace, "PRODUCT");
        artifact.CompleteUpload("https://example.test/current.pdf", "hash");
        f.CatalogReads.Setup(c => c.GetArtifactAsync(f.Workspace, "PRODUCT", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => { f.Modules.Remove("CATALOG"); return artifact; });
        await f.HandleTurn("Mándame el folleto de productos", "revoked");
        Assert.DoesNotContain("Pan integral", f.Sent["revoked"].Content);
        Assert.DoesNotContain(f.Outbound.Invocations, i => i.Method.Name == "SendDocumentAsync");
        Assert.Empty(f.Orders); Assert.Empty(f.Bookings);
    }
}
