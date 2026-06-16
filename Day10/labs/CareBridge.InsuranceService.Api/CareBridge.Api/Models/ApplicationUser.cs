using Microsoft.AspNetCore.Identity;

namespace CareBridge.Api.Models
{
    // Extends IdentityUser so AspNetUsers stores hospital-specific columns
    // alongside the standard Identity columns (Id, Email, PasswordHash, etc.).
    public class ApplicationUser : IdentityUser
    {
        public string FullName { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
    }
}

