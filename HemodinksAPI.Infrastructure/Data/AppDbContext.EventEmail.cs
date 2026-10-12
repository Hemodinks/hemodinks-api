using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Infrastructure.Data;

public partial class AppDbContext
{
    public DbSet<EventEmailDelivery> EventEmailDeliveries { get; set; } = null!;

    // Unique database indexes arbitrate concurrent requests across API replicas.
    public async Task<int> EnqueueEventEmailsAsync(IReadOnlyList<EventEmailDelivery> deliveries, CancellationToken ct)
    {
        var remaining = deliveries.ToList();
        for (var attempt = 0; attempt < 3 && remaining.Count > 0; attempt++)
        {
            EventEmailDeliveries.AddRange(remaining);
            try
            {
                await SaveChangesAsync(ct);
                return remaining.Count;
            }
            catch (DbUpdateException exception) when (exception.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 })
            {
                foreach (var delivery in remaining) Entry(delivery).State = EntityState.Detached;
                var first = remaining[0];
                var existing = await EventEmailDeliveries.AsNoTracking()
                    .Where(item => item.ClinicaId == first.ClinicaId && item.EventId == first.EventId)
                    .Select(item => new { item.RecipientUserId, item.RecipientEmail }).ToListAsync(ct);
                var filtered = remaining.Where(item => !existing.Any(saved =>
                    saved.RecipientUserId == item.RecipientUserId || saved.RecipientEmail == item.RecipientEmail)).ToList();
                // Never suppress unrelated database errors.
                if (filtered.Count == remaining.Count || (attempt == 2 && filtered.Count > 0)) throw;
                remaining = filtered;
            }
        }
        return 0;
    }
}


