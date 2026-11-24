namespace LaboratoryManagement.Domain.Entities;

/// <summary>A test or a panel. Panels (IsPanel) own no parameters; they reference component tests via PanelItems,
/// so result definitions are never duplicated.</summary>
public class LabTest : Entity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int CategoryId { get; set; }
    public TestCategory Category { get; set; } = null!;
    public int? SpecimenTypeId { get; set; }
    public SpecimenType? SpecimenType { get; set; }
    public int? ContainerId { get; set; }
    public SampleContainer? Container { get; set; }
    public string? Instructions { get; set; }
    public int TurnaroundHours { get; set; } = 24;
    public decimal Price { get; set; }
    /// <summary>True while the price is a seeded/demo sample price that an administrator has not reviewed.</summary>
    public bool PriceIsSample { get; set; }
    public string? Method { get; set; }
    public bool IsPanel { get; set; }
    public bool IsActive { get; set; } = true;
    public byte[]? RowVersion { get; set; }

    public ICollection<TestParameter> Parameters { get; set; } = new List<TestParameter>();
    /// <summary>When this test is a panel: its components.</summary>
    public ICollection<PanelItem> PanelItems { get; set; } = new List<PanelItem>();

    public string DisplayText => $"{Name} ({Code})";
}

public class PanelItem : Entity
{
    public int PanelTestId { get; set; }
    public LabTest PanelTest { get; set; } = null!;
    public int ComponentTestId { get; set; }
    public LabTest ComponentTest { get; set; } = null!;
    public int DisplayOrder { get; set; }
}

public class TestParameter : Entity
{
    public int LabTestId { get; set; }
    public LabTest LabTest { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public ResultType ResultType { get; set; } = ResultType.Numeric;
    public int? UnitId { get; set; }
    public MeasurementUnit? Unit { get; set; }
    public int DisplayOrder { get; set; }
    public int DecimalPlaces { get; set; } = 1;
    public bool IsActive { get; set; } = true;
    public ICollection<ReferenceRange> ReferenceRanges { get; set; } = new List<ReferenceRange>();
}

public class ReferenceRange : Entity
{
    public int TestParameterId { get; set; }
    public TestParameter TestParameter { get; set; } = null!;
    /// <summary>Null = applies to any gender.</summary>
    public Gender? Gender { get; set; }
    public decimal MinAgeYears { get; set; }
    /// <summary>Exclusive upper age bound; null = no upper bound.</summary>
    public decimal? MaxAgeYears { get; set; }
    public decimal? Low { get; set; }
    public decimal? High { get; set; }
    public decimal? CriticalLow { get; set; }
    public decimal? CriticalHigh { get; set; }
    /// <summary>For qualitative parameters: the expected/normal text (e.g. Negative, Non-Reactive).</summary>
    public string? NormalText { get; set; }
    /// <summary>Optional free-text override printed on reports (e.g. "Desirable: &lt; 200").</summary>
    public string? DisplayText { get; set; }
    /// <summary>Seeded example values; must be validated by the laboratory.</summary>
    public bool IsExample { get; set; }
}
