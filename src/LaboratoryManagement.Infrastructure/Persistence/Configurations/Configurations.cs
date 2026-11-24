using LaboratoryManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LaboratoryManagement.Infrastructure.Persistence.Configurations;

// ---- Identity ----
public class PermissionConfig : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> b)
    {
        b.HasIndex(x => x.Code).IsUnique();
        b.Property(x => x.Code).IsRequired().HasMaxLength(100);
        b.Property(x => x.Group).HasMaxLength(100);
        b.Property(x => x.Description).HasMaxLength(300);
    }
}

public class RoleConfig : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> b)
    {
        b.HasIndex(x => x.Name).IsUnique();
        b.Property(x => x.Name).IsRequired().HasMaxLength(100);
        b.Property(x => x.Description).HasMaxLength(300);
    }
}

public class RolePermissionConfig : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> b)
    {
        b.HasKey(x => new { x.RoleId, x.PermissionId });
        b.HasOne(x => x.Role).WithMany(r => r.RolePermissions).HasForeignKey(x => x.RoleId);
        b.HasOne(x => x.Permission).WithMany().HasForeignKey(x => x.PermissionId);
    }
}

public class UserConfig : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.HasIndex(x => x.Username).IsUnique();
        b.Property(x => x.Username).IsRequired().HasMaxLength(100);
        b.Property(x => x.FullName).IsRequired().HasMaxLength(200);
        b.Property(x => x.PasswordHash).IsRequired().HasMaxLength(500);
        b.Property(x => x.Designation).HasMaxLength(200);
        b.Property(x => x.SignatureText).HasMaxLength(300);
    }
}

public class UserRoleConfig : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> b)
    {
        b.HasKey(x => new { x.UserId, x.RoleId });
        b.HasOne(x => x.User).WithMany(u => u.UserRoles).HasForeignKey(x => x.UserId);
        b.HasOne(x => x.Role).WithMany(r => r.UserRoles).HasForeignKey(x => x.RoleId);
    }
}

public class LoginHistoryConfig : IEntityTypeConfiguration<LoginHistory>
{
    public void Configure(EntityTypeBuilder<LoginHistory> b)
    {
        b.HasIndex(x => x.At);
        b.Property(x => x.Username).HasMaxLength(100);
        b.Property(x => x.Event).HasMaxLength(50);
        b.Property(x => x.Details).HasMaxLength(500);
    }
}

public class AuditLogConfig : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.HasIndex(x => x.At);
        b.HasIndex(x => new { x.EntityType, x.EntityId });
        b.Property(x => x.Username).HasMaxLength(100);
        b.Property(x => x.Action).HasMaxLength(200);
        b.Property(x => x.EntityType).HasMaxLength(100);
        b.Property(x => x.EntityId).HasMaxLength(100);
        b.Property(x => x.Details).HasMaxLength(4000);
    }
}

// ---- Master data ----
public class AppSettingConfig : IEntityTypeConfiguration<AppSetting>
{
    public void Configure(EntityTypeBuilder<AppSetting> b)
    {
        b.HasKey(x => x.Key);
        b.Property(x => x.Key).HasMaxLength(100);
        b.Property(x => x.Value).HasMaxLength(2000);
        b.Property(x => x.Description).HasMaxLength(300);
        b.Property(x => x.Group).HasMaxLength(50);
    }
}

public class NumberSequenceConfig : IEntityTypeConfiguration<NumberSequence>
{
    public void Configure(EntityTypeBuilder<NumberSequence> b)
    {
        b.HasKey(x => x.Key);
        b.Property(x => x.Key).HasMaxLength(100);
        b.Property(x => x.RowVersion).IsRowVersion();
    }
}

public class TestCategoryConfig : IEntityTypeConfiguration<TestCategory>
{
    public void Configure(EntityTypeBuilder<TestCategory> b) { b.HasIndex(x => x.Code).IsUnique(); b.Property(x => x.Code).HasMaxLength(30); b.Property(x => x.Name).HasMaxLength(150); }
}
public class SpecimenTypeConfig : IEntityTypeConfiguration<SpecimenType>
{
    public void Configure(EntityTypeBuilder<SpecimenType> b) { b.HasIndex(x => x.Code).IsUnique(); b.Property(x => x.Code).HasMaxLength(30); b.Property(x => x.Name).HasMaxLength(100); }
}
public class SampleContainerConfig : IEntityTypeConfiguration<SampleContainer>
{
    public void Configure(EntityTypeBuilder<SampleContainer> b) { b.HasIndex(x => x.Code).IsUnique(); b.Property(x => x.Code).HasMaxLength(30); b.Property(x => x.Name).HasMaxLength(100); }
}
public class MeasurementUnitConfig : IEntityTypeConfiguration<MeasurementUnit>
{
    public void Configure(EntityTypeBuilder<MeasurementUnit> b) { b.HasIndex(x => x.Code).IsUnique(); b.Property(x => x.Code).HasMaxLength(50); b.Property(x => x.Symbol).HasMaxLength(50); }
}
public class PaymentMethodConfig : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> b) { b.HasIndex(x => x.Code).IsUnique(); b.Property(x => x.Code).HasMaxLength(30); b.Property(x => x.Name).HasMaxLength(100); }
}

public class PatientConfig : IEntityTypeConfiguration<Patient>
{
    public void Configure(EntityTypeBuilder<Patient> b)
    {
        b.HasIndex(x => x.PatientNo).IsUnique();
        b.HasIndex(x => x.FullName);
        b.HasIndex(x => x.Phone);
        b.HasIndex(x => x.Cnic);
        b.Property(x => x.PatientNo).IsRequired().HasMaxLength(30);
        b.Property(x => x.FullName).IsRequired().HasMaxLength(200);
        b.Property(x => x.FatherOrHusbandName).HasMaxLength(200);
        b.Property(x => x.DateOfBirth).HasColumnType("date");
        b.Property(x => x.Phone).HasMaxLength(30);
        b.Property(x => x.WhatsApp).HasMaxLength(30);
        b.Property(x => x.Address).HasMaxLength(500);
        b.Property(x => x.Email).HasMaxLength(200);
        b.Property(x => x.Cnic).HasMaxLength(20);
        b.Property(x => x.RowVersion).IsRowVersion();
    }
}

public class DoctorConfig : IEntityTypeConfiguration<Doctor>
{
    public void Configure(EntityTypeBuilder<Doctor> b)
    {
        b.HasIndex(x => x.DoctorNo).IsUnique();
        b.HasIndex(x => x.Name);
        b.Property(x => x.DoctorNo).IsRequired().HasMaxLength(30);
        b.Property(x => x.Name).IsRequired().HasMaxLength(200);
        b.Property(x => x.Specialization).HasMaxLength(150);
        b.Property(x => x.ClinicOrHospital).HasMaxLength(200);
        b.Property(x => x.Contact).HasMaxLength(50);
        b.Property(x => x.Address).HasMaxLength(500);
    }
}

// ---- Catalog ----
public class LabTestConfig : IEntityTypeConfiguration<LabTest>
{
    public void Configure(EntityTypeBuilder<LabTest> b)
    {
        b.HasIndex(x => x.Code).IsUnique();
        b.HasIndex(x => x.Name);
        b.Property(x => x.Code).IsRequired().HasMaxLength(30);
        b.Property(x => x.Name).IsRequired().HasMaxLength(200);
        b.Property(x => x.Instructions).HasMaxLength(1000);
        b.Property(x => x.Method).HasMaxLength(150);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId);
        b.HasOne(x => x.SpecimenType).WithMany().HasForeignKey(x => x.SpecimenTypeId);
        b.HasOne(x => x.Container).WithMany().HasForeignKey(x => x.ContainerId);
    }
}

public class PanelItemConfig : IEntityTypeConfiguration<PanelItem>
{
    public void Configure(EntityTypeBuilder<PanelItem> b)
    {
        b.HasIndex(x => new { x.PanelTestId, x.ComponentTestId }).IsUnique();
        b.HasOne(x => x.PanelTest).WithMany(t => t.PanelItems).HasForeignKey(x => x.PanelTestId);
        b.HasOne(x => x.ComponentTest).WithMany().HasForeignKey(x => x.ComponentTestId);
    }
}

public class TestParameterConfig : IEntityTypeConfiguration<TestParameter>
{
    public void Configure(EntityTypeBuilder<TestParameter> b)
    {
        b.HasIndex(x => new { x.LabTestId, x.DisplayOrder });
        b.Property(x => x.Name).IsRequired().HasMaxLength(200);
        b.HasOne(x => x.LabTest).WithMany(t => t.Parameters).HasForeignKey(x => x.LabTestId);
        b.HasOne(x => x.Unit).WithMany().HasForeignKey(x => x.UnitId);
    }
}

public class ReferenceRangeConfig : IEntityTypeConfiguration<ReferenceRange>
{
    public void Configure(EntityTypeBuilder<ReferenceRange> b)
    {
        b.Property(x => x.MinAgeYears).HasPrecision(8, 3);
        b.Property(x => x.MaxAgeYears).HasPrecision(8, 3);
        b.Property(x => x.Low).HasPrecision(18, 4);
        b.Property(x => x.High).HasPrecision(18, 4);
        b.Property(x => x.CriticalLow).HasPrecision(18, 4);
        b.Property(x => x.CriticalHigh).HasPrecision(18, 4);
        b.Property(x => x.NormalText).HasMaxLength(100);
        b.Property(x => x.DisplayText).HasMaxLength(200);
        b.HasOne(x => x.TestParameter).WithMany(p => p.ReferenceRanges).HasForeignKey(x => x.TestParameterId);
    }
}

// ---- Orders ----
public class LabOrderConfig : IEntityTypeConfiguration<LabOrder>
{
    public void Configure(EntityTypeBuilder<LabOrder> b)
    {
        b.ToTable("Orders");
        b.HasIndex(x => x.OrderNo).IsUnique();
        b.HasIndex(x => x.OrderedAt);
        b.HasIndex(x => x.PatientId);
        b.HasIndex(x => x.Status);
        b.Property(x => x.OrderNo).IsRequired().HasMaxLength(40);
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.Property(x => x.CancelReason).HasMaxLength(500);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasOne(x => x.Patient).WithMany().HasForeignKey(x => x.PatientId);
        b.HasOne(x => x.Doctor).WithMany().HasForeignKey(x => x.DoctorId);
        b.HasOne(x => x.Report).WithOne(r => r.Order).HasForeignKey<Report>(r => r.OrderId);
    }
}

public class OrderLineConfig : IEntityTypeConfiguration<OrderLine>
{
    public void Configure(EntityTypeBuilder<OrderLine> b)
    {
        b.HasOne(x => x.Order).WithMany(o => o.Lines).HasForeignKey(x => x.OrderId);
        b.HasOne(x => x.LabTest).WithMany().HasForeignKey(x => x.LabTestId);
    }
}

public class OrderTestConfig : IEntityTypeConfiguration<OrderTest>
{
    public void Configure(EntityTypeBuilder<OrderTest> b)
    {
        b.HasIndex(x => x.Status);
        b.HasOne(x => x.Order).WithMany(o => o.Tests).HasForeignKey(x => x.OrderId);
        b.HasOne(x => x.OrderLine).WithMany().HasForeignKey(x => x.OrderLineId);
        b.HasOne(x => x.LabTest).WithMany().HasForeignKey(x => x.LabTestId);
        b.HasOne(x => x.Sample).WithMany(s => s.Tests).HasForeignKey(x => x.SampleId);
    }
}

public class PaymentConfig : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> b)
    {
        b.HasIndex(x => x.ReceiptNo).IsUnique();
        b.HasIndex(x => x.PaidAt);
        b.Property(x => x.ReceiptNo).IsRequired().HasMaxLength(40);
        b.Property(x => x.Reference).HasMaxLength(200);
        b.Property(x => x.Reason).HasMaxLength(500);
        b.HasOne(x => x.Order).WithMany(o => o.Payments).HasForeignKey(x => x.OrderId);
        b.HasOne(x => x.PaymentMethod).WithMany().HasForeignKey(x => x.PaymentMethodId);
    }
}

public class SampleConfig : IEntityTypeConfiguration<Sample>
{
    public void Configure(EntityTypeBuilder<Sample> b)
    {
        b.HasIndex(x => x.Barcode).IsUnique();
        b.HasIndex(x => x.Status);
        b.Property(x => x.Barcode).IsRequired().HasMaxLength(40);
        b.Property(x => x.RejectionReason).HasMaxLength(500);
        b.HasOne(x => x.Order).WithMany(o => o.Samples).HasForeignKey(x => x.OrderId);
        b.HasOne(x => x.SpecimenType).WithMany().HasForeignKey(x => x.SpecimenTypeId);
        b.HasOne(x => x.Container).WithMany().HasForeignKey(x => x.ContainerId);
        b.HasOne(x => x.RecollectionOfSample).WithMany().HasForeignKey(x => x.RecollectionOfSampleId);
    }
}

public class ResultValueConfig : IEntityTypeConfiguration<ResultValue>
{
    public void Configure(EntityTypeBuilder<ResultValue> b)
    {
        b.HasIndex(x => new { x.OrderTestId, x.TestParameterId }).IsUnique();
        b.Property(x => x.Value).HasMaxLength(1000);
        b.Property(x => x.Unit).HasMaxLength(50);
        b.Property(x => x.ReferenceText).HasMaxLength(300);
        b.Property(x => x.Comment).HasMaxLength(1000);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasOne(x => x.OrderTest).WithMany(t => t.Results).HasForeignKey(x => x.OrderTestId);
        b.HasOne(x => x.TestParameter).WithMany().HasForeignKey(x => x.TestParameterId);
    }
}

public class ResultRevisionConfig : IEntityTypeConfiguration<ResultRevision>
{
    public void Configure(EntityTypeBuilder<ResultRevision> b)
    {
        b.Property(x => x.OldValue).HasMaxLength(1000);
        b.Property(x => x.NewValue).HasMaxLength(1000);
        b.Property(x => x.ChangedByName).HasMaxLength(200);
        b.Property(x => x.Reason).HasMaxLength(500);
        b.HasOne(x => x.ResultValue).WithMany(v => v.Revisions).HasForeignKey(x => x.ResultValueId);
    }
}

public class ReportConfig : IEntityTypeConfiguration<Report>
{
    public void Configure(EntityTypeBuilder<Report> b)
    {
        b.HasIndex(x => x.OrderId).IsUnique();
        b.HasIndex(x => x.Status);
        b.Property(x => x.AmendmentReason).HasMaxLength(500);
        b.Property(x => x.VerifierName).HasMaxLength(200);
        b.Property(x => x.VerifierDesignation).HasMaxLength(200);
        b.Property(x => x.VerifierRoles).HasMaxLength(300);
        b.Property(x => x.SignatureText).HasMaxLength(300);
    }
}

public class ReportVersionConfig : IEntityTypeConfiguration<ReportVersion>
{
    public void Configure(EntityTypeBuilder<ReportVersion> b)
    {
        b.HasIndex(x => new { x.ReportId, x.VersionNo }).IsUnique();
        b.Property(x => x.SnapshotJson).IsRequired(); // nvarchar(max): overrides the 256 default via explicit max length below
        b.Property(x => x.SnapshotJson).HasColumnType("nvarchar(max)");
        b.Property(x => x.Reason).HasMaxLength(500);
        b.Property(x => x.CreatedByName).HasMaxLength(200);
        b.HasOne(x => x.Report).WithMany(r => r.Versions).HasForeignKey(x => x.ReportId);
    }
}

public class BackupRecordConfig : IEntityTypeConfiguration<BackupRecord>
{
    public void Configure(EntityTypeBuilder<BackupRecord> b)
    {
        b.Property(x => x.FilePath).HasMaxLength(500);
        b.Property(x => x.Message).HasMaxLength(1000);
        b.Property(x => x.Kind).HasMaxLength(30);
        b.Property(x => x.CreatedBy).HasMaxLength(100);
    }
}
