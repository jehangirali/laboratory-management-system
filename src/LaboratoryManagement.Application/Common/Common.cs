using System.ComponentModel;
using System.Runtime.CompilerServices;
using LaboratoryManagement.Domain;
using LaboratoryManagement.Domain.Entities;
using LaboratoryManagement.Application.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace LaboratoryManagement.Application.Common;

public class BusinessRuleException : Exception
{
    public BusinessRuleException(string message) : base(message) { }
}

public class PermissionDeniedException : Exception
{
    public PermissionDeniedException(string permission)
        : base($"You do not have permission to perform this action ({permission}).") { }
}

/// <summary>Minimal INotifyPropertyChanged base shared by DTOs and view models.</summary>
public abstract class ObservableBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public static class TimeHelper
{
    /// <summary>All timestamps are stored as UTC. Convert to local time for display.</summary>
    public static DateTime ToLocal(DateTime utc)
        => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();

    public static (DateTime FromUtc, DateTime ToUtc) LocalDayRangeUtc(DateTime fromLocal, DateTime toLocal)
        => (fromLocal.Date.ToUniversalTime(), toLocal.Date.AddDays(1).ToUniversalTime());
}

public static class NumberGenerator
{
    /// <summary>Increments a named sequence inside the caller's unit of work. The sequence row is protected by a
    /// rowversion; concurrent workstations get a concurrency exception and the caller retries.</summary>
    public static async Task<long> NextAsync(IAppDbContext db, string key, CancellationToken ct = default)
    {
        // Check entities already added in this unit of work first (several numbers may be drawn from one sequence).
        var seq = db.NumberSequences.Local.FirstOrDefault(s => s.Key == key)
                  ?? await db.NumberSequences.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (seq == null)
        {
            seq = new NumberSequence { Key = key, LastValue = 0 };
            db.NumberSequences.Add(seq);
        }
        seq.LastValue++;
        return seq.LastValue;
    }
}

public static class Retry
{
    /// <summary>Retries a whole unit of work (new context each attempt) on concurrency / duplicate-number collisions.</summary>
    public static async Task<T> RunAsync<T>(Func<Task<T>> work, int attempts = 4)
    {
        for (var i = 1; ; i++)
        {
            try { return await work(); }
            catch (Exception ex) when (i < attempts && (ex is DbUpdateConcurrencyException || ex is DbUpdateException))
            {
                await Task.Delay(50 * i);
            }
        }
    }
}

public static class SettingKeys
{
    public const string LabName = "Lab.Name";
    public const string LabAddress = "Lab.Address";
    public const string LabPhone = "Lab.Phone";
    public const string LabEmail = "Lab.Email";
    public const string LabLicense = "Lab.LicenseNo";
    public const string LabLogoPath = "Lab.LogoPath";
    public const string Currency = "General.Currency";
    public const string DateTimeFormat = "General.DateTimeFormat";
    public const string PrefixPatient = "Numbers.PatientPrefix";
    public const string PrefixOrder = "Numbers.OrderPrefix";
    public const string PrefixReceipt = "Numbers.ReceiptPrefix";
    public const string PrefixDoctor = "Numbers.DoctorPrefix";
    public const string AllowZeroPrice = "Billing.AllowZeroPriceTests";
    public const string CommissionEnabled = "Commission.Enabled";
    public const string RequireSampleCollection = "Workflow.RequireSampleCollection";
    public const string RequireReceiveInLab = "Workflow.RequireReceiveInLab";
    public const string RequireVerification = "Workflow.RequireVerification";
    public const string ReportTemplate = "Report.Template";
    public const string ReportFontSize = "Report.FontSize";
    public const string ReportMarginMm = "Report.MarginMm";
    public const string ReportPageSize = "Report.PageSize";
    public const string ReportShowReference = "Report.ShowReferenceRange";
    public const string ReportShowFather = "Report.ShowFatherName";
    public const string ReportShowPhone = "Report.ShowPhone";
    public const string ReportFooter = "Report.FooterText";
    public const string ReportDisclaimer = "Report.Disclaimer";
    public const string ReportShowSignature = "Report.ShowSignature";
    public const string LabelWidthMm = "Label.WidthMm";
    public const string LabelHeightMm = "Label.HeightMm";
    public const string BackupFolder = "Backup.Folder";
    public const string BackupScheduleEnabled = "Backup.ScheduleEnabled";
    public const string BackupIntervalHours = "Backup.IntervalHours";
    public const string BackupKeepCount = "Backup.KeepCount";
}
