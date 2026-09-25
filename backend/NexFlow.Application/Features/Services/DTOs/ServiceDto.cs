using NexFlow.Application.Features.Shared.DTOs;

namespace NexFlow.Application.Features.Services.DTOs;

public class ServiceDto : BusinessOfferingDto
{
    public int? DurationInMinutes { get; set; }
    public bool RequiresReservation { get; set; }

    public ServiceDto()
    {
        Type = "SERVICE";
    }
}