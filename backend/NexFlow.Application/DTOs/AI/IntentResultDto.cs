namespace NexFlow.Application.DTOs.AI;

public record IntentResultDto(
    string Intent, // Ej: "CREATE_RESERVATION", "FAQ", "CANCEL_RESERVATION"
    double Confidence, // Ej: 0.95
    Dictionary<string, string> Parameters // Ej: { "service": "Corte", "date": "2026-08-19" }
)
{
    // Método de utilidad para saber si la IA está segura de lo que entendió
    public bool IsConfident(double threshold = 0.80) => Confidence >= threshold;
}