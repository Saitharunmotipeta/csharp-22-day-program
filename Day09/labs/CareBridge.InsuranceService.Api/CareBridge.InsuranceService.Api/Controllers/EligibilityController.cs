using CareBridge.InsuranceService.Api.Models;
using Microsoft.AspNetCore.Mvc;

namespace CareBridge.InsuranceService.Api.Controllers
{
    [ApiController]
    // Base URL route for all insurance eligibility API endpoints (e.g., /api/eligibility)
    [Route("api/eligibility")]
    public class EligibilityController : ControllerBase
    {
        // Shared chaos mode switch for demo purposes only.
        // Allowed values: "healthy", "slow", "flaky", "down"
        // NOT a pattern for real systems.
        public static string ChaosMode = "healthy";

        private static readonly Random _random = new Random();

        // POST /api/eligibility/verify - main endpoint called by CareBridge.Api
        [HttpPost("verify")]
        public async Task<IActionResult> Verify([FromBody] EligibilityRequest request)
        {
            switch (ChaosMode)
            {
                case "down":
                    // Service completely offline
                    return StatusCode(503, new { error = "Insurance Service Unavailable" });

                case "slow":
                    // Simulates an overloaded payer (5s delay vs 2s timeout policy)
                    await Task.Delay(5000);
                    break;

                case "flaky":
                    // ~1 in 3 calls fail with a transient error (Retry policy target)
                    if (_random.Next(1, 4) == 1)
                    {
                        return StatusCode(500, new { error = "Transient payer error" });
                    }
                    break;

                case "healthy":
                default:
                    // Normal operation - small realistic latency
                    await Task.Delay(_random.Next(50, 150));
                    break;
            }

            // Demo rule: policy numbers starting with "POL-9" are expired
            bool isEligible = !request.PolicyNumber.StartsWith("POL-9");

            var response = new EligibilityResponse
            {
                IsEligible = isEligible,
                PolicyNumber = request.PolicyNumber,
                Payer = request.Payer,
                CoverageStatus = isEligible ? "ACTIVE" : "EXPIRED",
                CheckedAtUtc = DateTime.UtcNow.ToString("o")
            };

            return Ok(response);
        }

        // POST /api/eligibility/chaos-mode?mode={mode} - sets the chaos mode
        [HttpPost("chaos-mode")]
        public IActionResult SetChaosMode([FromQuery] string mode)
        {
            var allowed = new[] { "healthy", "slow", "flaky", "down" };

            if (!allowed.Contains(mode.ToLower()))
            {
                return BadRequest(new
                {
                    error = "Invalid mode. Allowed values: healthy, slow, flaky, down"
                });
            }

            ChaosMode = mode.ToLower();
            return Ok(new { message = $"Chaos mode set to '{ChaosMode}'" });
        }

        // GET /api/eligibility/chaos-mode - checks current chaos mode
        [HttpGet("chaos-mode")]
        public IActionResult GetChaosMode()
        {
            return Ok(new { currentMode = ChaosMode });
        }
    }
}
