using System;
using System.ComponentModel.DataAnnotations;

public class AppLog
{
    [Key]
    public int LogId { get; set; }
    [Required]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    [Required]
    [StringLength(100)]
    public string? Level { get; set; }
    [Required]
    public string? Message { get; set; }
    public string? Exception { get; set; }
    public string? Logger { get; set; }
    public int? UserId { get; set; }
}