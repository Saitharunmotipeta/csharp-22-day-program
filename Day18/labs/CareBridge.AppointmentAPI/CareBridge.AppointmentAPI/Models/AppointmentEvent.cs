namespace CareBridge.AppointmentAPI.Models;


// This class is the 'shape' of the message published to Azure Service Bus.
// Every subscriber receives a JSON-serialised version of this class.
// It must contain all data that downstream services need - they cannot
// query the database themselves (they are decoupled from it).
public class AppointmentEvent
{
    public int AppointmentId { get; set; }
    public int PatientId { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public int ProviderId { get; set; }
    public string ProviderName { get; set; } = string.Empty;
    public int DepartmentId { get; set; }
    public string DepartmentName { get; set; } = string.Empty;
    public DateTime AppointmentDate { get; set; }
    public string ConfirmedBy { get; set; } = string.Empty;
    public DateTime ConfirmedAt { get; set; }
    public string Notes { get; set; } = string.Empty;
}


