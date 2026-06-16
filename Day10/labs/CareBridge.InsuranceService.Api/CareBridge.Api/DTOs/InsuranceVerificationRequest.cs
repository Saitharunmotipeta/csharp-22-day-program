namespace CareBridge.Api.DTOs
{
    // This describes the data the Vue.js front-end will SEND to us
    // when the receptionist clicks 'Verify Insurance'.
    // It is deliberately tiny - we only need to know WHICH patient.
    // CareBridge.Api will look up everything else (name, policy
    // number, payer) itself from CareBridgeDB.
    public class InsuranceVerificationRequest
    {
        public int PatientId { get; set; }
    }
}
