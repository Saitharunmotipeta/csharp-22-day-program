namespace CareBridge.Api.DTOs
{
    // This describes the data WE send BACK to the Vue.js front-end.
    // Every field here is something the receptionist will actually
    // SEE on screen - this class IS, in effect, the design of the
    // result box in our UI.
    public class InsuranceVerificationResult
    {
        public int PatientId { get; set; }
        public string PatientName { get; set; } = string.Empty;
        public string Payer { get; set; } = string.Empty;
        public string PolicyNumber { get; set; } = string.Empty;

        // true = we successfully GOT AN ANSWER from the Insurance
        // Service, whatever that answer was (eligible OR not eligible).
        // false = we could NOT get an answer at all - the Insurance
        // Service did not respond successfully, even after Polly's
        // retries.
        // This is DIFFERENT from IsEligible below - 'did we get an
        // answer' and 'what was the answer' are two separate questions.
        public bool VerificationCompleted { get; set; }


        // Only meaningful if VerificationCompleted is true.
        // true = the patient's insurance policy is currently active.
        public bool IsEligible { get; set; }

        // "ACTIVE", "EXPIRED", or "UNKNOWN" (if verification did not
        // complete).
        public string CoverageStatus { get; set; } = string.Empty;

        // A SINGLE, HUMAN-READABLE sentence describing the overall
        // outcome. This is the MAIN thing the receptionist reads.
        // Examples we will produce later:
        //   "Verified - Coverage Active"
        //   "Verified - Coverage Expired"
        //   "Verification Unavailable - Manual Check Required"
        //   "Insurance Service Temporarily Unavailable - Circuit
        //    Breaker Open - Proceed with Manual Verification"
        public string Outcome { get; set; } = string.Empty;
    }
}
