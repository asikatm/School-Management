using MudBlazor;
using SchoolManagement.Domain.Admission;

namespace SchoolManagement.Web.Pages.Admission;

/// <summary>Colours, choices and Dhaka-time conversion shared by the admission screens.</summary>
public static class AdmissionUi
{
    private static readonly TimeSpan Dhaka = TimeSpan.FromHours(6);

    public static readonly string[] PaymentChannels = ["Cash", "Cheque", "POS", "BankDeposit", "bKash", "Nagad", "Rocket", "Card"];
    public static readonly string[] DocumentTypes = ["Birth certificate", "Photo", "Previous report card", "Transfer certificate", "Guardian NID", "Vaccination record", "Other"];
    public static readonly string[] Relationships = ["Father", "Mother", "Guardian", "Grandparent", "Sibling", "Other"];
    public static readonly string[] BloodGroups = ["A+", "A-", "B+", "B-", "AB+", "AB-", "O+", "O-"];
    public static readonly string[] Religions = ["Islam", "Hinduism", "Buddhism", "Christianity", "Other"];
    public static readonly string[] IncomeBands = ["Below 30,000", "30,000–60,000", "60,000–1,00,000", "1,00,000–2,00,000", "Above 2,00,000"];

    public static Color StageColor(EnquiryStage stage) => stage switch
    {
        EnquiryStage.Enquiry => Color.Default,
        EnquiryStage.CampusVisit => Color.Info,
        EnquiryStage.Applied => Color.Primary,
        EnquiryStage.TestInterview => Color.Secondary,
        EnquiryStage.Offered => Color.Warning,
        EnquiryStage.Admitted => Color.Success,
        _ => Color.Error
    };

    public static Color StatusColor(ApplicationStatus status) => status switch
    {
        ApplicationStatus.Draft => Color.Default,
        ApplicationStatus.Submitted or ApplicationStatus.UnderReview => Color.Info,
        ApplicationStatus.TestScheduled or ApplicationStatus.Tested => Color.Secondary,
        ApplicationStatus.Shortlisted or ApplicationStatus.Waitlisted => Color.Primary,
        ApplicationStatus.Offered => Color.Warning,
        ApplicationStatus.Accepted or ApplicationStatus.Enrolled => Color.Success,
        _ => Color.Error
    };

    public static Color OfferColor(OfferStatus status) => status switch
    {
        OfferStatus.Pending => Color.Warning,
        OfferStatus.Accepted => Color.Success,
        _ => Color.Error
    };

    public static Color InvoiceColor(AdmissionInvoiceStatus status) => status switch
    {
        AdmissionInvoiceStatus.Unpaid => Color.Warning,
        AdmissionInvoiceStatus.Paid => Color.Success,
        _ => Color.Default
    };

    /// <summary>A date and time picked in Dhaka local time, as UTC for the API.</summary>
    public static DateTime? ToUtc(DateTime? date, TimeSpan? time) =>
        date is null ? null : DateTime.SpecifyKind(date.Value.Date + (time ?? TimeSpan.Zero) - Dhaka, DateTimeKind.Utc);

    /// <summary>UTC from the API as a Dhaka-local date and time for the pickers.</summary>
    public static (DateTime Date, TimeSpan Time) FromUtc(DateTime utc)
    {
        var local = utc + Dhaka;
        return (local.Date, local.TimeOfDay);
    }

    public static DateTime TodayDhaka => (DateTime.UtcNow + Dhaka).Date;

    public static DateTime? ToDate(DateOnly? d) => d?.ToDateTime(TimeOnly.MinValue);
    public static DateOnly? ToDateOnly(DateTime? d) => d is null ? null : DateOnly.FromDateTime(d.Value);

    public static string Size(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes / 1024.0 / 1024.0:0.#} MB"
    };

    public static bool IsEditable(ApplicationStatus s) => s is ApplicationStatus.Draft or ApplicationStatus.Submitted or ApplicationStatus.UnderReview;

    public static bool CanOffer(ApplicationStatus s) =>
        s is ApplicationStatus.Shortlisted or ApplicationStatus.Waitlisted or ApplicationStatus.Tested or ApplicationStatus.UnderReview or ApplicationStatus.Submitted;

    public static bool AcceptsDocuments(ApplicationStatus s) =>
        s is ApplicationStatus.Draft or ApplicationStatus.Submitted or ApplicationStatus.UnderReview or ApplicationStatus.Offered or ApplicationStatus.Accepted;

    public static bool IsClosed(ApplicationStatus s) =>
        s is ApplicationStatus.Enrolled or ApplicationStatus.Rejected or ApplicationStatus.Withdrawn or ApplicationStatus.Declined;
}
