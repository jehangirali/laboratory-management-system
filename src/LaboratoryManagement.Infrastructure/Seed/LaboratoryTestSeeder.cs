using System.Globalization;
using LaboratoryManagement.Domain;
using LaboratoryManagement.Domain.Entities;
using LaboratoryManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LaboratoryManagement.Infrastructure.Seed;

/// <summary>Starter catalog. Prices are seeded as 0 (not set). Reference ranges and units are EXAMPLES ONLY and are
/// flagged IsExample; every laboratory must validate them against its analyzer, method and population.</summary>
public sealed class LaboratoryTestSeeder : ISeeder
{
    public string Name => nameof(LaboratoryTestSeeder);
    public int Order => 70;

    private record P(string Name, string Unit, string Range, ResultType Type = ResultType.Numeric, string? Crit = null, int Dp = 1);
    private record T(string Code, string Name, string Cat, string Spec, string? Cont, P[] Params, string? Method = null, int Tat = 24);

    private static P N(string name, string unit, string range, string? crit = null, int dp = 1) => new(name, unit, range, ResultType.Numeric, crit, dp);
    private static P Txt(string name) => new(name, "", "", ResultType.Text);
    private static P PN(string name) => new(name, "", "Negative", ResultType.PositiveNegative);
    private static P RN(string name) => new(name, "", "Non-Reactive", ResultType.ReactiveNonReactive);

    private static T One(string code, string name, string cat, string spec, string? cont, string unit, string range, string? crit = null, string? method = null, int tat = 24, int dp = 1)
        => new(code, name, cat, spec, cont, new[] { N(name, unit, range, crit, dp) }, method, tat);

    private static readonly T[] Tests =
    {
        // Hematology
        new("CBC", "Complete Blood Count (CBC)", "HEM", "BLOOD", "EDTA", new[]
        {
            N("Hemoglobin (Hb)", "g/dL", "M:13.0-17.0,F:12.0-15.0", "7/20"),
            N("RBC Count", "x10^6/µL", "M:4.5-5.5,F:3.8-4.8", null, 2),
            N("WBC (TLC)", "x10^3/µL", "4.0-11.0", "2/30"),
            N("Platelet Count", "x10^3/µL", "150-400", "20/1000", 0),
            N("Hematocrit (PCV)", "%", "M:40-50,F:36-46"),
            N("MCV", "fL", "83-101"), N("MCH", "pg", "27-32"), N("MCHC", "g/dL", "31.5-34.5"),
            N("Neutrophils", "%", "40-80", null, 0), N("Lymphocytes", "%", "20-40", null, 0), N("Monocytes", "%", "2-10", null, 0),
            N("Eosinophils", "%", "1-6", null, 0), N("Basophils", "%", "0-2", null, 0),
        }, "Automated analyzer", 6),
        One("HB", "Hemoglobin", "HEM", "BLOOD", "EDTA", "g/dL", "M:13.0-17.0,F:12.0-15.0", "7/20", tat: 4),
        One("ESR", "ESR", "HEM", "BLOOD", "EDTA", "mm/hr", "M:0-15,F:0-20", null, "Westergren", 4, 0),
        One("PLT", "Platelet Count", "HEM", "BLOOD", "EDTA", "x10^3/µL", "150-400", "20/1000", tat: 4, dp: 0),
        One("TLC", "Total Leukocyte Count (TLC)", "HEM", "BLOOD", "EDTA", "x10^3/µL", "4.0-11.0", "2/30", tat: 4),
        new("DLC", "Differential Leukocyte Count (DLC)", "HEM", "BLOOD", "EDTA", new[]
        { N("Neutrophils", "%", "40-80", null, 0), N("Lymphocytes", "%", "20-40", null, 0), N("Monocytes", "%", "2-10", null, 0), N("Eosinophils", "%", "1-6", null, 0), N("Basophils", "%", "0-2", null, 0) }, null, 6),
        One("RBC", "RBC Count", "HEM", "BLOOD", "EDTA", "x10^6/µL", "M:4.5-5.5,F:3.8-4.8", tat: 4, dp: 2),
        One("PCV", "PCV / Hematocrit", "HEM", "BLOOD", "EDTA", "%", "M:40-50,F:36-46", tat: 4),
        One("RETIC", "Reticulocyte Count", "HEM", "BLOOD", "EDTA", "%", "0.5-2.5", tat: 6, dp: 1),
        new("PBF", "Peripheral Blood Film", "HEM", "BLOOD", "EDTA", new[] { Txt("RBC Morphology"), Txt("WBC Morphology"), Txt("Platelets"), Txt("Impression / Comments") }, "Microscopy", 24),
        new("BG", "Blood Group & Rh", "HEM", "BLOOD", "EDTA", new[] { Txt("Blood Group"), Txt("Rh Factor") }, "Tube/slide agglutination", 4),
        // Biochemistry
        One("FBS", "Fasting Blood Sugar (FBS)", "BIO", "PLASMA", "FLUORIDE", "mg/dL", "70-100", "40/500", tat: 4, dp: 0),
        One("RBS", "Random Blood Sugar (RBS)", "BIO", "PLASMA", "FLUORIDE", "mg/dL", "70-140", "40/500", tat: 4, dp: 0),
        One("HBA1C", "HbA1c", "BIO", "BLOOD", "EDTA", "%", "4.0-5.6", tat: 24, dp: 1),
        One("UREA", "Urea", "BIO", "SERUM", "SST", "mg/dL", "15-45", dp: 0),
        One("CREAT", "Creatinine", "BIO", "SERUM", "SST", "mg/dL", "M:0.7-1.3,F:0.6-1.1", null, dp: 2),
        One("UA", "Uric Acid", "BIO", "SERUM", "SST", "mg/dL", "M:3.4-7.0,F:2.4-6.0"),
        One("ALT", "ALT / SGPT", "BIO", "SERUM", "SST", "U/L", "M:0-45,F:0-35", dp: 0),
        One("AST", "AST / SGOT", "BIO", "SERUM", "SST", "U/L", "0-40", dp: 0),
        One("ALP", "Alkaline Phosphatase (ALP)", "BIO", "SERUM", "SST", "U/L", "40-130", dp: 0),
        One("TBIL", "Total Bilirubin", "BIO", "SERUM", "SST", "mg/dL", "0.2-1.2", dp: 2),
        One("DBIL", "Direct Bilirubin", "BIO", "SERUM", "SST", "mg/dL", "0.0-0.3", dp: 2),
        One("TP", "Total Protein", "BIO", "SERUM", "SST", "g/dL", "6.0-8.3"),
        One("ALB", "Albumin", "BIO", "SERUM", "SST", "g/dL", "3.5-5.2"),
        One("CA", "Calcium", "BIO", "SERUM", "SST", "mg/dL", "8.6-10.2"),
        One("PHOS", "Phosphorus", "BIO", "SERUM", "SST", "mg/dL", "2.5-4.5"),
        One("MG", "Magnesium", "BIO", "SERUM", "SST", "mg/dL", "1.6-2.6"),
        // Lipids
        One("CHOL", "Total Cholesterol", "BIO", "SERUM", "SST", "mg/dL", "<200", dp: 0),
        One("TG", "Triglycerides", "BIO", "SERUM", "SST", "mg/dL", "<150", dp: 0),
        One("HDL", "HDL Cholesterol", "BIO", "SERUM", "SST", "mg/dL", ">40", dp: 0),
        One("LDL", "LDL Cholesterol", "BIO", "SERUM", "SST", "mg/dL", "<100", dp: 0),
        One("VLDL", "VLDL Cholesterol", "BIO", "SERUM", "SST", "mg/dL", "5-40", dp: 0),
        // Thyroid
        One("T3", "T3 (Total)", "THY", "SERUM", "SST", "ng/dL", "80-200", dp: 0),
        One("T4", "T4 (Total)", "THY", "SERUM", "SST", "µg/dL", "5.1-14.1"),
        One("TSH", "TSH", "THY", "SERUM", "SST", "µIU/mL", "0.4-4.0", dp: 2),
        One("FT3", "Free T3", "THY", "SERUM", "SST", "pg/mL", "2.0-4.4", dp: 2),
        One("FT4", "Free T4", "THY", "SERUM", "SST", "ng/dL", "0.8-1.8", dp: 2),
        // Electrolytes
        One("NA", "Sodium (Na)", "ELEC", "SERUM", "SST", "mmol/L", "135-145", "120/160", dp: 0),
        One("K", "Potassium (K)", "ELEC", "SERUM", "SST", "mmol/L", "3.5-5.1", "2.5/6.5"),
        One("CL", "Chloride (Cl)", "ELEC", "SERUM", "SST", "mmol/L", "98-107", dp: 0),
        One("HCO3", "Bicarbonate (HCO3)", "ELEC", "SERUM", "SST", "mmol/L", "22-29", dp: 0),
        // Urine
        new("CUE", "Complete Urine Examination (CUE)", "URINE", "URINE", "URINECUP", new[]
        {
            Txt("Colour"), Txt("Appearance"), N("pH", "", "5.0-8.0"), N("Specific Gravity", "", "1.005-1.030", null, 3),
            PN("Protein"), PN("Glucose"), PN("Ketones"), PN("Bilirubin"), PN("Blood"), PN("Nitrite"),
            N("Pus Cells", "/HPF", "0-5", null, 0), N("RBCs", "/HPF", "0-2", null, 0), Txt("Epithelial Cells"), Txt("Casts"), Txt("Crystals"), Txt("Bacteria")
        }, "Dipstick + microscopy", 4),
        new("URE", "Urine Routine Examination", "URINE", "URINE", "URINECUP", new[] { Txt("Physical Examination"), Txt("Chemical Examination"), Txt("Microscopic Examination") }, null, 4),
        new("UCS", "Urine Culture & Sensitivity", "MICRO", "URINE", "URINECUP", new[] { Txt("Culture Result"), Txt("Organism Isolated"), Txt("Sensitivity") }, "Culture", 72),
        new("UPRO", "Urine Protein", "URINE", "URINE", "URINECUP", new[] { PN("Protein") }, "Dipstick", 4),
        new("USUG", "Urine Sugar", "URINE", "URINE", "URINECUP", new[] { PN("Sugar") }, "Dipstick", 4),
        // Stool
        new("SRE", "Stool Routine Examination", "STOOL", "STOOL", "STOOLCUP", new[] { Txt("Colour"), Txt("Consistency"), Txt("Mucus"), Txt("Ova / Cysts / Parasites"), N("Pus Cells", "/HPF", "0-5", null, 0), N("RBCs", "/HPF", "0-2", null, 0) }, "Microscopy", 12),
        new("OB", "Stool Occult Blood", "STOOL", "STOOL", "STOOLCUP", new[] { PN("Occult Blood") }, null, 12),
        new("SCS", "Stool Culture", "MICRO", "STOOL", "STOOLCUP", new[] { Txt("Culture Result"), Txt("Organism Isolated"), Txt("Sensitivity") }, "Culture", 72),
        // Serology
        new("HBSAG", "HBsAg", "SERO", "SERUM", "SST", new[] { RN("HBsAg") }, "Immunochromatography / ELISA", 6),
        new("HCV", "Anti-HCV", "SERO", "SERUM", "SST", new[] { RN("Anti-HCV") }, "Immunochromatography / ELISA", 6),
        new("HIV", "HIV Screening", "SERO", "SERUM", "SST", new[] { RN("HIV 1/2") }, "Immunochromatography / ELISA", 6),
        new("DNS1", "Dengue NS1 Antigen", "SERO", "SERUM", "SST", new[] { PN("Dengue NS1") }, "Immunochromatography", 6),
        new("DENGM", "Dengue IgG / IgM", "SERO", "SERUM", "SST", new[] { PN("Dengue IgM"), PN("Dengue IgG") }, "Immunochromatography", 6),
        new("TYPH", "Typhoid Screening (IgM/IgG)", "SERO", "SERUM", "SST", new[] { PN("Typhoid IgM"), PN("Typhoid IgG") }, "Immunochromatography", 6),
        One("CRP", "CRP", "IMM", "SERUM", "SST", "mg/L", "<6", dp: 1),
        One("ASO", "ASO Titre", "IMM", "SERUM", "SST", "IU/mL", "<200", dp: 0),
        new("RF", "Rheumatoid Factor", "IMM", "SERUM", "SST", new[] { PN("Rheumatoid Factor") }, "Latex agglutination", 6),
        // Cardiac
        One("TROPI", "Troponin I", "CARD", "SERUM", "SST", "ng/mL", "<0.04", dp: 3, tat: 2),
        One("TROPT", "Troponin T", "CARD", "SERUM", "SST", "ng/mL", "<0.014", dp: 3, tat: 2),
        One("CKMB", "CK-MB", "CARD", "SERUM", "SST", "U/L", "<25", dp: 0, tat: 4),
        One("CPK", "CPK", "CARD", "SERUM", "SST", "U/L", "M:39-308,F:26-192", dp: 0, tat: 4),
        // Hormones / vitamins
        One("LH", "LH", "HORM", "SERUM", "SST", "mIU/mL", "1.7-8.6", dp: 2),
        One("FSH", "FSH", "HORM", "SERUM", "SST", "mIU/mL", "1.5-12.4", dp: 2),
        One("PRL", "Prolactin", "HORM", "SERUM", "SST", "ng/mL", "M:4.0-15.2,F:4.8-23.3", dp: 1),
        One("TESTO", "Testosterone", "HORM", "SERUM", "SST", "ng/dL", "M:280-1100,F:15-70", dp: 0),
        One("E2", "Estradiol (E2)", "HORM", "SERUM", "SST", "pg/mL", "M:11-44,F:30-400", dp: 0),
        One("BHCG", "Beta-hCG", "HORM", "SERUM", "SST", "mIU/mL", "<5", dp: 1),
        One("VITD", "Vitamin D (25-OH)", "SPEC", "SERUM", "SST", "ng/mL", "30-100", dp: 1),
        One("B12", "Vitamin B12", "SPEC", "SERUM", "SST", "pg/mL", "200-900", dp: 0),
        // Coagulation
        One("PT", "Prothrombin Time (PT)", "COAG", "PLASMA", "CITRATE", "sec", "11-14", dp: 1, tat: 4),
        One("INR", "INR", "COAG", "PLASMA", "CITRATE", "ratio", "0.8-1.2", dp: 2, tat: 4),
        One("APTT", "APTT", "COAG", "PLASMA", "CITRATE", "sec", "25-35", dp: 1, tat: 4),
    };

    private static readonly (string Code, string Name, string[] Components)[] Panels =
    {
        ("LFT", "Liver Function Tests (LFT)", new[] { "TBIL", "DBIL", "ALT", "AST", "ALP", "TP", "ALB" }),
        ("RFT", "Renal Function Tests (RFT)", new[] { "UREA", "CREAT", "UA", "NA", "K", "CL" }),
        ("LIPID", "Lipid Profile", new[] { "CHOL", "TG", "HDL", "LDL", "VLDL" }),
        ("THYP", "Thyroid Profile", new[] { "T3", "T4", "TSH" }),
        ("ELYTE", "Electrolytes Profile", new[] { "NA", "K", "CL", "HCO3" }),
        ("COAGP", "Coagulation Profile", new[] { "PT", "INR", "APTT" }),
    };

    public async Task SeedAsync(AppDbContext db, CancellationToken ct)
    {
        var cats = await db.TestCategories.ToDictionaryAsync(c => c.Code, c => c.Id, ct);
        var specs = await db.SpecimenTypes.ToDictionaryAsync(c => c.Code, c => c.Id, ct);
        var conts = await db.SampleContainers.ToDictionaryAsync(c => c.Code, c => c.Id, ct);
        var units = await db.MeasurementUnits.ToDictionaryAsync(c => c.Code, c => c.Id, ct);
        var existing = await db.LabTests.ToDictionaryAsync(t => t.Code, t => t.Id, ct);

        foreach (var t in Tests)
        {
            if (existing.ContainsKey(t.Code)) continue; // never overwrite administrator-edited tests
            var test = new LabTest
            {
                Code = t.Code, Name = t.Name, CategoryId = cats[t.Cat], SpecimenTypeId = specs[t.Spec],
                ContainerId = t.Cont == null ? null : conts[t.Cont], Method = t.Method, TurnaroundHours = t.Tat, Price = 0, IsActive = true
            };
            var order = 0;
            foreach (var p in t.Params)
            {
                var param = new TestParameter
                {
                    Name = p.Name, ResultType = p.Type, DisplayOrder = ++order, DecimalPlaces = p.Dp,
                    UnitId = !string.IsNullOrEmpty(p.Unit) && units.TryGetValue(p.Unit, out var uid) ? uid : null
                };
                foreach (var r in ParseRanges(p)) param.ReferenceRanges.Add(r);
                test.Parameters.Add(param);
            }
            db.LabTests.Add(test);
        }
        await db.SaveChangesAsync(ct);

        existing = await db.LabTests.ToDictionaryAsync(t => t.Code, t => t.Id, ct);
        foreach (var (code, name, comps) in Panels)
        {
            if (existing.ContainsKey(code)) continue;
            var panel = new LabTest { Code = code, Name = name, CategoryId = cats["PROF"], IsPanel = true, Price = 0, TurnaroundHours = 24 };
            var i = 0;
            foreach (var c in comps)
                if (existing.TryGetValue(c, out var cid)) panel.PanelItems.Add(new PanelItem { ComponentTestId = cid, DisplayOrder = ++i });
            db.LabTests.Add(panel);
        }
    }

    private static IEnumerable<ReferenceRange> ParseRanges(P p)
    {
        if (string.IsNullOrWhiteSpace(p.Range)) yield break;
        (decimal? cl, decimal? ch) crit = (null, null);
        if (p.Crit != null)
        {
            var c = p.Crit.Split('/');
            crit = (Dec(c[0]), Dec(c[1]));
        }
        if (p.Type is ResultType.PositiveNegative or ResultType.ReactiveNonReactive)
        {
            yield return new ReferenceRange { NormalText = p.Range, IsExample = true };
            yield break;
        }
        foreach (var seg in p.Range.Split(','))
        {
            Gender? g = null; var s = seg.Trim();
            if (s.StartsWith("M:")) { g = Gender.Male; s = s[2..]; }
            else if (s.StartsWith("F:")) { g = Gender.Female; s = s[2..]; }
            var r = new ReferenceRange { Gender = g, CriticalLow = crit.cl, CriticalHigh = crit.ch, IsExample = true };
            if (s.StartsWith("<")) r.High = Dec(s[1..]);
            else if (s.StartsWith(">")) r.Low = Dec(s[1..]);
            else { var parts = s.Split('-'); r.Low = Dec(parts[0]); r.High = Dec(parts[1]); }
            yield return r;
        }
    }

    private static decimal Dec(string s) => decimal.Parse(s.Trim(), CultureInfo.InvariantCulture);
}
