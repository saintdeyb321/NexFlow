namespace NexFlow.Domain.Enums;

public enum ConversationMode
{
    Automatic = 1, // La IA responde
    Human = 2,     // El dueño tomó el control, la IA se calla
    Paused = 3     // Pausa temporal (ej. fuera de horario)
}