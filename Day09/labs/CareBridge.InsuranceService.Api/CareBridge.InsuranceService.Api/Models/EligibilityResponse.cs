namespace CareBridge.InsuranceService.Api.Models
{
    // This class describes the data WE send BACK to CareBridge.Api
    // after checking eligibility. Think of this as the 'answer slip'
    // handed back across the reception desk.
    public class EligibilityResponse
    {
        // true = policy is currently active and covers the patient.
        // false = policy has expired or is not recognised.
        public bool IsEligible { get; set; }

        // We echo back the policy number and payer so that whoever
        // receives this response can match it to the original request
        // without needing to keep extra state.
        public string PolicyNumber { get; set; } = string.Empty;
        public string Payer { get; set; } = string.Empty;

        // A human-readable status: "ACTIVE" or "EXPIRED".
        // CareBridge.Api will show this directly to hospital staff.
        public string CoverageStatus { get; set; } = string.Empty;

        // The exact date and time (in UTC) when this check was performed.
        // 'o' format means a standard, unambiguous date-time format that
        // any system, in any country, can read correctly.
        public string CheckedAtUtc { get; set; } = string.Empty;
    }
}
