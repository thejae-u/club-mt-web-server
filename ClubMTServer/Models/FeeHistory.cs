using System.ComponentModel.DataAnnotations;

namespace ClubMTServer.Models;

public class FeeHistory
{
    public int Id { get; set; }

    [Required]
    public DateTime Date { get; set; } = DateTime.UtcNow;

    [Required]
    [MaxLength(200)]
    public string Description { get; set; } = string.Empty;

    [Required]
    public int Amount { get; set; } // Positive for income, negative for expense

    [Required]
    [MaxLength(50)]
    public string Category { get; set; } = "General"; // Food, Rent, Income, etc.

    public string? Remarks { get; set; }

    public int BalanceAfter { get; set; } // Running balance after this transaction
}
