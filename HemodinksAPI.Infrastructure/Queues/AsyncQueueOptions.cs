using HemodinksAPI.Application.Async;

namespace HemodinksAPI.Infrastructure.Queues;

public class AsyncQueueOptions
{
    public bool Enabled { get; set; }

    public string? ConnectionString { get; set; }

    public string PasswordResetEmailQueueName { get; set; } = AsyncQueueNames.PasswordResetEmails;

    public string FileExportQueueName { get; set; } = AsyncQueueNames.FileExportJobs;
}
