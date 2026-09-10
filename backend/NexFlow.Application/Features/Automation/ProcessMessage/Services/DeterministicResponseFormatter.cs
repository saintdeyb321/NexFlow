using System.Text.Json;
using System.Text.RegularExpressions;
using System.Linq;
using System.Collections.Generic;

namespace NexFlow.Application.Features.Automation.ProcessMessage.Services;

public static class DeterministicResponseFormatter
{
    public static string? TryFormatResponse(string moduleCode, string? jsonData, out string? documentUrl)
    {
        documentUrl = null;
        if (string.IsNullOrWhiteSpace(jsonData) || jsonData.Trim() == "{}") return null;

        try
        {
            using var doc = JsonDocument.Parse(jsonData);
            var root = doc.RootElement;

            // Extraemos el PDF si existe
            if (root.TryGetProperty("pdfUrl", out var pdfUrlProp) && pdfUrlProp.ValueKind == JsonValueKind.String)
            {
                documentUrl = pdfUrlProp.GetString();
            }

            // Ahorro de Tokens: Catálogo muy grande o vacío
            if ((moduleCode == "CATALOG" || moduleCode == "SERVICES") && root.TryGetProperty("status", out var catStatus))
            {
                if (catStatus.GetString() == "too_many_results")
                    return "¡Tenemos una gran variedad de opciones! 📄 Te adjunto nuestro catálogo completo en PDF para que lo revises con mayor comodidad. Si buscas algo en específico, dime qué necesitas.";

                if (catStatus.GetString() == "empty")
                    return "Lo siento, actualmente no tenemos ítems disponibles en nuestro catálogo.";
            }

            // Respuestas rápidas pre-programadas
            if (root.TryGetProperty("status", out var statusProp) && statusProp.GetString() == "missing_parameter")
            {
                var paramName = root.TryGetProperty("parameter", out var pProp) ? pProp.GetString() : "";
                if (paramName == "location") return "¡Claro! Para continuar, ¿me podrías indicar en cuál de nuestras sedes deseas hacer la reserva?";
                if (paramName == "service") return "¿Qué servicio específico te gustaría reservar?";
                if (paramName == "date") return "¿Para qué fecha deseas agendar tu turno? (Ej. Mañana, el viernes, 15 de octubre)";
                if (paramName == "time") return "¿A qué hora te gustaría asistir?";
                if (paramName == "name") return "Para dejar la reserva a tu nombre, ¿me podrías indicar tu nombre completo?";
            }

            return null; // Si no hay regla predeterminada, que Gemini se encargue.
        }
        catch
        {
            return null;
        }
    }
}