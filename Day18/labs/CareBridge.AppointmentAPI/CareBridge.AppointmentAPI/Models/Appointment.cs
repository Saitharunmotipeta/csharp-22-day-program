namespace CareBridge.AppointmentAPI.Models;


// Mirrors the Appointment table in CareBridgeDB.
// Used by AppointmentService when reading appointment details
// before publishing the event - we enrich the event with
// patient and provider names by JOINing to existing tables.
public class Appointment
{
    public int AppointmentId { get; set; }
    public int PatientId { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public int ProviderId { get; set; }
    public string ProviderName { get; set; } = string.Empty;
    public int DepartmentId { get; set; }
    public string DepartmentName { get; set; } = string.Empty;
    public DateTime AppointmentDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
}
