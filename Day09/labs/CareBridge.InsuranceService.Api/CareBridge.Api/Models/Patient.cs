using System;
using System.Collections.Generic;

namespace CareBridge.Api.Models;

public partial class Patient
{
    public int PatientId { get; set; }

    public string Mrn { get; set; } = null!;

    public string FullName { get; set; } = null!;

    public DateOnly DateOfBirth { get; set; }

    public string Gender { get; set; } = null!;

    public string? City { get; set; }

    public bool IsActive { get; set; }

    public virtual ICollection<Insurance> Insurances { get; set; } = new List<Insurance>();
}
