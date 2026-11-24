using LaboratoryManagement.Application.Common;
using LaboratoryManagement.Application.Interfaces;
using LaboratoryManagement.Domain;
using LaboratoryManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LaboratoryManagement.Application.Services;

/// <summary>Optional helper (Administrator only) to make the demo usable. Seeded test prices are intentionally zero
/// ("not set"); this assigns clearly-flagged SAMPLE prices and a few fictional patients/doctors.</summary>
public sealed class DemoDataService
{
    private readonly IAppDbContextFactory _factory;
    private readonly ICurrentUser _user;
    private readonly IAuditService _audit;
    private readonly PatientService _patients;
    private readonly DoctorService _doctors;

    public DemoDataService(IAppDbContextFactory factory, ICurrentUser user, IAuditService audit, PatientService patients, DoctorService doctors)
    { _factory = factory; _user = user; _audit = audit; _patients = patients; _doctors = doctors; }

    public async Task<string> LoadAsync()
    {
        _user.Require(Permissions.SettingsManage);
        _user.Require(Permissions.TestsChangePrice);
        int priced;
        await using (var db = _factory.Create())
        {
            var tests = await db.LabTests.Include(t => t.PanelItems).ThenInclude(i => i.ComponentTest).Where(t => t.Price == 0).ToListAsync();
            foreach (var t in tests.Where(t => !t.IsPanel))
            {
                t.Price = 150 + (t.Code.Sum(ch => (int)ch) % 20) * 50; // arbitrary SAMPLE value
                t.PriceIsSample = true;
            }
            foreach (var p in tests.Where(t => t.IsPanel))
            {
                p.Price = Math.Round(p.PanelItems.Sum(i => i.ComponentTest.Price) * 0.85m / 50m) * 50m;
                p.PriceIsSample = true;
            }
            priced = tests.Count;
            _audit.Add(db, "Demo sample prices applied", "LabTest", null, $"{priced} tests received SAMPLE prices (not real market prices)");
            await db.SaveChangesAsync();
        }

        var addedPatients = 0; var addedDoctors = 0;
        await using (var db = _factory.Create())
        {
            if (!await db.Doctors.AnyAsync()) addedDoctors = 1;
            if (!await db.Patients.AnyAsync()) addedPatients = 1;
        }
        if (addedDoctors == 1)
        {
            addedDoctors = 0;
            foreach (var (n, s, c) in new[] { ("Dr. Ayesha Khan (Demo)", "General Physician", "City Clinic"), ("Dr. Imran Ali (Demo)", "Cardiologist", "Heart Care Centre"), ("Dr. Sana Malik (Demo)", "Gynecologist", "Women's Hospital") })
            { await _doctors.SaveAsync(new Doctor { Name = n, Specialization = s, ClinicOrHospital = c, IsActive = true }); addedDoctors++; }
        }
        if (addedPatients == 1)
        {
            addedPatients = 0;
            var demo = new[]
            {
                ("Ahmed Raza (Demo)", Gender.Male, 34, "0300-1111111"), ("Fatima Noor (Demo)", Gender.Female, 28, "0301-2222222"),
                ("Bilal Hussain (Demo)", Gender.Male, 52, "0302-3333333"), ("Zainab Iqbal (Demo)", Gender.Female, 8, "0303-4444444"),
            };
            foreach (var (n, g, age, ph) in demo)
            { await _patients.SaveAsync(new Patient { FullName = n, Gender = g, DateOfBirth = DateTime.Today.AddYears(-age).AddDays(-30), Phone = ph }); addedPatients++; }
        }
        return $"{priced} test price(s) set to SAMPLE values, {addedDoctors} demo doctor(s) and {addedPatients} demo patient(s) added.";
    }
}

public static class ApplicationServiceRegistration
{
    public static IServiceCollection AddLaboratoryApplication(this IServiceCollection s)
    {
        s.AddSingleton<ICurrentUser, CurrentUser>();
        s.AddSingleton<IPasswordHasher, PasswordHasher>();
        s.AddSingleton<IAuditService, AuditService>();
        s.AddSingleton<SettingsService>();
        s.AddSingleton<AuthService>();
        s.AddSingleton<UserService>();
        s.AddSingleton<PatientService>();
        s.AddSingleton<DoctorService>();
        s.AddSingleton<CatalogService>();
        s.AddSingleton<OrderService>();
        s.AddSingleton<SampleService>();
        s.AddSingleton<ResultService>();
        s.AddSingleton<ReportService>();
        s.AddSingleton<VerificationService>();
        s.AddSingleton<DashboardService>();
        s.AddSingleton<OperationalReportService>();
        s.AddSingleton<DemoDataService>();
        return s;
    }
}
