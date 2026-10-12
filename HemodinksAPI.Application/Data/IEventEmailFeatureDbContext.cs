using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Data;

public interface IEventEmailFeatureDbContext : IEventFeatureDbContext
{
    DbSet<EventEmailDelivery> EventEmailDeliveries { get; }
    DbSet<EquipeOperador> EquipeOperadores { get; }
    Task<int> EnqueueEventEmailsAsync(IReadOnlyList<EventEmailDelivery> deliveries, CancellationToken cancellationToken);
}


