using System;
using System.ComponentModel.DataAnnotations;

public class User
{
    [Key]
    public int UserId { get; set; }

    [Required]
    [StringLength(50)]
    public string FirstName { get; set; }

    [Required]
    [StringLength(50)]
    public string LastName { get; set; }

    [Required]
    [EmailAddress]
    public string Email { get; set; }

    [Required]
    public string PasswordHash { get; set; }

    public bool IsActive { get; set; } = true;
    public bool EmailVerified { get; set; } = false;
    public string? VerificationCode { get; set; }
    public DateTime? VerificationCodeExpires { get; set; }
    public int VerificationAttempts { get; set; } = 0;
    public DateTime? LastVerificationAttempt { get; set; }
    public DateTime? LastResendTime { get; set; }

    public DateTime? LastLogin { get; set; }

    public int? SelectedTeamId { get; set; }
}