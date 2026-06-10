namespace HemodinksAPI.Application.Services;

public interface IEventReminderProcessor
{
    Task<int> ProcessDueRemindersAsync(CancellationToken cancellationToken);
}
