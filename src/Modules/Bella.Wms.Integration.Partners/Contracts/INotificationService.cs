using Bella.Wms.Platform.Abstractions;

namespace Bella.Wms.Integration.Partners.Contracts;

/// <summary>
/// Outbound alert email. Replaces the ABL <c>sendMail</c> private method on
/// <c>locusAPI.cls</c> and the <c>rf/send_mail2.p</c> call <c>putawayreject</c> makes.
/// </summary>
/// <remarks>
/// <c>docs/HANDLER_SPECS.md</c> Group 2 is explicit that this is a platform concern, not
/// something to convert inline into each reject-family handler: "introduce an
/// <c>INotificationService</c> in Contracts with a stub implementation, and register it
/// the way <c>NotImplementedLocusClient</c> is registered." This is that interface.
/// </remarks>
public interface INotificationService
{
    /// <summary>Sends an alert email. Converts the ABL <c>sendMail(subject, body)</c> call.</summary>
    Task<OperationResult> SendAlertAsync(
        string subject,
        string body,
        CancellationToken cancellationToken = default);
}
