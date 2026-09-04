using Bella.Wms.Integration.Partners.Contracts;
using Bella.Wms.Platform.Abstractions;
using Microsoft.Extensions.Logging;

namespace Bella.Wms.Integration.Partners.Infrastructure;

/// <summary>
/// Placeholder <see cref="INotificationService"/> — no mail transport is wired up yet.
/// </summary>
/// <remarks>
/// Same shape as <see cref="NotImplementedLocusClient"/>: it fails loudly (an error log
/// line and a failed <see cref="OperationResult"/>) rather than silently, so the
/// reject-family handlers are constructible and testable before a real implementation
/// exists. Swap the registration in
/// <see cref="PartnersInfrastructureExtensions.AddPartnersInfrastructure"/> when one
/// lands; nothing else changes.
/// </remarks>
public sealed class NotImplementedNotificationService : INotificationService
{
    private readonly ILogger<NotImplementedNotificationService> _logger;

    public NotImplementedNotificationService(ILogger<NotImplementedNotificationService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<OperationResult> SendAlertAsync(
        string subject, string body, CancellationToken cancellationToken = default)
    {
        _logger.LogError(
            "INotificationService.SendAlertAsync is not built yet. Subject: {Subject}",
            subject);

        return Task.FromResult(OperationResult.Failure(
            "Outbound alert email is not implemented.",
            "NOTIFICATION_SERVICE_NOT_IMPLEMENTED"));
    }
}
