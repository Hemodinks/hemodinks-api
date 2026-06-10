namespace HemodinksAPI.Api.Services;

public interface IEventReminderProcessor
{
    Task<int> ProcessDueRemindersAsync(CancellationToken cancellationToken);
}
