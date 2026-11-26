namespace LaboratoryManagement.Domain.Entities;

public class LabOrder : Entity
{
    public string OrderNo { get; set; } = string.Empty;
    public int PatientId { get; set; }
    public Patient Patient { get; set; } = null!;
    public int? DoctorId { get; set; }
    public Doctor? Doctor { get; set; }
    public DateTime OrderedAt { get; set; } = DateTime.UtcNow;
    public long Subtotal { get; set; }
    public long Discount { get; set; }
    public long NetAmount { get; set; }
    /// <summary>Payments received minus refunds.</summary>
    public long PaidAmount { get; set; }
    public long Balance { get; set; }
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Unpaid;
    public OrderStatus Status { get; set; } = OrderStatus.Registered;
    public string? Notes { get; set; }
    public string? CancelReason { get; set; }
    public DateTime? CancelledAt { get; set; }
    public int? CancelledByUserId { get; set; }
    public int CreatedByUserId { get; set; }
    public ICollection<OrderLine> Lines { get; set; } = new List<OrderLine>();
    public ICollection<OrderTest> Tests { get; set; } = new List<OrderTest>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
    public ICollection<Sample> Samples { get; set; } = new List<Sample>();
    public Report? Report { get; set; }
}

/// <summary>A billed line: one selected test or panel with its price at the time of ordering.</summary>
public class OrderLine : Entity
{
    public int OrderId { get; set; }
    public LabOrder Order { get; set; } = null!;
    public int LabTestId { get; set; }
    public LabTest LabTest { get; set; } = null!;
    public long Price { get; set; }
}

/// <summary>A test that is actually performed (panels are expanded into their component tests).</summary>
public class OrderTest : Entity
{
    public int OrderId { get; set; }
    public LabOrder Order { get; set; } = null!;
    public int? OrderLineId { get; set; }
    public OrderLine? OrderLine { get; set; }
    public int LabTestId { get; set; }
    public LabTest LabTest { get; set; } = null!;
    public int? SampleId { get; set; }
    public Sample? Sample { get; set; }
    public OrderTestStatus Status { get; set; } = OrderTestStatus.Pending;
    public ICollection<ResultValue> Results { get; set; } = new List<ResultValue>();
}

public class Payment : Entity
{
    public string ReceiptNo { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public LabOrder Order { get; set; } = null!;
    public long Amount { get; set; }
    public PaymentType Type { get; set; } = PaymentType.Payment;
    public int PaymentMethodId { get; set; }
    public PaymentMethod PaymentMethod { get; set; } = null!;
    public DateTime PaidAt { get; set; } = DateTime.UtcNow;
    public int ReceivedByUserId { get; set; }
    public string? Reference { get; set; }
    public string? Reason { get; set; }
}

public class Sample : Entity
{
    public int OrderId { get; set; }
    public LabOrder Order { get; set; } = null!;
    public string Barcode { get; set; } = string.Empty;
    public int? SpecimenTypeId { get; set; }
    public SpecimenType? SpecimenType { get; set; }
    public int? ContainerId { get; set; }
    public SampleContainer? Container { get; set; }
    public SampleStatus Status { get; set; } = SampleStatus.PendingCollection;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CollectedAt { get; set; }
    public int? CollectedByUserId { get; set; }
    public DateTime? ReceivedAt { get; set; }
    public int? ReceivedByUserId { get; set; }
    public string? RejectionReason { get; set; }
    public DateTime? RejectedAt { get; set; }
    /// <summary>Set on a replacement sample: the rejected sample it replaces.</summary>
    public int? RecollectionOfSampleId { get; set; }
    public Sample? RecollectionOfSample { get; set; }
    public ICollection<OrderTest> Tests { get; set; } = new List<OrderTest>();
}

public class ResultValue : Entity
{
    public int OrderTestId { get; set; }
    public OrderTest OrderTest { get; set; } = null!;
    public int TestParameterId { get; set; }
    public TestParameter TestParameter { get; set; } = null!;
    public string? Value { get; set; }
    /// <summary>Snapshot of the unit at entry time.</summary>
    public string? Unit { get; set; }
    /// <summary>Snapshot of the reference range text at entry time (template/range edits never change stored results).</summary>
    public string? ReferenceText { get; set; }
    public ResultFlag Flag { get; set; }
    public string? Comment { get; set; }
    public int EnteredByUserId { get; set; }
    public DateTime EnteredAt { get; set; } = DateTime.UtcNow;
    public ICollection<ResultRevision> Revisions { get; set; } = new List<ResultRevision>();
}

public class ResultRevision : Entity
{
    public int ResultValueId { get; set; }
    public ResultValue ResultValue { get; set; } = null!;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public ResultFlag OldFlag { get; set; }
    public ResultFlag NewFlag { get; set; }
    public int ChangedByUserId { get; set; }
    public string ChangedByName { get; set; } = string.Empty;
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
    public string? Reason { get; set; }
    public bool IsAmendment { get; set; }
}

public class Report : Entity
{
    public int OrderId { get; set; }
    public LabOrder Order { get; set; } = null!;
    public ReportStatus Status { get; set; } = ReportStatus.Pending;
    public bool IsAmended { get; set; }
    public string? AmendmentReason { get; set; }
    public int? VerifiedByUserId { get; set; }
    public string? VerifierName { get; set; }
    public string? VerifierDesignation { get; set; }
    public string? VerifierRoles { get; set; }
    public string? SignatureText { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public DateTime? PrintedAt { get; set; }
    public bool DirectlyFinalized { get; set; }
}

public class BackupRecord : Entity
{
    public DateTime At { get; set; } = DateTime.UtcNow;
    public string FilePath { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public bool Success { get; set; }
    public string? Message { get; set; }
    /// <summary>Manual, Scheduled, PreUpgrade, Restore.</summary>
    public string Kind { get; set; } = "Manual";
    public string? CreatedBy { get; set; }
}
