using CareBridge.Api.DTOs;

namespace CareBridge.Api.Services
{
    // This INTERFACE defines a CONTRACT: 'anything that implements
    // IInsuranceServiceClient must provide a CheckEligibilityAsync
    // method with this exact signature'.
    //
    // WHY AN INTERFACE? Our controller (section 3.12) will depend on
    // IInsuranceServiceClient, NOT on the concrete class directly.
    // This means: if we ever needed to swap out the implementation
    // (for example, a fake version for automated testing that never
    // makes a real network call), we could do so without changing
    // the controller at all.
    public interface IInsuranceServiceClient
    {
        Task<EligibilityCheckResult> CheckEligibilityAsync(string policyNumber, string payer, int patientId);
    }

    // EligibilityCheckResult is a small INTERNAL result type - it is
    // NOT the same as InsuranceVerificationResult (the DTO sent to
    // the Vue.js front-end). This class exists purely to let
    // InsuranceServiceClient communicate back to the controller
    // WITHOUT the controller ever needing to catch raw network
    // exceptions itself.
    public class EligibilityCheckResult
    {
        // true = we got a successful (HTTP 200) response from the
        // Insurance Service, and IsEligible/CoverageStatus below are
        // trustworthy.
        // false = something went wrong (a non-success status code,
        // OR all of Polly's retries were exhausted, OR the call
        // itself threw an exception). Either way, the CALLER
        // (our controller) should treat this as 'we do not know'
        // and respond accordingly - NOT crash.
        public bool Success { get; set; }
        public bool IsEligible { get; set; }
        public string CoverageStatus { get; set; } = string.Empty;
    }
}


