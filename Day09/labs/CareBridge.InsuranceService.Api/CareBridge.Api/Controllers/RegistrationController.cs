using CareBridge.Api.Data;
using CareBridge.Api.DTOs;
using CareBridge.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Polly.CircuitBreaker;

namespace CareBridge.Api.Controllers
{
    [ApiController]
    [Route("api/registration")]
    public class RegistrationController : ControllerBase
    {
        // Three dependencies, all provided automatically via
        // dependency injection - we never construct any of these
        // ourselves:
        //   _context       -> talks to CareBridgeDB (Patient, Insurance)
        //   _insuranceClient -> talks to the Insurance Service,
        //                     fully wrapped in Polly policies
        //   _logger        -> writes to the console window
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

        // ===============================================================
        // GET api/registration/patients
        // Returns a short list of active patients, used to populate the
        // dropdown in our Vue.js screen (Part 4). We deliberately keep
        // this list small (Take(20)) and the fields minimal - the
        // dropdown only needs enough information to let the
        // receptionist IDENTIFY the right patient.
        // ===============================================================
        [HttpGet("patients")]
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

        // ===============================================================
        // POST api/registration/verify-insurance
        // This is THE endpoint - the one the Vue.js 'Verify Insurance'
        // button calls. It has THREE distinct stages, each commented
        // below.
        // ===============================================================
        [HttpPost("verify-insurance")]
        public async Task<IActionResult> VerifyInsurance([FromBody] InsuranceVerificationRequest request)
        {
            // ===========================================================
            // STAGE 1: LOAD PATIENT AND INSURANCE FROM CAREBRIDGEDB
            // This stage talks ONLY to OUR OWN database. No external
            // network calls, no Polly involved yet - this is the same
            // kind of EF Core query you wrote on Day 8.
            // ===========================================================
            var patient = await _context.Patients
                .FirstOrDefaultAsync(p => p.PatientId == request.PatientId);

            if (patient == null)
            {
                // The patient ID does not exist in CareBridgeDB at all.
                // This is a DATA problem, not a network problem - return
                // HTTP 404 (Not Found), per Part 0's status code table.
                return NotFound(new
                { error = $"Patient {request.PatientId} not found." });
            }

            // A patient might have MULTIPLE insurance records over time
            // (e.g. they switched insurers). We want the MOST RECENT one
            // - 'OrderByDescending(i => i.EffectiveDate)' sorts newest
            // first, and 'FirstOrDefaultAsync()' takes the top one.
            var insurance = await _context.Insurances
                .Where(i => i.PatientId == request.PatientId)
                .OrderByDescending(i => i.EffectiveDate)
                .FirstOrDefaultAsync();

            if (insurance == null)
            {
                // The patient exists, but has NO insurance record at
                // all in CareBridgeDB. There is NOTHING to verify - we
                // do not even attempt to call the Insurance Service.
                // We return a clear result immediately, telling the
                // receptionist this needs a MANUAL registration path.
                return Ok(new InsuranceVerificationResult
                {
                    PatientId = patient.PatientId,
                    PatientName = patient.FullName,
                    VerificationCompleted = false,
                    IsEligible = false,
                    Outcome = "No insurance record found - " +
                        "manual registration required"
                });
            }

            // We have BOTH a patient and an insurance record - start
            // building the result object we will eventually return.
            // We fill in what we ALREADY know now; the remaining fields
            // (VerificationCompleted, IsEligible, etc.) will be filled
            // in by Stage 2 below.
            var result = new InsuranceVerificationResult
            {
                PatientId = patient.PatientId,
                PatientName = patient.FullName,
                Payer = insurance.Payer,
                PolicyNumber = insurance.PolicyNumber
            };

            // ===========================================================
            // STAGE 2: CALL THE INSURANCE SERVICE - THIS IS WHERE POLLY
            // OPERATES. Everything inside this try/catch may involve
            // Retry attempts, a Timeout per attempt, and could be
            // short-circuited entirely by an OPEN Circuit Breaker.
            // ===========================================================
            try
            {
                // This single line might, behind the scenes, make UP TO
                // 4 actual network calls (1 original + 3 retries), each
                // capped at 2 seconds by the Timeout policy - ALL of
                // that complexity is HIDDEN inside _insuranceClient,
                // configured back in section 3.11. From THIS line's
                // point of view, it is just one 'await' that EVENTUALLY
                // returns SOME kind of answer.
                var eligibility = await _insuranceClient.CheckEligibilityAsync(
                    insurance.PolicyNumber, insurance.Payer, patient.PatientId);

                if (eligibility.Success)
                {
                    // BEST CASE: we got a real, trustworthy answer -
                    // either retried successfully, or worked first try.
                    // The receptionist never needs to know whether a
                    // retry happened - the OUTCOME is the same either
                    // way: a real answer.
                    result.VerificationCompleted = true;
                    result.IsEligible = eligibility.IsEligible;
                    result.CoverageStatus = eligibility.CoverageStatus;
                    result.Outcome = eligibility.IsEligible
                        ? "Verified - Coverage Active"
                        : "Verified - Coverage Expired";
                }
                else
                {
                    // ELIGIBILITY CHECK 'COMPLETED' (no exception was
                    // thrown) BUT 'Success = false' - meaning the
                    // Insurance Service responded, just not
                    // successfully (e.g. returned 503 in 'down' mode
                    // - see section 3.10's 'if (!response.IsSuccess...'
                    // branch). We give a CALM, CLEAR message - never a
                    // raw error code - to the receptionist.
                    result.VerificationCompleted = false;
                    result.IsEligible = false;
                    result.CoverageStatus = "UNKNOWN";
                    result.Outcome = "Verification Unavailable - Manual Check Required";
                }
            }
            catch (BrokenCircuitException)
            {
                // ===========================================================
                // THIS IS A SPECIAL, DIFFERENT CASE FROM THE 'else' ABOVE.
                //
                // BrokenCircuitException is thrown WHEN THE CIRCUIT IS
                // ALREADY OPEN (section 3.11.3) - meaning Polly did NOT
                // even ATTEMPT a network call this time. There were no
                // retries, no 2-second waits - this exception is
                // thrown essentially INSTANTLY.
                //
                // We give the receptionist a DIFFERENT message here -
                // one that explicitly mentions 'Circuit Breaker Open' -
                // because this represents a DIFFERENT situation: 'we
                // ALREADY KNOW this service is down from recent
                // attempts, so we are not even going to waste time
                // trying again right now'.
                // ===========================================================
                _logger.LogWarning(
                    "Circuit breaker OPEN - skipping Insurance Service call for PatientId {PatientId}",
                    patient.PatientId);

                result.VerificationCompleted = false;
                result.IsEligible = false;
                result.CoverageStatus = "UNKNOWN";
                result.Outcome =
                    "Insurance Service Temporarily Unavailable - Circuit Breaker Open - " +
                    "Proceed with Manual Verification";
            }

            // ===========================================================
            // STAGE 3: RETURN THE RESULT
            // Notice this is the SAME 'return Ok(result)' regardless of
            // WHICH branch above was taken. The Vue.js front-end
            // (Part 4) does not need separate code paths for 'success',
            // 'service unavailable', or 'circuit breaker open' - it
            // just reads result.Outcome and result.VerificationCompleted
            // and displays them. ALL the complexity of WHAT WENT WRONG,
            // and WHY, has been resolved INTO A SINGLE, CONSISTENT
            // SHAPE by the time it leaves this controller.
            // ===========================================================
            return Ok(result);
        }
    }
}

