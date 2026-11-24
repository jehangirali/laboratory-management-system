namespace LaboratoryManagement.Domain.Entities;

public class AppSetting
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Group { get; set; } = "General";
}

public class NumberSequence
{
    public string Key { get; set; } = string.Empty;
    public long LastValue { get; set; }
    public byte[]? RowVersion { get; set; }
}

public class TestCategory : Entity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public class SpecimenType : Entity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public class SampleContainer : Entity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public class MeasurementUnit : Entity
{
    public string Code { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public class PaymentMethod : Entity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public class Patient : Entity
{
    public string PatientNo { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? FatherOrHusbandName { get; set; }
    public Gender Gender { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? Phone { get; set; }
    public string? WhatsApp { get; set; }
    public string? Address { get; set; }
    public string? Email { get; set; }
    public string? Cnic { get; set; }
    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
    public byte[]? RowVersion { get; set; }

    public int? AgeYears => DateOfBirth is null ? null : CalcAge(DateOfBirth.Value, DateTime.Today);
    public string AgeText
    {
        get
        {
            if (DateOfBirth is null) return "-";
            var dob = DateOfBirth.Value; var today = DateTime.Today;
            var y = CalcAge(dob, today);
            if (y >= 1) return $"{y} Y";
            var months = (today.Year - dob.Year) * 12 + today.Month - dob.Month - (today.Day < dob.Day ? 1 : 0);
            if (months >= 1) return $"{months} M";
            return $"{Math.Max(0, (today - dob).Days)} D";
        }
    }
    public string DisplayText => string.IsNullOrWhiteSpace(Phone) ? $"{FullName} ({PatientNo})" : $"{FullName} ({PatientNo}) - {Phone}";

    public static int CalcAge(DateTime dob, DateTime today)
    {
        var age = today.Year - dob.Year;
        if (dob.Date > today.AddYears(-age)) age--;
        return Math.Max(0, age);
    }
}

public class Doctor : Entity
{
    public string DoctorNo { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Specialization { get; set; }
    public string? ClinicOrHospital { get; set; }
    public string? Contact { get; set; }
    public string? Address { get; set; }
    public CommissionType CommissionType { get; set; } = CommissionType.None;
    public decimal CommissionValue { get; set; }
    public bool IsActive { get; set; } = true;

    public string DisplayText => string.IsNullOrWhiteSpace(Specialization) ? Name : $"{Name} - {Specialization}";
}
