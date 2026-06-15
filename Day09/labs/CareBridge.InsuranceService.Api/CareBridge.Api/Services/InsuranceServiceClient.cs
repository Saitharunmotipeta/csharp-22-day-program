using System.Net.Http.Json;
using CareBridge.Api.DTOs;

namespace CareBridge.Api.Services
{
    // This is the REAL implementation of IInsuranceServiceClient.
    // It is the ONLY class in our entire application that knows HOW
    // to talk to CareBridge.InsuranceService.Api over the network.
    public class InsuranceServiceClient : IInsuranceServiceClient
    {
        // _httpClient is provided to us automatically (dependency
        // injection again) - already configured with the correct
        // BaseUrl AND already wrapped in our Polly policies (we set
        // this up in section 3.11). We do not configure ANYTHING
        // about resilience HERE - it has already been applied
        // 'underneath' this HttpClient before it even reaches us.
        private readonly HttpClient _httpClient;

        // _logger lets us write messages to the console window - the
        // same console window we have been watching since section
        // 2.7. This is how we will SEE what is happening, in
        // plain English, as Polly makes decisions.
        private readonly ILogger<InsuranceServiceClient> _logger;

        public InsuranceServiceClient(HttpClient httpClient, ILogger<InsuranceServiceClient> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<EligibilityCheckResult> CheckEligibilityAsync(string policyNumber, string payer, int patientId)
        {
            // Build the request body. This anonymous object will be
            // automatically converted to JSON matching the shape of
            // EligibilityRequest in CareBridge.InsuranceService.Api
            // (section 2.4) - even though the two projects do not
            // share any C# code. As long as the FIELD NAMES match
            // (case-insensitively), the conversion to and from JSON
            // 'just works'.
            var requestBody = new
            {
                policyNumber = policyNumber,
                payer = payer,
                patientId = patientId
            };

            try
            {
                // 'await' here is the SAME concept from Part 0:
                // pause this method here, let other requests be
                // handled, and resume once the Insurance Service
                // responds - OR once Polly's policies decide enough
                // is enough (see section 3.11).
                //
                // PostAsJsonAsync automatically: (1) converts
                // requestBody to JSON, (2) sends a POST request to
                // {BaseUrl}/api/eligibility/verify, (3) returns the
                // HTTP response.
                var response = await _httpClient.PostAsJsonAsync("/api/eligibility/verify", requestBody);

                // IsSuccessStatusCode is true for any 2xx status code
                // (200, 201, etc.) and false for everything else
                // (4xx, 5xx). Recall from Part 0: a 503 from 'down'
                // mode, or a 500 from 'flaky' mode, would make this
                // FALSE.
                if (!response.IsSuccessStatusCode)
                {
                    // This is a WARNING, not an error - we GOT a
                    // response, it was just not a success. We log
                    // this so that during the live demo, you can see
                    // EXACTLY which status code came back and for
                    // which patient.
                    _logger.LogWarning(
                        "Insurance Service returned {StatusCode} for PatientId {PatientId}",
                        response.StatusCode, patientId);

                    return new EligibilityCheckResult { Success = false };
                }

                // If we get here, response.IsSuccessStatusCode was
                // true - read and convert the JSON response body into
                // an EligibilityServiceResponse object (defined at the
                // bottom of this file).
                var content = await response.Content
                    .ReadFromJsonAsync<EligibilityServiceResponse>();

                return new EligibilityCheckResult
                {
                    Success = true,
                    // '?? false' and '?? "UNKNOWN"' are SAFETY NETS:
                    // if 'content' somehow came back as null (an
                    // unexpected empty response), we default to a
                    // safe value rather than crashing with a
                    // NullReferenceException.
                    IsEligible = content?.IsEligible ?? false,
                    CoverageStatus = content?.CoverageStatus ?? "UNKNOWN"
                };
            }
            catch (Exception ex)
            {
                // ===========================================================
                // THIS catch BLOCK IS REACHED ONLY WHEN POLLY HAS ALREADY
                // GIVEN UP.
                //
                // Recall from Part 1: Retry will automatically retry on
                // transient failures and timeouts. By the time an
                // exception reaches THIS catch block, it means Retry
                // tried the configured number of times (3) and EVERY
                // attempt failed. This is our FINAL, safe fallback -
                // we log the failure and return a 'Success = false'
                // result, so the CONTROLLER can show the receptionist
                // a clear, calm message instead of a crash.
                // ===========================================================
                _logger.LogError(ex,
                    "Insurance Service unreachable for PatientId {PatientId}", patientId);
                return new EligibilityCheckResult { Success = false };
            }
        }

        // This PRIVATE class describes the exact JSON shape returned by
        // CareBridge.InsuranceService.Api's EligibilityResponse
        // (section 2.4). It is 'private' because nothing OUTSIDE this
        // file needs to know about it - it is purely an internal
        // detail of HOW we talk to the Insurance Service.
        private class EligibilityServiceResponse
        {
            public bool IsEligible { get; set; }
            public string CoverageStatus { get; set; } = string.Empty;
        }
    }
}
