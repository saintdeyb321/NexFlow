namespace NexFlow.Application.DTOs.Conversations;

public record MessageDto(
    string Role, // "USER" o "ASSISTANT"
    string Content,
    DateTime Timestamp
);

public record ConversationContextDto(
    string CustomerIdentifier,
    List<MessageDto> History
);