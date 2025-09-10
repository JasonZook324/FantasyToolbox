using System;
using System.ComponentModel.DataAnnotations;

namespace FantasyToolbox.Models
{
    public class User
    {
        [Key]
        public int UserId { get; set; }
        [Required, StringLength(50)]
        public string FirstName { get; set; }
        [Required, StringLength(50)]
        public string LastName { get; set; }
        [Required, EmailAddress]
        public string Email { get; set; }
        [Required]
        public string PasswordHash { get; set; }
        public bool IsActive { get; set; }
        public DateTime? LastLogin { get; set; }
        public int? SelectedTeamId { get; set; }
    }
}