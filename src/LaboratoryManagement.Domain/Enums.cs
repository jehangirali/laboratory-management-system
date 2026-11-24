namespace LaboratoryManagement.Domain;

public enum Gender { Male = 1, Female = 2, Other = 3 }

public enum ResultType { Numeric = 1, Text = 2, PositiveNegative = 3, ReactiveNonReactive = 4 }

public enum OrderStatus { Registered = 1, InProgress = 2, Completed = 3, Cancelled = 4 }

public enum PaymentStatus { Unpaid = 1, Partial = 2, Paid = 3, Refunded = 4 }

public enum PaymentType { Payment = 1, Refund = 2 }

public enum SampleStatus
{
    PendingCollection = 1,
    Collected = 2,
    ReceivedInLab = 3,
    Processing = 4,
    Completed = 5,
    Rejected = 6,
    RecollectionRequired = 7
}

public enum OrderTestStatus { Pending = 1, InProgress = 2, ReadyForVerification = 3, Verified = 4, Cancelled = 5 }

public enum ReportStatus { Pending = 1, InProgress = 2, ReadyForVerification = 3, Verified = 4, Printed = 5, Amended = 6 }

public enum ResultFlag { None = 0, Low = 1, High = 2, CriticalLow = 3, CriticalHigh = 4, Abnormal = 5 }

public enum CommissionType { None = 0, Percentage = 1, Fixed = 2 }

public enum ReportTemplateType { General = 1, Hematology = 2, Profile = 3 }
