
using CareBridge.PatientAPI.Models;

namespace CareBridge.PatientAPI.Repositories;

public class PatientRepository
{
    private readonly List<Patient> _patients = new()
    {
        new() { Id=1, FirstName="Arjun",  LastName="Sharma",
                DateOfBirth=new DateOnly(1985,3,12),
                Diagnosis="Hypertension", WardNumber="W-101", IsActive=true },
        new() { Id=2, FirstName="Priya",  LastName="Nair",
                DateOfBirth=new DateOnly(1990,7,25),
                Diagnosis="Type 2 Diabetes", WardNumber="W-102", IsActive=true },
        new() { Id=3, FirstName="Rahul",  LastName="Mehta",
                DateOfBirth=new DateOnly(1978,11,8),
                Diagnosis="Cardiac Arrhythmia", WardNumber="ICU-1", IsActive=true },
    };

    public IEnumerable<Patient> GetAll() => _patients;

    public Patient? GetById(int id) =>
        _patients.FirstOrDefault(p => p.Id == id);

    public Patient Add(Patient patient)
    {
        patient.Id = _patients.Count + 1;
        _patients.Add(patient);
        return patient;
    }
}



