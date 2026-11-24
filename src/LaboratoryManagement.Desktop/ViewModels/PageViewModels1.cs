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

public interface INavigator
{
    void Go<T>(Action<T>? init = null) where T : ViewModelBase;
}

public record ChartBar(string Label, string ValueText, double Width);

public sealed class DashboardViewModel : ViewModelBase
{
    private readonly DashboardService _svc;
    public DashboardViewModel(DashboardService svc) => _svc = svc;

    public ObservableCollection<DashboardCard> Cards { get; } = new();
    public ObservableCollection<ChartBar> Registrations { get; } = new();
    public ObservableCollection<ChartBar> Revenue { get; } = new();
    public ObservableCollection<ChartBar> TopTests { get; } = new();
    public ObservableCollection<ChartBar> Categories { get; } = new();
    public ICommand RefreshCommand => new AsyncRelayCommand(LoadAsync);

    private static void Fill(ObservableCollection<ChartBar> target, List<ChartPoint> pts, bool money = false)
    {
        target.Clear();
        var max = pts.Count == 0 ? 0 : pts.Max(p => Math.Abs(p.Value));
        foreach (var p in pts)
            target.Add(new ChartBar(p.Label, money ? p.Value.ToString("N0") : p.Value.ToString("0"), max == 0 ? 0 : (double)(Math.Abs(p.Value) / max) * 200));
    }

    public override async Task LoadAsync()
    {
        await RunAsync(async () =>
        {
            var d = await _svc.LoadAsync();
            Cards.Clear(); foreach (var c in d.Cards) Cards.Add(c);
            Fill(Registrations, d.DailyRegistrations); Fill(Revenue, d.MonthlyRevenue, true);
            Fill(TopTests, d.TopTests); Fill(Categories, d.CategoryDistribution);
        });
    }
}

public sealed class PatientsViewModel : ViewModelBase
{
    private readonly PatientService _svc; private readonly IDialogService _dialogs; private readonly INavigator _nav; private readonly ICurrentUser _user;
    public PatientsViewModel(PatientService svc, IDialogService dialogs, INavigator nav, ICurrentUser user) { _svc = svc; _dialogs = dialogs; _nav = nav; _user = user; }

    public bool CanManage => _user.Has(Permissions.PatientsManage);
    public bool CanOrder => _user.Has(Permissions.OrdersCreate);
    public ObservableCollection<Patient> Patients { get; } = new();
    public Patient? Selected { get; set; }
    private string _search = ""; private int _ver;
    public string SearchText { get => _search; set { if (Set(ref _search, value)) _ = SearchAsync(); } }

    public override Task LoadAsync() => SearchAsync();
    private async Task SearchAsync()
    {
        var v = ++_ver;
        try
        {
            await Task.Delay(180);
            if (v != _ver) return;
            var list = await _svc.SearchAsync(SearchText);
            if (v != _ver) return;
            Patients.Clear(); foreach (var p in list) Patients.Add(p);
        }
        catch (Exception ex) { Ui.HandleException(ex); }
    }

    public ICommand NewCommand => new RelayCommand(() => { if (_dialogs.EditPatient(null) != null) _ = SearchAsync(); });
    public ICommand EditCommand => new RelayCommand(() => { if (Selected != null && _dialogs.EditPatient(Selected.Id) != null) _ = SearchAsync(); });
    public ICommand HistoryCommand => new RelayCommand(() => { if (Selected != null) _dialogs.ShowPatientHistory(Selected.Id); });
    public ICommand OrderCommand => new RelayCommand(() =>
    {
        if (Selected == null) return;
        var p = Selected; _nav.Go<NewOrderViewModel>(vm => vm.SelectedPatient = p);
    });
}

public sealed class DoctorsViewModel : ViewModelBase
{
    private readonly DoctorService _svc; private readonly IDialogService _dialogs; private readonly ICurrentUser _user;
    public DoctorsViewModel(DoctorService svc, IDialogService dialogs, ICurrentUser user) { _svc = svc; _dialogs = dialogs; _user = user; }
    public bool CanManage => _user.Has(Permissions.DoctorsManage);
    public ObservableCollection<Doctor> Doctors { get; } = new();
    public Doctor? Selected { get; set; }
    private string _search = ""; private int _ver;
    public string SearchText { get => _search; set { if (Set(ref _search, value)) _ = SearchAsync(); } }
    public override Task LoadAsync() => SearchAsync();
    private async Task SearchAsync()
    {
        var v = ++_ver;
        try
        {
            await Task.Delay(180); if (v != _ver) return;
            var list = await _svc.SearchAsync(SearchText, true);
            if (v != _ver) return;
            Doctors.Clear(); foreach (var d in list) Doctors.Add(d);
        }
        catch (Exception ex) { Ui.HandleException(ex); }
    }
    public ICommand NewCommand => new RelayCommand(() => { if (_dialogs.EditDoctor(null) != null) _ = SearchAsync(); });
    public ICommand EditCommand => new RelayCommand(() => { if (Selected != null && _dialogs.EditDoctor(Selected.Id) != null) _ = SearchAsync(); });
}

public sealed class TestsViewModel : ViewModelBase
{
    private readonly CatalogService _svc; private readonly IDialogService _dialogs; private readonly ICurrentUser _user;
    public TestsViewModel(CatalogService svc, IDialogService dialogs, ICurrentUser user) { _svc = svc; _dialogs = dialogs; _user = user; }

    public bool CanManage => _user.Has(Permissions.TestsManage);
    public ObservableCollection<LabTest> Tests { get; } = new();
    public List<LookupItem> Categories { get; private set; } = new();
    public LabTest? Selected { get; set; }
    private string _search = ""; private int _ver;
    public string SearchText { get => _search; set { if (Set(ref _search, value)) _ = SearchAsync(); } }
    private int? _cat; public int? CategoryId { get => _cat; set { if (Set(ref _cat, value)) _ = SearchAsync(); } }
    private bool _inactive; public bool ShowInactive { get => _inactive; set { if (Set(ref _inactive, value)) _ = SearchAsync(); } }
    private int _unpriced; public int Unpriced { get => _unpriced; set => Set(ref _unpriced, value); }

    public override async Task LoadAsync()
    {
        Categories = new[] { new LookupItem(null, "All categories") }.Concat((await _svc.GetCategoriesAsync()).Select(c => new LookupItem(c.Id, c.Name))).ToList();
        OnPropertyChanged(nameof(Categories));
        await SearchAsync();
    }

    private async Task SearchAsync()
    {
        var v = ++_ver;
        try
        {
            await Task.Delay(150); if (v != _ver) return;
            var list = await _svc.SearchAsync(SearchText, CategoryId, ShowInactive);
            if (v != _ver) return;
            Tests.Clear(); foreach (var t in list) Tests.Add(t);
            Unpriced = list.Count(t => t.Price <= 0 || t.PriceIsSample);
        }
        catch (Exception ex) { Ui.HandleException(ex); }
    }

    public ICommand NewTestCommand => new RelayCommand(() => { if (_dialogs.EditTest(null, false)) _ = SearchAsync(); });
    public ICommand NewPanelCommand => new RelayCommand(() => { if (_dialogs.EditTest(null, true)) _ = SearchAsync(); });
    public ICommand EditCommand => new RelayCommand(() => { if (Selected != null && _dialogs.EditTest(Selected.Id, Selected.IsPanel)) _ = SearchAsync(); });
}

public sealed class OrderLineVm
{
    public LabTest Test { get; init; } = null!;
    public string Name => Test.Name;
    public string Code => Test.Code;
    public decimal Price => Test.Price;
    public string Flag => Test.Price <= 0 ? "Price not set" : Test.PriceIsSample ? "SAMPLE price" : "";
    public string Kind => Test.IsPanel ? "Panel" : "Test";
}

public sealed class NewOrderViewModel : ViewModelBase
{
    private readonly PatientService _patients; private readonly DoctorService _doctors; private readonly CatalogService _catalog;
    private readonly OrderService _orders; private readonly IDialogService _dialogs; private readonly ICurrentUser _user; private readonly SampleService _samples;
    private readonly INavigator _nav;

    public NewOrderViewModel(PatientService patients, DoctorService doctors, CatalogService catalog, OrderService orders,
        IDialogService dialogs, ICurrentUser user, SampleService samples, INavigator nav)
    { _patients = patients; _doctors = doctors; _catalog = catalog; _orders = orders; _dialogs = dialogs; _user = user; _samples = samples; _nav = nav; }

    public bool CanDiscount => _user.Has(Permissions.OrdersDiscount);
    public bool CanReceivePayment => _user.Has(Permissions.PaymentsReceive);

    // Patient lookup
    private string _patientSearch = ""; private int _pver;
    public string PatientSearch { get => _patientSearch; set { if (Set(ref _patientSearch, value)) _ = SearchPatientsAsync(); } }
    public ObservableCollection<Patient> PatientResults { get; } = new();
    private Patient? _patient;
    public Patient? SelectedPatient { get => _patient; set { if (Set(ref _patient, value)) OnPropertyChanged(nameof(PatientInfo)); } }
    public string PatientInfo => SelectedPatient == null ? "No patient selected" : $"{SelectedPatient.FullName}  |  {SelectedPatient.PatientNo}  |  {SelectedPatient.Gender}, {SelectedPatient.AgeText}  |  {SelectedPatient.Phone}";
    private async Task SearchPatientsAsync()
    {
        var v = ++_pver;
        try
        {
            await Task.Delay(150); if (v != _pver) return;
            var list = await _patients.LookupAsync(PatientSearch);
            if (v != _pver) return;
            PatientResults.Clear(); foreach (var p in list) PatientResults.Add(p);
        }
        catch (Exception ex) { Ui.HandleException(ex); }
    }
    public ICommand AddPatientCommand => new RelayCommand(text =>
    {
        // The order form (selected tests, amounts) stays untouched while the dialog is open.
        var p = _dialogs.EditPatient(null, text as string);
        if (p != null) SelectedPatient = p;
    });

    // Doctor lookup with "add new"
    private string _doctorSearch = ""; private int _dver;
    public string DoctorSearch { get => _doctorSearch; set { if (Set(ref _doctorSearch, value)) _ = SearchDoctorsAsync(); } }
    public ObservableCollection<Doctor> DoctorResults { get; } = new();
    private Doctor? _doctor; public Doctor? SelectedDoctor { get => _doctor; set => Set(ref _doctor, value); }
    private async Task SearchDoctorsAsync()
    {
        var v = ++_dver;
        try
        {
            await Task.Delay(150); if (v != _dver) return;
            var list = await _doctors.SearchAsync(DoctorSearch, false, 15);
            if (v != _dver) return;
            DoctorResults.Clear(); foreach (var d in list) DoctorResults.Add(d);
        }
        catch (Exception ex) { Ui.HandleException(ex); }
    }
    public ICommand AddDoctorCommand => new RelayCommand(text =>
    {
        var d = _dialogs.EditDoctor(null, text as string);
        if (d != null) SelectedDoctor = d; // auto-select the newly created doctor
    });

    // Test picker
    public ObservableCollection<LabTest> AvailableTests { get; } = new();
    public LabTest? SelectedAvailable { get; set; }
    private string _testSearch = ""; private int _tver;
    public string TestSearch { get => _testSearch; set { if (Set(ref _testSearch, value)) _ = SearchTestsAsync(); } }
    private async Task SearchTestsAsync()
    {
        var v = ++_tver;
        try
        {
            await Task.Delay(150); if (v != _tver) return;
            var list = await _catalog.SearchAsync(TestSearch, null, false, null, 80);
            if (v != _tver) return;
            AvailableTests.Clear(); foreach (var t in list) AvailableTests.Add(t);
        }
        catch (Exception ex) { Ui.HandleException(ex); }
    }

    public ObservableCollection<OrderLineVm> Lines { get; } = new();
    public OrderLineVm? SelectedLine { get; set; }
    public ICommand AddTestCommand => new RelayCommand(p =>
    {
        var t = (p as LabTest) ?? SelectedAvailable;
        if (t == null) return;
        if (Lines.Any(l => l.Test.Id == t.Id)) return;
        Lines.Add(new OrderLineVm { Test = t });
        Recalc();
    });
    public ICommand RemoveLineCommand => new RelayCommand(() => { if (SelectedLine != null) { Lines.Remove(SelectedLine); Recalc(); } });

    // Billing
    private string _discount = "0"; public string DiscountText { get => _discount; set { if (Set(ref _discount, value)) Recalc(); } }
    private string _payment = "0"; public string PaymentText { get => _payment; set { if (Set(ref _payment, value)) Recalc(); } }
    public List<PaymentMethod> Methods { get; private set; } = new();
    private PaymentMethod? _method; public PaymentMethod? Method { get => _method; set => Set(ref _method, value); }
    private string? _reference; public string? PaymentReference { get => _reference; set => Set(ref _reference, value); }
    private string? _notes; public string? Notes { get => _notes; set => Set(ref _notes, value); }
    private decimal _sub, _net, _bal;
    public decimal Subtotal { get => _sub; private set => Set(ref _sub, value); }
    public decimal Net { get => _net; private set => Set(ref _net, value); }
    public decimal Balance { get => _bal; private set => Set(ref _bal, value); }
    public string Warning => Lines.Any(l => l.Test.Price <= 0) ? "Some selected tests have no price set. An administrator must set prices before billing."
        : Lines.Any(l => l.Test.PriceIsSample) ? "Some prices are SAMPLE values (demo data), not official prices." : "";

    private static decimal Parse(string s) => decimal.TryParse(s, NumberStyles.Number, CultureInfo.CurrentCulture, out var d) ? d : 0;
    private void Recalc()
    {
        Subtotal = Lines.Sum(l => l.Price);
        Net = Math.Max(0, Subtotal - Parse(DiscountText));
        Balance = Net - Parse(PaymentText);
        OnPropertyChanged(nameof(Warning));
    }

    public override async Task LoadAsync()
    {
        Methods = await _catalog.GetPaymentMethodsAsync();
        Method = Methods.FirstOrDefault(m => m.Code == "CASH") ?? Methods.FirstOrDefault();
        OnPropertyChanged(nameof(Methods));
        await SearchPatientsAsync(); await SearchDoctorsAsync(); await SearchTestsAsync();
    }

    public ICommand PayFullCommand => new RelayCommand(() => PaymentText = Net.ToString("0.##"));

    public ICommand SaveCommand => new AsyncRelayCommand(async () =>
    {
        if (SelectedPatient == null) throw new BusinessRuleException("Select a patient (or add a new one).");
        var req = new CreateOrderRequest
        {
            PatientId = SelectedPatient.Id, DoctorId = SelectedDoctor?.Id, TestIds = Lines.Select(l => l.Test.Id).ToList(),
            Discount = Parse(DiscountText), InitialPayment = Parse(PaymentText), PaymentMethodId = Method?.Id,
            PaymentReference = PaymentReference, Notes = Notes
        };
        var res = await _orders.CreateAsync(req);
        Ui.Info($"Order {res.OrderNo} created.");
        if (res.PaymentId != null && Ui.Confirm("Print receipt now?")) _dialogs.PreviewReceipt(res.PaymentId.Value);
        if (_user.Has(Permissions.SamplesManage))
        {
            var samples = await _samples.GetByOrderAsync(res.OrderId);
            if (samples.Count > 0 && Ui.Confirm($"Print {samples.Count} sample label(s) now?")) _dialogs.PreviewLabels(samples.Select(s => s.SampleId));
        }
        Reset();
    });

    public ICommand ClearCommand => new RelayCommand(Reset);

    private void Reset()
    {
        SelectedPatient = null; SelectedDoctor = null; Lines.Clear(); DiscountText = "0"; PaymentText = "0"; PaymentReference = null; Notes = null; Recalc();
    }
}

public sealed class OrdersViewModel : ViewModelBase
{
    private readonly OrderService _svc; private readonly IDialogService _dialogs; private readonly INavigator _nav; private readonly ICurrentUser _user;
    public OrdersViewModel(OrderService svc, IDialogService dialogs, INavigator nav, ICurrentUser user) { _svc = svc; _dialogs = dialogs; _nav = nav; _user = user; }

    public bool CanCreate => _user.Has(Permissions.OrdersCreate);
    public DateTime From { get; set; } = DateTime.Today;
    public DateTime To { get; set; } = DateTime.Today;
    public string? Text { get; set; }
    public string[] StatusOptions { get; } = { "All", "Registered", "InProgress", "Completed", "Cancelled" };
    public string StatusFilter { get; set; } = "All";
    public bool OnlyBalance { get; set; }
    public ObservableCollection<LabOrder> Orders { get; } = new();
    public LabOrder? Selected { get; set; }
    public override Task LoadAsync() => SearchAsync();

    private async Task SearchAsync()
    {
        var f = new OrderFilter
        {
            FromLocal = From, ToLocal = To, Text = Text, OnlyWithBalance = OnlyBalance,
            Status = Enum.TryParse<OrderStatus>(StatusFilter, out var s) ? s : null
        };
        var list = await _svc.SearchAsync(f);
        Orders.Clear(); foreach (var o in list) Orders.Add(o);
    }
    public ICommand SearchCommand => new AsyncRelayCommand(SearchAsync);
    public ICommand OpenCommand => new RelayCommand(() => { if (Selected != null) { _dialogs.ShowOrder(Selected.Id); _ = SearchAsync(); } });
    public ICommand NewCommand => new RelayCommand(() => _nav.Go<NewOrderViewModel>());
}
