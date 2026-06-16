using System.IdentityModel.Tokens.Jwt;   // [AUTH] Creates JWT tokens.
using System.Security.Claims;            // [AUTH] Stores user identity information inside tokens.
using System.Text;
using CareBridge.Api.DTOs;
using CareBridge.Api.Models;
using Microsoft.AspNetCore.Authorization; // [AUTH] Enables [Authorize] and [AllowAnonymous].
using Microsoft.AspNetCore.Identity;      // [AUTH] User and role management.
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;     // [AUTH] Token signing and security.

namespace CareBridge.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        // [AUTH] Used to create users, find users, validate passwords,
        // and manage user accounts.
        private readonly UserManager<ApplicationUser> _userManager;

        // [AUTH] Used to create and manage roles such as
        // Nurse, Receptionist, and Administrator.
        private readonly RoleManager<IdentityRole> _roleManager;

        // [AUTH] Reads JWT settings from appsettings.json.
        private readonly IConfiguration _config;

        public AuthController(
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager,
            IConfiguration config)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _config = config;
        }

        // POST /api/auth/register

        // [AUTH]
        // Anyone can register.
        // No login token is required.
        [HttpPost("register")]
        [AllowAnonymous]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            // [AUTH] Verify the requested role exists.
            if (!await _roleManager.RoleExistsAsync(request.Role))
            {
                return BadRequest(new
                {
                    message = $"Role '{request.Role}' does not exist."
                });
            }

            // [AUTH] Create a new user account.
            var user = new ApplicationUser
            {
                UserName = request.Email,
                Email = request.Email,
                FullName = request.FullName,
                Department = request.Department
            };

            // [AUTH] Securely hashes the password and saves the user.
            var result =
                await _userManager.CreateAsync(
                    user,
                    request.Password);

            if (!result.Succeeded)
            {
                return BadRequest(new
                {
                    errors = result.Errors
                                   .Select(e => e.Description)
                });
            }

            // [AUTH] Assign the selected role to the user.
            await _userManager.AddToRoleAsync(
                user,
                request.Role);

            // [AUTH] Generate and return a JWT token.
            return Ok(await BuildToken(user));
        }

        // POST /api/auth/login

        // [AUTH]
        // Anyone can attempt login.
        // No token is required because the user is not yet authenticated.
        [HttpPost("login")]
        [AllowAnonymous]
        public async Task<IActionResult> Login(
            [FromBody] LoginRequest request)
        {
            // [AUTH] Find user by email.
            var user =
                await _userManager.FindByEmailAsync(request.Email);

            if (user is null)
            {
                return Unauthorized(new
                {
                    message = "Invalid email or password."
                });
            }

            var signInManager =
                HttpContext.RequestServices
                    .GetRequiredService<SignInManager<ApplicationUser>>();

            // [AUTH]
            // Verifies password and applies lockout rules.
            var result =
                await signInManager.CheckPasswordSignInAsync(
                    user,
                    request.Password,
                    lockoutOnFailure: true);

            if (!result.Succeeded)
            {
                // [AUTH]
                // Protects against repeated password guessing attempts.
                if (result.IsLockedOut)
                {
                    return Unauthorized(new
                    {
                        message = "Account locked. Try again in 5 minutes."
                    });
                }

                return Unauthorized(new
                {
                    message = "Invalid email or password."
                });
            }

            // [AUTH]
            // Successful login → issue JWT token.
            return Ok(await BuildToken(user));
        }

        // GET /api/auth/me

        // [AUTH]
        // Requires a valid JWT token.
        // Returns information about the currently logged-in user.
        [HttpGet("me")]
        [Authorize]
        public async Task<IActionResult> Me()
        {
            // [AUTH] Read email from token claims.
            var email =
                User.FindFirstValue(ClaimTypes.Email);

            var user =
                await _userManager.FindByEmailAsync(email!);

            if (user is null)
                return NotFound();

            var roles =
                await _userManager.GetRolesAsync(user);

            return Ok(new
            {
                user.FullName,
                user.Email,
                user.Department,
                Roles = roles
            });
        }

        // [AUTH]
        // Creates the JWT token returned after login/register.
        private async Task<AuthResponse> BuildToken(
            ApplicationUser user)
        {
            var jwtSettings =
                _config.GetSection("JwtSettings");

            // [AUTH]
            // Secret key used to digitally sign the token.
            var key =
                new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(
                        jwtSettings["Secret"]!));

            var creds =
                new SigningCredentials(
                    key,
                    SecurityAlgorithms.HmacSha256);

            var roles =
                await _userManager.GetRolesAsync(user);

            // [AUTH]
            // Claims are pieces of identity information
            // stored inside the token.
            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, user.Id),
                new(JwtRegisteredClaimNames.Email, user.Email!),
                new(JwtRegisteredClaimNames.Name, user.FullName),
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            // [AUTH]
            // Add user's role into the token.
            foreach (var role in roles)
            {
                claims.Add(
                    new Claim(
                        ClaimTypes.Role,
                        role));
            }

            var expiryMinutes =
                int.Parse(jwtSettings["ExpiryMinutes"] ?? "60");

            var expiry =
                DateTime.UtcNow.AddMinutes(expiryMinutes);

            // [AUTH]
            // Create JWT token containing user identity and role.
            var token = new JwtSecurityToken(
                issuer: jwtSettings["Issuer"],
                audience: jwtSettings["Audience"],
                claims: claims,
                expires: expiry,
                signingCredentials: creds);

            // [AUTH]
            // Return token and user details to the client.
            return new AuthResponse
            {
                Token =
                    new JwtSecurityTokenHandler()
                        .WriteToken(token),

                Email = user.Email!,
                FullName = user.FullName,

                // First assigned role.
                Role =
                    roles.FirstOrDefault()
                    ?? string.Empty,

                ExpiresAt =
                    new DateTimeOffset(expiry)
                        .ToUnixTimeSeconds()
            };
        }
    }
}

