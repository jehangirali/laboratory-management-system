namespace LaboratoryManagement.Domain;

/// <summary>Stable permission codes. Seeded into the Permissions table; enforced in application services.</summary>
public static class Permissions
{
    public const string DashboardView = "Dashboard.View";
    public const string PatientsView = "Patients.View";
    public const string PatientsManage = "Patients.Manage";
    public const string DoctorsView = "Doctors.View";
    public const string DoctorsManage = "Doctors.Manage";
    public const string TestsView = "Tests.View";
    public const string TestsManage = "Tests.Manage";
    public const string TestsChangePrice = "Tests.ChangePrice";
    public const string OrdersView = "Orders.View";
    public const string OrdersCreate = "Orders.Create";
    public const string OrdersDiscount = "Orders.Discount";
    public const string OrdersCancel = "Orders.Cancel";
    public const string OrdersRefund = "Orders.Refund";
    public const string PaymentsReceive = "Payments.Receive";
    public const string SamplesManage = "Samples.Manage";
    public const string ResultsEnter = "Results.Enter";
    public const string ResultsVerify = "Results.Verify";
    public const string ReportsPrint = "Reports.Print";
    public const string ReportsAmend = "Reports.Amend";
    public const string FinanceReports = "Finance.Reports";
    public const string CommissionsView = "Commissions.View";
    public const string UsersManage = "Users.Manage";
    public const string SettingsManage = "Settings.Manage";
    public const string BackupManage = "Backup.Manage";

    public static readonly IReadOnlyList<(string Code, string Group, string Description)> All = new[]
    {
        (DashboardView, "Dashboard", "View the dashboard"),
        (PatientsView, "Patients", "View and search patients"),
        (PatientsManage, "Patients", "Create and edit patients"),
        (DoctorsView, "Doctors", "View referring doctors"),
        (DoctorsManage, "Doctors", "Create and edit referring doctors"),
        (TestsView, "Test catalog", "View test catalog"),
        (TestsManage, "Test catalog", "Create/edit tests, parameters, ranges and panels"),
        (TestsChangePrice, "Test catalog", "Change test prices"),
        (OrdersView, "Orders", "View orders and payments"),
        (OrdersCreate, "Orders", "Register new orders"),
        (OrdersDiscount, "Orders", "Apply discounts"),
        (OrdersCancel, "Orders", "Cancel orders"),
        (OrdersRefund, "Orders", "Issue refunds"),
        (PaymentsReceive, "Orders", "Receive payments"),
        (SamplesManage, "Samples", "Collect, receive, reject and recollect samples"),
        (ResultsEnter, "Results", "Enter and edit results before verification"),
        (ResultsVerify, "Results", "Verify (approve) results and reports"),
        (ReportsPrint, "Reports", "Preview and print patient reports"),
        (ReportsAmend, "Reports", "Start report amendments"),
        (FinanceReports, "Reports", "Financial and operational reports, revenue dashboard figures"),
        (CommissionsView, "Reports", "View doctor commission reports"),
        (UsersManage, "Administration", "Manage users, roles and permissions"),
        (SettingsManage, "Administration", "Manage laboratory settings"),
        (BackupManage, "Administration", "Backup and restore database"),
    };
}

public static class SystemRoles
{
    public const string Administrator = "Administrator";
    public const string Receptionist = "Receptionist";
    public const string LabTechnician = "Lab Technician";
    public const string Pathologist = "Pathologist / Report Verifier";
    public const string Accountant = "Accountant";
}
