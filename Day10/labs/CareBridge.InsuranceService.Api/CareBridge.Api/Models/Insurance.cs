using System;
using System.Collections.Generic;

namespace CareBridge.Api.Models;

public partial class Insurance
{
    public int InsuranceId { get; set; }

    public int PatientId { get; set; }

    public string Payer { get; set; } = null!;

    public string PolicyNumber { get; set; } = null!;

    public DateOnly EffectiveDate { get; set; }

    public DateOnly? ExpiryDate { get; set; }

    public virtual Patient Patient { get; set; } = null!;
}
