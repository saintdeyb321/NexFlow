using NexFlow.Application.Features.Automation.Dialogue.Contracts;

namespace NexFlow.Application.Features.Automation.Dialogue.Composition;

// Presentation of already verified data only: no lookups, transitions or consent.
public static class SecretaryPresentation
{
    private static string OperationName(OperationKind kind) => kind == OperationKind.Order ? "pedido" : "reserva";
    private static string Article(OperationKind kind) => kind == OperationKind.Order ? "el" : "la";
    private static string Saved(OperationKind kind, bool sentence = false)
        => $"{(sentence ? "Tu" : "tu")} {OperationName(kind)} sigue " + (kind == OperationKind.Order ? "guardado" : "guardada");
    public static string FocusTransition(OperationKind active, OperationKind? saved, bool resumed, bool canResume)
    {
        var text = resumed ? $"Retomamos tu {OperationName(active)}."
            : saved is { } other ? $"{Saved(other, sentence: true)}; continuamos con tu {OperationName(active)}."
            : $"Continuamos con tu {OperationName(active)}.";
        if (resumed && saved is { } paused) text += " " + Saved(paused, sentence: true) + ".";
        if (saved is { } available && canResume)
            text += $" Puedes pedir «Retomar {Article(available)} {OperationName(available)}» cuando quieras volver.";
        return text + " Este cambio no envía ni confirma operaciones.";
    }

    public static string FocusReminder(OperationKind active, OperationKind saved)
        => $"Tu {OperationName(active)} está en preparación y {Saved(saved)}. Puedes pedir «Retomar {Article(saved)} {OperationName(saved)}»; al volver comprobaremos de nuevo los datos.";

    public const string AskDate = "Busquemos un día que te acomode.\n\n¿Para qué fecha deseas reservar? Puedes indicar día y mes, por ejemplo «03/11» o «3 de noviembre», o decir «mañana por la tarde»; comprobaré los horarios de la sede.";
    public const string AskName = "Para completar tu reserva,\n\n¿cuál es tu nombre y apellido?";
    public const string ChooseService = "¿Cuál servicio te gustaría reservar? Puedes decir su nombre o una opción como «la primera».";
    public const string ConfirmReservation = "¿Confirmas la reserva o prefieres cambiar algún dato? Puedes responder «Sí», «No» o indicar el cambio.";

    public static string AskTime(bool period = false) => period
        ? "¿Qué hora te acomoda dentro de esa franja? Puedes decir, por ejemplo, «a las cuatro de la tarde»; comprobaré ese horario."
        : "¿Qué horario te acomoda para atenderte? Puedes decir «a las cuatro de la tarde» o indicar otra hora; comprobaré su disponibilidad.";

    public static string Locations(IEnumerable<string> names, string question)
        => "Podemos atenderte en estas sedes:\n\n" + string.Join("\n", names.Select(n => "• " + n))
            + "\n\n" + question + " Puedes indicar el nombre o una opción como «la primera».";

    public static string Services(IReadOnlyList<string> names, bool hasMore)
        => "Estos son los servicios que puedes reservar en esta sede:\n\n"
            + string.Join("\n", names.Select(n => "• " + n))
            + (hasMore ? "\n\nHay más opciones; puedes pedir «Ver más» o consultar el catálogo." : "")
            + "\n\n" + ChooseService;

    public const string ChooseOption = "¿Cuál prefieres? Puedes indicar el nombre o una opción como «la primera».";

    public static string StartOrder(string productName) => "Te ayudo a preparar una solicitud para que el negocio la revise. Elegimos productos y cantidades, revisamos el resumen y después te pediré confirmar el envío.\n\n"
        + $"¿Qué productos y cantidades deseas solicitar? Por ejemplo, «2 unidades de {productName}». También puedes consultar «productos».";

    public const string ReviewOrder = "¿Deseas agregar o cambiar algo, o revisar el envío? Puedes indicar un cambio o decir «Enviar la solicitud»; antes de registrarla te pediré confirmar el resumen.";

    public static string OrderRecovery(bool hasItems, bool confirming) => "No he cambiado tu solicitud ni la he enviado. "
        + (confirming ? "El resumen sigue pendiente de tu decisión.\n\nPuedes responder «Confirmar», indicar una corrección o pedir «Descartar»."
            : hasItems ? "Conservamos tus productos y cantidades.\n\nPuedes pedir «Ver pedido», indicar una cantidad nueva o decir «Enviar la solicitud» para revisar el resumen."
            : "El borrador sigue guardado.\n\nIndica qué producto y cuántas unidades deseas, o consulta «productos».")
        + " Si necesitas ayuda de una persona, escribe «asesor».";
}
