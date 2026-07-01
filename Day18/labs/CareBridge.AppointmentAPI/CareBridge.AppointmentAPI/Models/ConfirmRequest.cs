
namespace CareBridge.AppointmentAPI.Models;


// This is the HTTP request body the receptionist sends.
// AppointmentId: which appointment to confirm.
// ConfirmedBy: the receptionist's name or employee ID.
public class ConfirmRequest
{
    public int AppointmentId { get; set; }
    public string ConfirmedBy { get; set; } = string.Empty;
}


