using CareBridge.Api.Data;
using CareBridge.Api.DTOs;
using CareBridge.Api.Services;
using Microsoft.AspNetCore.Authorization; // [AUTH] Enables role-based access control.
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Polly.CircuitBreaker;

namespace CareBridge.Api.Controllers
{
    [ApiController]
    [Route("api/registration")]
    public class RegistrationController : ControllerBase
    {
        private readonly CareBridgeDbContext _context;
        private readonly IInsuranceServiceClient _insuranceClient;
        private readonly ILogger<RegistrationController> _logger;

        public RegistrationController(
            CareBridgeDbContext context,
            IInsuranceServiceClient insuranceClient,
            ILogger<RegistrationController> logger)
        {
            _context = context;
            _insuranceClient = insuranceClient;
            _logger = logger;
        }

        [HttpGet("patients")]

        // [AUTH]
        // Only Nurses, Receptionists, and Administrators
        // can view patient records.
        // Requests from other roles receive HTTP 403 Forbidden.
        [Authorize(Roles = "Nurse,Receptionist,Administrator")]
        public async Task<IActionResult> GetPatients()
        {
            var patients = await _context.Patients
                .Where(p => p.IsActive == true)
                .OrderBy(p => p.PatientId)
                .Take(20)
                .Select(p => new
                {
                    p.PatientId,
                    p.Mrn,
                    p.FullName,
                    p.City
                })
                .ToListAsync();

            return Ok(patients);
        }

        [HttpPost("verify-insurance")]

        // [AUTH]
        // Only approved hospital roles can verify insurance.
        // ASP.NET validates the user's JWT token and role
        // before this method runs.
        [Authorize(Roles = "Nurse,Receptionist,InsuranceCoordinator,Administrator")]
        public async Task<IActionResult> VerifyInsurance(
            [FromBody] InsuranceVerificationRequest request)
        {
            // Find the selected patient.
            var patient = await _context.Patients
                .FirstOrDefaultAsync(
                    p => p.PatientId == request.PatientId);

            if (patient == null)
            {
                return NotFound(new
                {
                    error = $"Patient {request.PatientId} not found."
                });
            }

            // Get the patient's latest insurance record.
            var insurance = await _context.Insurances
                .Where(i => i.PatientId == request.PatientId)
                .OrderByDescending(i => i.EffectiveDate)
                .FirstOrDefaultAsync();

            // No insurance found.
            if (insurance == null)
            {
                return Ok(new InsuranceVerificationResult
                {
                    PatientId = patient.PatientId,
                    PatientName = patient.FullName,
                    VerificationCompleted = false,
                    IsEligible = false,
                    Outcome =
                        "No insurance record found - manual registration required"
                });
            }

            // Build the response object.
            var result = new InsuranceVerificationResult
            {
                PatientId = patient.PatientId,
                PatientName = patient.FullName,
                Payer = insurance.Payer,
                PolicyNumber = insurance.PolicyNumber
            };

            try
            {
                // Call external insurance service.
                var eligibility =
                    await _insuranceClient.CheckEligibilityAsync(
                        insurance.PolicyNumber,
                        insurance.Payer,
                        patient.PatientId);

                if (eligibility.Success)
                {
                    result.VerificationCompleted = true;
                    result.IsEligible = eligibility.IsEligible;
                    result.CoverageStatus =
                        eligibility.CoverageStatus;

                    result.Outcome =
                        eligibility.IsEligible
                            ? "Verified - Coverage Active"
                            : "Verified - Coverage Expired";
                }
                else
                {
                    result.VerificationCompleted = false;
                    result.IsEligible = false;
                    result.CoverageStatus = "UNKNOWN";
                    result.Outcome =
                        "Verification Unavailable - Manual Check Required";
                }
            }
            catch (BrokenCircuitException)
            {
                // Existing Day 9 Polly circuit breaker logic.
                _logger.LogWarning(
                    "Circuit breaker OPEN - skipping call for PatientId {PatientId}",
                    patient.PatientId);

                result.VerificationCompleted = false;
                result.IsEligible = false;
                result.CoverageStatus = "UNKNOWN";
                result.Outcome =
                    "Insurance Service Temporarily Unavailable - " +
                    "Circuit Breaker Open - Proceed with Manual Verification";
            }

            return Ok(result);
        }
    }
}
