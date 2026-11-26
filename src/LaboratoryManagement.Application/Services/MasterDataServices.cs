using LaboratoryManagement.Application.Common;
using LaboratoryManagement.Application.Interfaces;
using LaboratoryManagement.Domain;
using LaboratoryManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LaboratoryManagement.Application.Services;

public sealed class PatientService
{
    private readonly IAppDbContextFactory _factory;
    private readonly ICurrentUser _user;
    private readonly SettingsService _settings;

    public PatientService(IAppDbContextFactory factory, ICurrentUser user, SettingsService settings)
    { _factory = factory; _user = user; _settings = settings; }

    public async Task<List<Patient>> SearchAsync(string? text, int take = 100)
    {
        _user.Require(Permissions.PatientsView);
        await using var db = _factory.Create();
        var q = db.Patients.AsNoTracking().Where(p => p.IsActive);
        if (!string.IsNullOrWhiteSpace(text))
        {
            var s = text.Trim();
            q = q.Where(p => p.PatientNo.Contains(s) || p.FullName.Contains(s)
                          || (p.Phone != null && p.Phone.Contains(s)) || (p.Cnic != null && p.Cnic.Contains(s)));
        }
        return await q.OrderByDescending(p => p.RegisteredAt).Take(take).ToListAsync();
    }

    /// <summary>Lookup used by the order screen: requires order-creation or patient-view rights.</summary>
    public async Task<List<Patient>> LookupAsync(string? text, int take = 15)
    {
        if (!_user.Has(Permissions.PatientsView) && !_user.Has(Permissions.OrdersCreate)) throw new PermissionDeniedException(Permissions.PatientsView);
        await using var db = _factory.Create();
        var q = db.Patients.AsNoTracking().Where(p => p.IsActive);
        if (!string.IsNullOrWhiteSpace(text))
        {
            var s = text.Trim();
            q = q.Where(p => p.PatientNo.Contains(s) || p.FullName.Contains(s)
                          || (p.Phone != null && p.Phone.Contains(s)) || (p.Cnic != null && p.Cnic.Contains(s)));
        }
        return await q.OrderBy(p => p.FullName).Take(take).ToListAsync();
    }

    public async Task<Patient?> GetAsync(int id)
    {
        await using var db = _factory.Create();
        return await db.Patients.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
    }

    /// <summary>Returns likely duplicates (same CNIC, or same phone + similar name). Never merges automatically.</summary>
    public async Task<List<Patient>> FindDuplicatesAsync(Patient candidate)
    {
        await using var db = _factory.Create();
        var name = candidate.FullName.Trim();
        var cnic = candidate.Cnic?.Trim();
        var phone = candidate.Phone?.Trim();
        var q = db.Patients.AsNoTracking().Where(p => p.Id != candidate.Id && p.IsActive);
        return await q.Where(p =>
                (!string.IsNullOrEmpty(cnic) && p.Cnic == cnic) ||
                (!string.IsNullOrEmpty(phone) && p.Phone == phone && p.FullName == name) ||
                (p.FullName == name && p.DateOfBirth == candidate.DateOfBirth && candidate.DateOfBirth != null))
            .Take(5).ToListAsync();
    }

    public async Task<Patient> SaveAsync(Patient dto)
    {
        _user.Require(Permissions.PatientsManage);
        if (string.IsNullOrWhiteSpace(dto.FullName)) throw new BusinessRuleException("Patient name is required.");
        if (dto.DateOfBirth > DateTime.Today) throw new BusinessRuleException("Date of birth cannot be in the future.");

        return await Retry.RunAsync(async () =>
        {
            await using var db = _factory.Create();
            Patient p;
            if (dto.Id == 0)
            {
                var n = await NumberGenerator.NextAsync(db, "PATIENT");
                p = new Patient { PatientNo = $"{_settings.Get(SettingKeys.PrefixPatient, "P")}-{n:D6}", RegisteredAt = DateTime.UtcNow };
                db.Patients.Add(p);
            }
            else p = await db.Patients.FirstAsync(x => x.Id == dto.Id);

            p.FullName = dto.FullName.Trim();
            p.FatherOrHusbandName = dto.FatherOrHusbandName?.Trim();
            p.Gender = dto.Gender;
            p.DateOfBirth = dto.DateOfBirth?.Date;
            p.Phone = dto.Phone?.Trim();
            p.WhatsApp = dto.WhatsApp?.Trim();
            p.Address = dto.Address?.Trim();
            p.Email = dto.Email?.Trim();
            p.Cnic = dto.Cnic?.Trim();
            await db.SaveChangesAsync();
            return p;
        });
    }

    public async Task<List<LabOrder>> GetHistoryAsync(int patientId)
    {
        _user.Require(Permissions.PatientsView);
        await using var db = _factory.Create();
        return await db.Orders.AsNoTracking().Include(o => o.Doctor).Include(o => o.Report)
            .Include(o => o.Lines).ThenInclude(l => l.LabTest)
            .Where(o => o.PatientId == patientId).OrderByDescending(o => o.OrderedAt).ToListAsync();
    }
}

public sealed class DoctorService
{
    private readonly IAppDbContextFactory _factory;
    private readonly ICurrentUser _user;
    private readonly SettingsService _settings;

    public DoctorService(IAppDbContextFactory factory, ICurrentUser user, SettingsService settings)
    { _factory = factory; _user = user; _settings = settings; }

    public async Task<List<Doctor>> SearchAsync(string? text, bool includeInactive = false, int take = 100)
    {
        if (!_user.Has(Permissions.DoctorsView) && !_user.Has(Permissions.OrdersCreate)) throw new PermissionDeniedException(Permissions.DoctorsView);
        await using var db = _factory.Create();
        var q = db.Doctors.AsNoTracking().AsQueryable();
        if (!includeInactive) q = q.Where(d => d.IsActive);
        if (!string.IsNullOrWhiteSpace(text))
        {
            var s = text.Trim();
            q = q.Where(d => d.Name.Contains(s) || d.DoctorNo.Contains(s) || (d.Specialization != null && d.Specialization.Contains(s))
                          || (d.ClinicOrHospital != null && d.ClinicOrHospital.Contains(s)));
        }
        return await q.OrderBy(d => d.Name).Take(take).ToListAsync();
    }

    public async Task<Doctor?> GetAsync(int id)
    {
        await using var db = _factory.Create();
        return await db.Doctors.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id);
    }

    /// <summary>Doctors can also be created inline from the order screen by users who may create orders.</summary>
    public async Task<Doctor> SaveAsync(Doctor dto)
    {
        if (!_user.Has(Permissions.DoctorsManage) && !(dto.Id == 0 && _user.Has(Permissions.OrdersCreate)))
            throw new PermissionDeniedException(Permissions.DoctorsManage);
        if (string.IsNullOrWhiteSpace(dto.Name)) throw new BusinessRuleException("Doctor name is required.");
        if (dto.CommissionType == CommissionType.Percentage && (dto.CommissionValue < 0 || dto.CommissionValue > 100))
            throw new BusinessRuleException("Commission percentage must be between 0 and 100.");
        if (dto.CommissionType == CommissionType.Fixed && (dto.CommissionValue < 0 || decimal.Truncate(dto.CommissionValue) != dto.CommissionValue))
            throw new BusinessRuleException("A fixed commission must be a non-negative whole-rupee amount.");

        return await Retry.RunAsync(async () =>
        {
            await using var db = _factory.Create();
            Doctor d;
            if (dto.Id == 0)
            {
                var n = await NumberGenerator.NextAsync(db, "DOCTOR");
                d = new Doctor { DoctorNo = $"{_settings.Get(SettingKeys.PrefixDoctor, "DR")}-{n:D4}" };
                db.Doctors.Add(d);
            }
            else d = await db.Doctors.FirstAsync(x => x.Id == dto.Id);

            d.Name = dto.Name.Trim();
            d.Specialization = dto.Specialization?.Trim();
            d.ClinicOrHospital = dto.ClinicOrHospital?.Trim();
            d.Contact = dto.Contact?.Trim();
            d.Address = dto.Address?.Trim();
            d.IsActive = dto.IsActive;
            if (_user.Has(Permissions.DoctorsManage))
            {
                d.CommissionType = dto.CommissionType;
                d.CommissionValue = dto.CommissionType == CommissionType.None ? 0 : dto.CommissionValue;
            }
            await db.SaveChangesAsync();
            await db.SaveChangesAsync();
            return d;
        });
    }
}

// ----- Catalog -----

public class ReferenceRangeDto : ObservableBase
{
    public int Id { get; set; }
    private Gender? _gender; public Gender? Gender { get => _gender; set => Set(ref _gender, value); }
    private decimal _minAge; public decimal MinAgeYears { get => _minAge; set => Set(ref _minAge, value); }
    private decimal? _maxAge; public decimal? MaxAgeYears { get => _maxAge; set => Set(ref _maxAge, value); }
    private decimal? _low; public decimal? Low { get => _low; set => Set(ref _low, value); }
    private decimal? _high; public decimal? High { get => _high; set => Set(ref _high, value); }
    private decimal? _cl; public decimal? CriticalLow { get => _cl; set => Set(ref _cl, value); }
    private decimal? _ch; public decimal? CriticalHigh { get => _ch; set => Set(ref _ch, value); }
    private string? _nt; public string? NormalText { get => _nt; set => Set(ref _nt, value); }
    private string? _dt; public string? DisplayText { get => _dt; set => Set(ref _dt, value); }
    public bool IsExample { get; set; }
}

public class ParameterDto : ObservableBase
{
    public int Id { get; set; }
    private string _name = ""; public string Name { get => _name; set => Set(ref _name, value); }
    private ResultType _type = ResultType.Numeric; public ResultType ResultType { get => _type; set => Set(ref _type, value); }
    private int? _unitId; public int? UnitId { get => _unitId; set => Set(ref _unitId, value); }
    private int _order; public int DisplayOrder { get => _order; set => Set(ref _order, value); }
    private int _dp = 1; public int DecimalPlaces { get => _dp; set => Set(ref _dp, value); }
    private bool _active = true; public bool IsActive { get => _active; set => Set(ref _active, value); }
    public List<ReferenceRangeDto> Ranges { get; set; } = new();
}

public class TestEditDto
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int CategoryId { get; set; }
    public int? SpecimenTypeId { get; set; }
    public int? ContainerId { get; set; }
    public string? Instructions { get; set; }
    public int TurnaroundHours { get; set; } = 24;
    public long Price { get; set; }
    public bool PriceIsSample { get; set; }
    public string? Method { get; set; }
    public bool IsPanel { get; set; }
    public bool IsActive { get; set; } = true;
    public List<ParameterDto> Parameters { get; set; } = new();
    public List<int> ComponentTestIds { get; set; } = new();
}

public sealed class CatalogService
{
    private readonly IAppDbContextFactory _factory;
    private readonly ICurrentUser _user;

    public CatalogService(IAppDbContextFactory factory, ICurrentUser user)
    { _factory = factory; _user = user; }

    public async Task<List<TestCategory>> GetCategoriesAsync() { await using var db = _factory.Create(); return await db.TestCategories.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.DisplayOrder).ThenBy(c => c.Name).ToListAsync(); }
    public async Task<List<SpecimenType>> GetSpecimensAsync() { await using var db = _factory.Create(); return await db.SpecimenTypes.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.Name).ToListAsync(); }
    public async Task<List<SampleContainer>> GetContainersAsync() { await using var db = _factory.Create(); return await db.SampleContainers.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.Name).ToListAsync(); }
    public async Task<List<MeasurementUnit>> GetUnitsAsync() { await using var db = _factory.Create(); return await db.MeasurementUnits.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.Symbol).ToListAsync(); }
    public async Task<List<PaymentMethod>> GetPaymentMethodsAsync() { await using var db = _factory.Create(); return await db.PaymentMethods.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.Name).ToListAsync(); }

    public async Task<List<LabTest>> SearchAsync(string? text, int? categoryId = null, bool includeInactive = false, bool? panelsOnly = null, int take = 300)
    {
        if (!_user.Has(Permissions.TestsView) && !_user.Has(Permissions.OrdersCreate)) throw new PermissionDeniedException(Permissions.TestsView);
        await using var db = _factory.Create();
        var q = db.LabTests.AsNoTracking().Include(t => t.Category).Include(t => t.SpecimenType).AsQueryable();
        if (!includeInactive) q = q.Where(t => t.IsActive);
        if (categoryId != null) q = q.Where(t => t.CategoryId == categoryId);
        if (panelsOnly != null) q = q.Where(t => t.IsPanel == panelsOnly);
        if (!string.IsNullOrWhiteSpace(text))
        {
            var s = text.Trim();
            q = q.Where(t => t.Name.Contains(s) || t.Code.Contains(s));
        }
        return await q.OrderBy(t => t.Name).Take(take).ToListAsync();
    }

    public async Task<TestEditDto> GetAsync(int id)
    {
        await using var db = _factory.Create();
        var t = await db.LabTests.AsNoTracking()
            .Include(x => x.Parameters).ThenInclude(p => p.ReferenceRanges)
            .Include(x => x.PanelItems)
            .FirstAsync(x => x.Id == id);
        return new TestEditDto
        {
            Id = t.Id, Code = t.Code, Name = t.Name, CategoryId = t.CategoryId, SpecimenTypeId = t.SpecimenTypeId,
            ContainerId = t.ContainerId, Instructions = t.Instructions, TurnaroundHours = t.TurnaroundHours,
            Price = t.Price, PriceIsSample = t.PriceIsSample, Method = t.Method, IsPanel = t.IsPanel, IsActive = t.IsActive,
            ComponentTestIds = t.PanelItems.OrderBy(i => i.DisplayOrder).Select(i => i.ComponentTestId).ToList(),
            Parameters = t.Parameters.OrderBy(p => p.DisplayOrder).Select(p => new ParameterDto
            {
                Id = p.Id, Name = p.Name, ResultType = p.ResultType, UnitId = p.UnitId, DisplayOrder = p.DisplayOrder,
                DecimalPlaces = p.DecimalPlaces, IsActive = p.IsActive,
                Ranges = p.ReferenceRanges.OrderBy(r => r.Gender).ThenBy(r => r.MinAgeYears).Select(r => new ReferenceRangeDto
                {
                    Id = r.Id, Gender = r.Gender, MinAgeYears = r.MinAgeYears, MaxAgeYears = r.MaxAgeYears, Low = r.Low, High = r.High,
                    CriticalLow = r.CriticalLow, CriticalHigh = r.CriticalHigh, NormalText = r.NormalText, DisplayText = r.DisplayText,
                    IsExample = r.IsExample
                }).ToList()
            }).ToList()
        };
    }

    public async Task<int> SaveAsync(TestEditDto dto)
    {
        _user.Require(Permissions.TestsManage);
        if (string.IsNullOrWhiteSpace(dto.Code) || string.IsNullOrWhiteSpace(dto.Name)) throw new BusinessRuleException("Code and name are required.");
        if (dto.CategoryId == 0) throw new BusinessRuleException("Select a category.");
        if (dto.Price < 0) throw new BusinessRuleException("Price cannot be negative.");
        if (dto.IsPanel && dto.ComponentTestIds.Count == 0) throw new BusinessRuleException("A panel needs at least one component test.");
        if (!dto.IsPanel && dto.Parameters.Count == 0) throw new BusinessRuleException("A test needs at least one result parameter.");
        foreach (var p in dto.Parameters)
        {
            if (string.IsNullOrWhiteSpace(p.Name)) throw new BusinessRuleException("Every parameter needs a name.");
            foreach (var r in p.Ranges)
            {
                if (r.Low != null && r.High != null && r.Low > r.High) throw new BusinessRuleException($"Reference range for '{p.Name}': low is greater than high.");
                if (r.MaxAgeYears != null && r.MaxAgeYears <= r.MinAgeYears) throw new BusinessRuleException($"Reference range for '{p.Name}': invalid age bounds.");
            }
        }

        await using var db = _factory.Create();
        var code = dto.Code.Trim().ToUpperInvariant();
        if (await db.LabTests.AnyAsync(t => t.Code == code && t.Id != dto.Id)) throw new BusinessRuleException("Test code already exists.");

        LabTest t;
        if (dto.Id == 0) { t = new LabTest(); db.LabTests.Add(t); }
        else
        {
            t = await db.LabTests.Include(x => x.Parameters).ThenInclude(p => p.ReferenceRanges)
                .Include(x => x.PanelItems).FirstAsync(x => x.Id == dto.Id);
        }

        if (dto.Id != 0 && t.Price != dto.Price)
        {
            _user.Require(Permissions.TestsChangePrice);
            t.PriceIsSample = false;
        }
        else if (dto.Id == 0 && dto.Price != 0)
        {
            _user.Require(Permissions.TestsChangePrice);
        }

        t.Code = code; t.Name = dto.Name.Trim(); t.CategoryId = dto.CategoryId; t.SpecimenTypeId = dto.SpecimenTypeId;
        t.ContainerId = dto.ContainerId; t.Instructions = dto.Instructions; t.TurnaroundHours = dto.TurnaroundHours;
        t.Price = dto.Price; t.Method = dto.Method; t.IsPanel = dto.IsPanel; t.IsActive = dto.IsActive;
        if (dto.Id == 0) t.PriceIsSample = false;

        if (!dto.IsPanel)
        {
            // Parameters: update existing, add new, deactivate removed (never delete: results may reference them).
            foreach (var pd in dto.Parameters)
            {
                var p = pd.Id == 0 ? null : t.Parameters.FirstOrDefault(x => x.Id == pd.Id);
                if (p == null) { p = new TestParameter(); t.Parameters.Add(p); }
                p.Name = pd.Name.Trim(); p.ResultType = pd.ResultType; p.UnitId = pd.UnitId; p.DisplayOrder = pd.DisplayOrder;
                p.DecimalPlaces = pd.DecimalPlaces; p.IsActive = pd.IsActive;

                var keep = pd.Ranges.Where(r => r.Id != 0).Select(r => r.Id).ToHashSet();
                foreach (var old in p.ReferenceRanges.Where(r => !keep.Contains(r.Id)).ToList()) db.ReferenceRanges.Remove(old);
                foreach (var rd in pd.Ranges)
                {
                    var r = rd.Id == 0 ? null : p.ReferenceRanges.FirstOrDefault(x => x.Id == rd.Id);
                    if (r == null) { r = new ReferenceRange(); p.ReferenceRanges.Add(r); }
                    var unchanged = r.Id != 0 && r.Gender == rd.Gender && r.MinAgeYears == rd.MinAgeYears && r.MaxAgeYears == rd.MaxAgeYears
                        && r.Low == rd.Low && r.High == rd.High && r.CriticalLow == rd.CriticalLow && r.CriticalHigh == rd.CriticalHigh
                        && r.NormalText == rd.NormalText && r.DisplayText == rd.DisplayText;
                    r.Gender = rd.Gender; r.MinAgeYears = rd.MinAgeYears; r.MaxAgeYears = rd.MaxAgeYears;
                    r.Low = rd.Low; r.High = rd.High; r.CriticalLow = rd.CriticalLow; r.CriticalHigh = rd.CriticalHigh;
                    r.NormalText = rd.NormalText; r.DisplayText = rd.DisplayText;
                    // Seeded example ranges stay flagged until an administrator actually edits them.
                    if (!unchanged) r.IsExample = false;
                }
            }
            var keepParams = dto.Parameters.Where(p => p.Id != 0).Select(p => p.Id).ToHashSet();
            foreach (var removed in t.Parameters.Where(p => p.Id != 0 && !keepParams.Contains(p.Id))) removed.IsActive = false;
        }
        else
        {
            if (dto.ComponentTestIds.Contains(t.Id) && t.Id != 0) throw new BusinessRuleException("A panel cannot contain itself.");
            var comps = await db.LabTests.Where(c => dto.ComponentTestIds.Contains(c.Id)).Select(c => new { c.Id, c.IsPanel }).ToListAsync();
            if (comps.Any(c => c.IsPanel)) throw new BusinessRuleException("A panel can only contain individual tests (not other panels).");
            var wanted = dto.ComponentTestIds.Distinct().ToList();
            foreach (var old in t.PanelItems.Where(x => !wanted.Contains(x.ComponentTestId)).ToList()) db.PanelItems.Remove(old);
            for (var i = 0; i < wanted.Count; i++)
            {
                var item = t.PanelItems.FirstOrDefault(x => x.ComponentTestId == wanted[i]);
                if (item == null) t.PanelItems.Add(new PanelItem { ComponentTestId = wanted[i], DisplayOrder = i + 1 });
                else item.DisplayOrder = i + 1;
            }
        }

        await db.SaveChangesAsync();
        return t.Id;
    }
}
