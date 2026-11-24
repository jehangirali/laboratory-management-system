using System.Globalization;
using LaboratoryManagement.Domain;
using LaboratoryManagement.Domain.Entities;

namespace LaboratoryManagement.Application.Services;

/// <summary>Pure functions: pick the applicable reference range and flag a value against the laboratory's configured ranges only.
/// Never produces diagnoses.</summary>
public static class ReferenceRangeEvaluator
{
    public static ReferenceRange? Select(IEnumerable<ReferenceRange> ranges, Gender gender, decimal? ageYears)
    {
        return ranges
            .Where(r => r.Gender == null || r.Gender == gender)
            .Where(r => ageYears == null || (ageYears >= r.MinAgeYears && (r.MaxAgeYears == null || ageYears < r.MaxAgeYears)))
            .OrderByDescending(r => r.Gender == gender ? 1 : 0)
            .ThenByDescending(r => r.MinAgeYears)
            .FirstOrDefault();
    }

    public static bool TryParseNumber(string? text, out decimal value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.Trim().TrimStart('<', '>', '=', ' ').Replace(",", "");
        return decimal.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    public static ResultFlag Evaluate(ResultType type, string? value, ReferenceRange? range)
    {
        if (string.IsNullOrWhiteSpace(value) || range == null) return ResultFlag.None;
        switch (type)
        {
            case ResultType.Numeric:
                if (!TryParseNumber(value, out var v)) return ResultFlag.None;
                if (range.CriticalLow != null && v < range.CriticalLow) return ResultFlag.CriticalLow;
                if (range.CriticalHigh != null && v > range.CriticalHigh) return ResultFlag.CriticalHigh;
                if (range.Low != null && v < range.Low) return ResultFlag.Low;
                if (range.High != null && v > range.High) return ResultFlag.High;
                return ResultFlag.None;
            case ResultType.PositiveNegative:
            case ResultType.ReactiveNonReactive:
                if (string.IsNullOrWhiteSpace(range.NormalText)) return ResultFlag.None;
                return string.Equals(value.Trim(), range.NormalText.Trim(), StringComparison.OrdinalIgnoreCase)
                    ? ResultFlag.None : ResultFlag.Abnormal;
            default:
                return ResultFlag.None;
        }
    }

    public static string FormatRange(ReferenceRange? r)
    {
        if (r == null) return "";
        if (!string.IsNullOrWhiteSpace(r.DisplayText)) return r.DisplayText!;
        if (!string.IsNullOrWhiteSpace(r.NormalText) && r.Low == null && r.High == null) return r.NormalText!;
        var inv = CultureInfo.InvariantCulture;
        if (r.Low != null && r.High != null) return $"{r.Low.Value.ToString("0.##", inv)} - {r.High.Value.ToString("0.##", inv)}";
        if (r.Low != null) return $"> {r.Low.Value.ToString("0.##", inv)}";
        if (r.High != null) return $"< {r.High.Value.ToString("0.##", inv)}";
        return "";
    }

    public static string FlagText(ResultFlag f) => f switch
    {
        ResultFlag.Low => "L",
        ResultFlag.High => "H",
        ResultFlag.CriticalLow => "LL",
        ResultFlag.CriticalHigh => "HH",
        ResultFlag.Abnormal => "A",
        _ => ""
    };
}
