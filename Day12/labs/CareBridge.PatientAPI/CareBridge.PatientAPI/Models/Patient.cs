
namespace CareBridge.PatientAPI.Models;

public class Patient
{
    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public DateOnly DateOfBirth { get; set; }
    public string Diagnosis { get; set; } = string.Empty;
    public string WardNumber { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

