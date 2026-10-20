namespace SchoolManagement.Api.Common;

public static class RateLimitPolicies
{
    /// <summary>Sign-in and token refresh: per-IP limit against credential stuffing.</summary>
    public const string Login = "login";

    /// <summary>Anonymous website forms (enquiry, application, offer response).</summary>
    public const string PublicForms = "public-forms";
}
