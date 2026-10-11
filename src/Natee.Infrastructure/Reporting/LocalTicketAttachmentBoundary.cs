using Natee.Core.Interfaces;

namespace Natee.Infrastructure.Reporting;

/// <summary>
/// Default local implementation of <see cref="ITicketIntegrationBoundary"/> for Version 1 (MVP).
/// Verifies that generated PDF and JSON report files exist on disk for manual attachment to an IT Support ticket.
/// Never uploads data automatically or connects to external ticketing servers in Version 1.
/// </summary>
public sealed class LocalTicketAttachmentBoundary : ITicketIntegrationBoundary
{
    public string ProviderName => "Local File Export (Manual Ticket Attachment)";
    public bool IsConfigured => true;

    public Task<(bool Supported, string Message)> PrepareTicketAttachmentBundleAsync(
        string pdfReportPath,
        string jsonReportPath,
        CancellationToken cancellationToken = default)
    {
        bool pdfExists = File.Exists(pdfReportPath);
        bool jsonExists = File.Exists(jsonReportPath);

        if (!pdfExists || !jsonExists)
        {
            return Task.FromResult((false, "One or more report files were not found on disk."));
        }

        return Task.FromResult((
            true,
            $"Reports saved locally and ready for manual attachment to your IT Support ticket:\n- PDF: {pdfReportPath}\n- JSON: {jsonReportPath}"));
    }
}
