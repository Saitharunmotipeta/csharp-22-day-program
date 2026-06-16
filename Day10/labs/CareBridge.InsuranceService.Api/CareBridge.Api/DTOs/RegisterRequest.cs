using System.ComponentModel.DataAnnotations;

namespace CareBridge.Api.DTOs
{
    public class RegisterRequest
    {
        [Required]
        public string FullName { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        [MinLength(6, ErrorMessage = "Password must be at least 6 characters.")]
        public string Password { get; set; } = string.Empty;

        [Required]
        public string Role { get; set; } = string.Empty;

        public string Department { get; set; } = string.Empty;
    }
}
