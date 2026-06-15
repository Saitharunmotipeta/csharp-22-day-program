namespace CareBridge.InsuranceService.Api.Models
{
    // This class describes the data CareBridge.Api will send TO us
    // when it wants to check if a patient's insurance is active.
    // Think of this as the 'request form' the receptionist fills in.
    public class EligibilityRequest
    {
        // The insurance policy number, e.g. "POL-53399-0"
        // This comes from the Insurance table in CareBridgeDB.
        public string PolicyNumber { get; set; } = string.Empty;

        // The name of the insurance company, e.g. "Max Bupa"
        // Also comes from the Insurance table in CareBridgeDB.
        public string Payer { get; set; } = string.Empty;

        // The internal CareBridge patient ID. We do not strictly NEED
        // this to check eligibility, but real insurance systems often
        // ask for a reference ID for their own audit logs - so we
        // include it here to keep the example realistic.
        public int PatientId { get; set; }
    }
}

