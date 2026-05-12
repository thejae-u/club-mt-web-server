namespace ClubMTServer.Models;

public class ManittoReportRequest
{
    public string Content { get; set; } = string.Empty;
    public string? ClientRequestId { get; set; }
}
