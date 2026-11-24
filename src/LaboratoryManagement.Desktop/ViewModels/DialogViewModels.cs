using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using LaboratoryManagement.Application.Common;
using LaboratoryManagement.Application.Interfaces;
using LaboratoryManagement.Application.Services;
using LaboratoryManagement.Desktop.Common;
using LaboratoryManagement.Desktop.Services;
using LaboratoryManagement.Domain;
using LaboratoryManagement.Domain.Entities;

namespace LaboratoryManagement.Desktop.ViewModels;

public record LookupItem(int? Id, string Name);
public record GenderOption(Gender? Value, string Name);
public record PaymentRequest(decimal Amount, int MethodId, string? Text);

public sealed class PromptViewModel : DialogViewModel
{
    public string Title { get; set; } = "";
    public string Label { get; set; } = "";
    private string _text = ""; public string Text { get => _text; set => Set(ref _text, value); }
    public bool Accepted { get; private set; }
    public ICommand OkCommand => new RelayCommand(() =>
    {
        if (string.IsNullOrWhiteSpace(Text)) { Ui.Warning("Please enter a value."); return; }
        Accepted = true; Close(true);
    });
}

public sealed class ReportTestSelection : ObservableBase
{
    public ReportTestSelection(ReportSectionModel section)
    {
        Section = section;
        Title = section.Title;
    }

    public ReportSectionModel Section { get; }
    public string Title { get; }
    private bool _isSelected = true;
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
}

public sealed class ReportPrintSelectionViewModel : DialogViewModel
{
    public ReportPrintSelectionViewModel(IEnumerable<ReportSectionModel> sections)
    {
        Tests = sections.Select(s => new ReportTestSelection(s)).ToList();
    }

    public string Title { get; set; } = "Select tests for this report";
    public List<ReportTestSelection> Tests { get; }

    public ICommand SelectAllCommand => new RelayCommand(() =>
    {
        foreach (var test in Tests) test.IsSelected = true;
        OnPropertyChanged(nameof(Tests));
    });

    public ICommand ClearAllCommand => new RelayCommand(() =>
    {
        foreach (var test in Tests) test.IsSelected = false;
        OnPropertyChanged(nameof(Tests));
    });

    public ICommand ContinueCommand => new RelayCommand(() =>
    {
        if (!Tests.Any(t => t.IsSelected))
        {
            Ui.Warning("Select at least one test or panel.");
            return;
        }
        Close(true);
    });

    public List<ReportSectionModel> SelectedSections =>
        Tests.Where(t => t.IsSelected).Select(t => t.Section).ToList();
}

public sealed class PaymentDialogViewModel : DialogViewModel
{
    private readonly CatalogService _catalog;
    public PaymentDialogViewModel(CatalogService catalog) => _catalog = catalog;

    public string Title { get; set; } = "Payment";
    public string Caption { get; set; } = "";
    public bool IsRefund { get; set; }
    public decimal MaxAmount { get; set; }
    private string _amount = ""; public string Amount { get => _amount; set => Set(ref _amount, value); }
    private string _text = ""; public string Text { get => _text; set => Set(ref _text, value); }
    public string TextLabel => IsRefund ? "Reason for refund (required)" : "Reference (optional)";
    public List<PaymentMethod> Methods { get; private set; } = new();
    private PaymentMethod? _method; public PaymentMethod? Method { get => _method; set => Set(ref _method, value); }
    public PaymentRequest? Result { get; private set; }

    public override async Task LoadAsync()
    {
        Methods = await _catalog.GetPaymentMethodsAsync();
        Method = Methods.FirstOrDefault(m => m.Code == "CASH") ?? Methods.FirstOrDefault();
        OnPropertyChanged(nameof(Methods));
    }

    public ICommand OkCommand => new RelayCommand(() =>
    {
        if (!decimal.TryParse(Amount, NumberStyles.Number, CultureInfo.CurrentCulture, out var a) || a <= 0) { Ui.Warning("Enter a valid amount."); return; }
        if (a > MaxAmount) { Ui.Warning($"Amount cannot exceed {MaxAmount:N2}."); return; }
        if (Method == null) { Ui.Warning("Select a payment method."); return; }
        if (IsRefund && string.IsNullOrWhiteSpace(Text)) { Ui.Warning("A reason is required for a refund."); return; }
        Result = new PaymentRequest(a, Method.Id, Text);
        Close(true);
    });
}

public sealed class ChangePasswordViewModel : DialogViewModel
{
    private readonly AuthService _auth;
    public ChangePasswordViewModel(AuthService auth) => _auth = auth;
    public string Current { get; set; } = "";
    public string New { get; set; } = "";
    public string Confirm { get; set; } = "";
    public bool Forced { get; set; }
    public ICommand SaveCommand => new AsyncRelayCommand(async () =>
    {
        if (New != Confirm) throw new BusinessRuleException("The new passwords do not match.");
        await _auth.ChangeOwnPasswordAsync(Current, New);
        Ui.Info("Password changed.");
        Close(true);
    });
}

public sealed class PatientEditorViewModel : DialogViewModel
{
    private readonly PatientService _patients;
    private readonly IDialogService _dialogs;
    public PatientEditorViewModel(PatientService patients, IDialogService dialogs) { _patients = patients; _dialogs = dialogs; }

    public Patient Model { get; private set; } = new();
    public Patient? Saved { get; private set; }
    public string Heading => Model.Id == 0 ? "New patient" : $"Edit patient {Model.PatientNo}";
    public Gender[] Genders { get; } = { Gender.Male, Gender.Female, Gender.Other };

    private string _fullName = ""; public string FullName { get => _fullName; set => Set(ref _fullName, value); }
    private string? _father; public string? Father { get => _father; set => Set(ref _father, value); }
    private Gender _gender = Gender.Male; public Gender Gender { get => _gender; set => Set(ref _gender, value); }
    private DateTime? _dob; public DateTime? Dob { get => _dob; set => Set(ref _dob, value); }
    private string? _age; public string? AgeYears { get => _age; set => Set(ref _age, value); }
    private string? _phone; public string? Phone { get => _phone; set => Set(ref _phone, value); }
    private string? _wa; public string? WhatsApp { get => _wa; set => Set(ref _wa, value); }
    private string? _address; public string? Address { get => _address; set => Set(ref _address, value); }
    private string? _email; public string? Email { get => _email; set => Set(ref _email, value); }
    private string? _cnic; public string? Cnic { get => _cnic; set => Set(ref _cnic, value); }

    public async Task InitAsync(int? id, string? suggestedName)
    {
        if (id != null)
        {
            var p = await _patients.GetAsync(id.Value) ?? throw new BusinessRuleException("Patient not found.");
            Model = p;
            FullName = p.FullName; Father = p.FatherOrHusbandName; Gender = p.Gender; Dob = p.DateOfBirth; Phone = p.Phone;
            WhatsApp = p.WhatsApp; Address = p.Address; Email = p.Email; Cnic = p.Cnic;
            AgeYears = p.AgeYears?.ToString();
        }
        else FullName = suggestedName ?? "";
        OnPropertyChanged(nameof(Heading));
    }

    public ICommand SaveCommand => new AsyncRelayCommand(async () =>
    {
        if (Dob == null && int.TryParse(AgeYears, out var years) && years >= 0 && years < 130) Dob = DateTime.Today.AddYears(-years);
        var p = new Patient
        {
            Id = Model.Id, FullName = FullName, FatherOrHusbandName = Father, Gender = Gender, DateOfBirth = Dob, Phone = Phone,
            WhatsApp = WhatsApp, Address = Address, Email = Email, Cnic = Cnic
        };
        if (string.IsNullOrWhiteSpace(p.FullName)) throw new BusinessRuleException("Patient name is required.");
        var dups = await _patients.FindDuplicatesAsync(p);
        if (dups.Count > 0)
        {
            var list = string.Join("\n", dups.Select(d => $"  - {d.FullName} ({d.PatientNo}), {d.Phone}, {d.Cnic}"));
            if (!Ui.Confirm($"Possible duplicate patient(s) found:\n{list}\n\nSave this as a separate patient anyway?", "Possible duplicate")) return;
        }
        Saved = await _patients.SaveAsync(p);
        Close(true);
    });
}

public sealed class DoctorEditorViewModel : DialogViewModel
{
    private readonly DoctorService _doctors;
    private readonly ICurrentUser _user;
    private readonly SettingsService _settings;
    public DoctorEditorViewModel(DoctorService doctors, ICurrentUser user, SettingsService settings) { _doctors = doctors; _user = user; _settings = settings; }

    public int Id { get; private set; }
    public Doctor? Saved { get; private set; }
    public string Heading => Id == 0 ? "New doctor" : "Edit doctor";
    public bool ShowCommission => _user.Has(Permissions.DoctorsManage) && _settings.GetBool(SettingKeys.CommissionEnabled);
    public CommissionType[] CommissionTypes { get; } = { CommissionType.None, CommissionType.Percentage, CommissionType.Fixed };

    private string _name = ""; public string Name { get => _name; set => Set(ref _name, value); }
    private string? _spec; public string? Specialization { get => _spec; set => Set(ref _spec, value); }
    private string? _clinic; public string? Clinic { get => _clinic; set => Set(ref _clinic, value); }
    private string? _contact; public string? Contact { get => _contact; set => Set(ref _contact, value); }
    private string? _address; public string? Address { get => _address; set => Set(ref _address, value); }
    private bool _active = true; public bool IsActive { get => _active; set => Set(ref _active, value); }
    private CommissionType _ct; public CommissionType CommissionType { get => _ct; set => Set(ref _ct, value); }
    private string _cv = "0"; public string CommissionValue { get => _cv; set => Set(ref _cv, value); }

    public async Task InitAsync(int? id, string? suggestedName)
    {
        if (id != null)
        {
            var d = await _doctors.GetAsync(id.Value) ?? throw new BusinessRuleException("Doctor not found.");
            Id = d.Id; Name = d.Name; Specialization = d.Specialization; Clinic = d.ClinicOrHospital; Contact = d.Contact;
            Address = d.Address; IsActive = d.IsActive; CommissionType = d.CommissionType; CommissionValue = d.CommissionValue.ToString(CultureInfo.InvariantCulture);
        }
        else Name = suggestedName ?? "";
        OnPropertyChanged(nameof(Heading));
    }

    public ICommand SaveCommand => new AsyncRelayCommand(async () =>
    {
        decimal.TryParse(CommissionValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var cv);
        Saved = await _doctors.SaveAsync(new Doctor
        {
            Id = Id, Name = Name, Specialization = Specialization, ClinicOrHospital = Clinic, Contact = Contact, Address = Address,
            IsActive = IsActive, CommissionType = CommissionType, CommissionValue = cv
        });
        Close(true);
    });
}

public sealed class RoleChoice : ObservableBase
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    private bool _sel; public bool IsSelected { get => _sel; set => Set(ref _sel, value); }
}

public sealed class UserEditorViewModel : DialogViewModel
{
    private readonly UserService _users;
    public UserEditorViewModel(UserService users) => _users = users;
    public int? Id { get; private set; }
    public string Heading => Id == null ? "New user" : "Edit user";
    public ObservableCollection<RoleChoice> Roles { get; } = new();
    private string _username = ""; public string Username { get => _username; set => Set(ref _username, value); }
    private string _full = ""; public string FullName { get => _full; set => Set(ref _full, value); }
    private string? _des; public string? Designation { get => _des; set => Set(ref _des, value); }
    private string? _sig; public string? SignatureText { get => _sig; set => Set(ref _sig, value); }
    private bool _active = true; public bool IsActive { get => _active; set => Set(ref _active, value); }
    private string? _pw; public string? NewPassword { get => _pw; set => Set(ref _pw, value); }
    public string PasswordLabel => Id == null ? "Temporary password (user must change it at first login)" : "Reset password (leave blank to keep current)";

    public async Task InitAsync(int? id)
    {
        Id = id;
        var roles = await _users.GetRolesAsync();
        UserEditDto? dto = id == null ? null : await _users.GetAsync(id.Value);
        foreach (var r in roles) Roles.Add(new RoleChoice { Id = r.Id, Name = r.Name, IsSelected = dto?.RoleIds.Contains(r.Id) ?? false });
        if (dto != null) { Username = dto.Username; FullName = dto.FullName; Designation = dto.Designation; SignatureText = dto.SignatureText; IsActive = dto.IsActive; }
        OnPropertyChanged(nameof(Heading)); OnPropertyChanged(nameof(PasswordLabel));
    }

    public ICommand SaveCommand => new AsyncRelayCommand(async () =>
    {
        await _users.SaveAsync(new UserEditDto
        {
            Id = Id, Username = Username, FullName = FullName, Designation = Designation, SignatureText = SignatureText,
            IsActive = IsActive, NewPassword = NewPassword, RoleIds = Roles.Where(r => r.IsSelected).Select(r => r.Id).ToList()
        });
        Close(true);
    });
}

public sealed class TestEditorViewModel : DialogViewModel
{
    private readonly CatalogService _catalog;
    private readonly ICurrentUser _user;
    public TestEditorViewModel(CatalogService catalog, ICurrentUser user) { _catalog = catalog; _user = user; }

    private int _id; private bool _isPanel;
    public string Heading => _id == 0 ? (_isPanel ? "New panel / package" : "New test") : (_isPanel ? "Edit panel / package" : "Edit test");
    public bool IsPanel => _isPanel;
    public bool IsNotPanel => !_isPanel;
    public bool CanEditPrice => _user.Has(Permissions.TestsChangePrice);
    public string? PriceNote => _priceIsSample ? "Current price is a SAMPLE value. Review and set the real price." : null;
    private bool _priceIsSample;

    public List<LookupItem> Categories { get; private set; } = new();
    public List<LookupItem> Specimens { get; private set; } = new();
    public List<LookupItem> Containers { get; private set; } = new();
    public List<LookupItem> Units { get; private set; } = new();
    public ResultType[] ResultTypes { get; } = Enum.GetValues<ResultType>();
    public List<GenderOption> GenderOptions { get; } = new() { new(null, "Any"), new(Gender.Male, "Male"), new(Gender.Female, "Female") };

    private string _code = ""; public string Code { get => _code; set => Set(ref _code, value); }
    private string _name = ""; public string Name { get => _name; set => Set(ref _name, value); }
    private int? _cat; public int? CategoryId { get => _cat; set => Set(ref _cat, value); }
    private int? _spec; public int? SpecimenTypeId { get => _spec; set => Set(ref _spec, value); }
    private int? _cont; public int? ContainerId { get => _cont; set => Set(ref _cont, value); }
    private string? _instr; public string? Instructions { get => _instr; set => Set(ref _instr, value); }
    private string _tat = "24"; public string Tat { get => _tat; set => Set(ref _tat, value); }
    private string _price = "0"; public string Price { get => _price; set => Set(ref _price, value); }
    private string? _method; public string? Method { get => _method; set => Set(ref _method, value); }
    private bool _active = true; public bool IsActive { get => _active; set => Set(ref _active, value); }

    public ObservableCollection<ParameterDto> Parameters { get; } = new();
    private readonly Dictionary<ParameterDto, ObservableCollection<ReferenceRangeDto>> _ranges = new();
    private ParameterDto? _selParam;
    public ParameterDto? SelectedParameter
    {
        get => _selParam;
        set { if (Set(ref _selParam, value)) Ranges = value != null && _ranges.TryGetValue(value, out var r) ? r : null; }
    }
    private ObservableCollection<ReferenceRangeDto>? _rangesView;
    public ObservableCollection<ReferenceRangeDto>? Ranges { get => _rangesView; private set => Set(ref _rangesView, value); }

    public ObservableCollection<LabTest> Components { get; } = new();
    public List<LabTest> AvailableTests { get; private set; } = new();
    private LabTest? _toAdd; public LabTest? ComponentToAdd { get => _toAdd; set => Set(ref _toAdd, value); }
    private LabTest? _selComp; public LabTest? SelectedComponent { get => _selComp; set => Set(ref _selComp, value); }

    public async Task InitAsync(int? id, bool isPanel)
    {
        Categories = (await _catalog.GetCategoriesAsync()).Select(c => new LookupItem(c.Id, c.Name)).ToList();
        Specimens = new[] { new LookupItem(null, "(none)") }.Concat((await _catalog.GetSpecimensAsync()).Select(c => new LookupItem(c.Id, c.Name))).ToList();
        Containers = new[] { new LookupItem(null, "(none)") }.Concat((await _catalog.GetContainersAsync()).Select(c => new LookupItem(c.Id, c.Name))).ToList();
        Units = new[] { new LookupItem(null, "") }.Concat((await _catalog.GetUnitsAsync()).Select(c => new LookupItem(c.Id, c.Symbol))).ToList();
        AvailableTests = await _catalog.SearchAsync(null, null, false, false);
        _isPanel = isPanel;
        if (id != null)
        {
            var t = await _catalog.GetAsync(id.Value);
            _id = t.Id; _isPanel = t.IsPanel; _priceIsSample = t.PriceIsSample;
            Code = t.Code; Name = t.Name; CategoryId = t.CategoryId; SpecimenTypeId = t.SpecimenTypeId; ContainerId = t.ContainerId;
            Instructions = t.Instructions; Tat = t.TurnaroundHours.ToString(); Price = t.Price.ToString("0.##", CultureInfo.InvariantCulture);
            Method = t.Method; IsActive = t.IsActive;
            foreach (var p in t.Parameters) { Parameters.Add(p); _ranges[p] = new ObservableCollection<ReferenceRangeDto>(p.Ranges); }
            foreach (var cid in t.ComponentTestIds)
            {
                var ct = AvailableTests.FirstOrDefault(x => x.Id == cid);
                if (ct != null) Components.Add(ct);
            }
        }
        else if (isPanel) CategoryId = Categories.FirstOrDefault(c => c.Name.StartsWith("Profiles"))?.Id;
        foreach (var n in new[] { nameof(Categories), nameof(Specimens), nameof(Containers), nameof(Units), nameof(AvailableTests), nameof(Heading), nameof(IsPanel), nameof(IsNotPanel), nameof(PriceNote) })
            OnPropertyChanged(n);
        if (Parameters.Count > 0) SelectedParameter = Parameters[0];
    }

    public ICommand AddParameterCommand => new RelayCommand(() =>
    {
        var p = new ParameterDto { Name = "New parameter", DisplayOrder = Parameters.Count + 1 };
        Parameters.Add(p); _ranges[p] = new ObservableCollection<ReferenceRangeDto>(); SelectedParameter = p;
    });
    public ICommand RemoveParameterCommand => new RelayCommand(() =>
    {
        if (SelectedParameter == null) return;
        if (!Ui.Confirm("Remove this parameter? Existing results are kept; the parameter is deactivated.")) return;
        _ranges.Remove(SelectedParameter); Parameters.Remove(SelectedParameter); SelectedParameter = Parameters.FirstOrDefault();
    });
    public ICommand AddRangeCommand => new RelayCommand(() => Ranges?.Add(new ReferenceRangeDto()));
    public ICommand RemoveRangeCommand => new RelayCommand(p => { if (p is ReferenceRangeDto r) Ranges?.Remove(r); });
    public ICommand AddComponentCommand => new RelayCommand(() =>
    {
        if (ComponentToAdd != null && Components.All(c => c.Id != ComponentToAdd.Id)) Components.Add(ComponentToAdd);
    });
    public ICommand RemoveComponentCommand => new RelayCommand(() => { if (SelectedComponent != null) Components.Remove(SelectedComponent); });

    public ICommand SaveCommand => new AsyncRelayCommand(async () =>
    {
        if (!decimal.TryParse(Price, NumberStyles.Number, CultureInfo.InvariantCulture, out var price)) throw new BusinessRuleException("Enter a valid price.");
        if (!int.TryParse(Tat, out var tat)) throw new BusinessRuleException("Turnaround time must be a whole number of hours.");
        foreach (var p in Parameters) p.Ranges = _ranges[p].ToList();
        var dto = new TestEditDto
        {
            Id = _id, Code = Code, Name = Name, CategoryId = CategoryId ?? 0, SpecimenTypeId = SpecimenTypeId, ContainerId = ContainerId,
            Instructions = Instructions, TurnaroundHours = tat, Price = price, PriceIsSample = _priceIsSample, Method = Method, IsPanel = _isPanel, IsActive = IsActive,
            Parameters = Parameters.ToList(), ComponentTestIds = Components.Select(c => c.Id).ToList()
        };
        await _catalog.SaveAsync(dto);
        Close(true);
    });
}

public sealed class OrderDetailViewModel : DialogViewModel
{
    private readonly OrderService _orders;
    private readonly SampleService _samples;
    private readonly ICurrentUser _user;
    private readonly IDialogService _dialogs;
    public OrderDetailViewModel(OrderService orders, SampleService samples, ICurrentUser user, IDialogService dialogs)
    { _orders = orders; _samples = samples; _user = user; _dialogs = dialogs; }

    private int _orderId;
    private LabOrder? _order;
    public LabOrder? Order { get => _order; private set { if (Set(ref _order, value)) OnPropertyChanged(string.Empty); } }
    public List<SampleRow> Samples { get; private set; } = new();
    public Payment? SelectedPayment { get; set; }
    public bool CanPay => _user.Has(Permissions.PaymentsReceive) && Order is { Status: not OrderStatus.Cancelled, Balance: > 0 };
    public bool CanRefund => _user.Has(Permissions.OrdersRefund) && Order is { PaidAmount: > 0 };
    public bool CanCancel => _user.Has(Permissions.OrdersCancel) && Order is { Status: not OrderStatus.Cancelled };
    public bool CanReport => _user.Has(Permissions.ReportsPrint) || _user.Has(Permissions.ResultsVerify);

    public async Task InitAsync(int orderId) { _orderId = orderId; await ReloadAsync(); }
    private async Task ReloadAsync()
    {
        Order = await _orders.GetDetailAsync(_orderId);
        Samples = await _samples.GetByOrderAsync(_orderId);
        OnPropertyChanged(nameof(Samples));
    }

    public ICommand PayCommand => new AsyncRelayCommand(async () =>
    {
        var req = _dialogs.AskPayment("Receive payment", Order!.Balance, false);
        if (req == null) return;
        var id = await _orders.AddPaymentAsync(_orderId, req.Amount, req.MethodId, req.Text);
        await ReloadAsync();
        if (Ui.Confirm("Payment recorded. Print receipt?")) _dialogs.PreviewReceipt(id);
    });
    public ICommand RefundCommand => new AsyncRelayCommand(async () =>
    {
        var req = _dialogs.AskPayment("Issue refund", Order!.PaidAmount, true);
        if (req == null) return;
        var id = await _orders.RefundAsync(_orderId, req.Amount, req.MethodId, req.Text ?? "");
        await ReloadAsync();
        _dialogs.PreviewReceipt(id);
    });
    public ICommand CancelOrderCommand => new AsyncRelayCommand(async () =>
    {
        var reason = _dialogs.Prompt("Cancel order", "Reason for cancellation");
        if (reason == null) return;
        if (!Ui.Confirm("Cancel this order? This cannot be undone. Any payment must be refunded separately.")) return;
        await _orders.CancelAsync(_orderId, reason);
        await ReloadAsync();
    });
    public ICommand ReceiptCommand => new RelayCommand(() =>
    {
        var p = SelectedPayment ?? Order?.Payments.OrderByDescending(x => x.PaidAt).FirstOrDefault();
        if (p == null) { Ui.Info("There are no payments on this order."); return; }
        _dialogs.PreviewReceipt(p.Id);
    });
    public ICommand LabelsCommand => new RelayCommand(() => _dialogs.PreviewLabels(Samples.Select(s => s.SampleId)));
    public ICommand ReportCommand => new RelayCommand(() => _dialogs.PreviewReport(_orderId));
    public ICommand CloseCommand => new RelayCommand(() => Close(true));
}

public sealed class PatientHistoryViewModel : DialogViewModel
{
    private readonly PatientService _patients;
    private readonly IDialogService _dialogs;
    private readonly Printing.DocumentFactory _docs;
    public PatientHistoryViewModel(PatientService patients, IDialogService dialogs, Printing.DocumentFactory docs) { _patients = patients; _dialogs = dialogs; _docs = docs; }

    public Patient? Patient { get; private set; }
    public List<LabOrder> Orders { get; private set; } = new();
    public LabOrder? Selected { get; set; }

    public async Task InitAsync(int patientId)
    {
        Patient = await _patients.GetAsync(patientId);
        Orders = await _patients.GetHistoryAsync(patientId);
        OnPropertyChanged(nameof(Patient)); OnPropertyChanged(nameof(Orders));
    }
    public ICommand OpenOrderCommand => new RelayCommand(() => { if (Selected != null) _dialogs.ShowOrder(Selected.Id); });
    public ICommand PrintCommand => new RelayCommand(() =>
        _dialogs.ShowDocument(() => _docs.BuildPatientHistory(Patient!, Orders), $"Patient history {Patient!.PatientNo}"));
}
