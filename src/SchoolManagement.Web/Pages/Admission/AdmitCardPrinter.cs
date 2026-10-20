using System.Net;
using System.Text;
using SchoolManagement.Web.Models;
using SchoolManagement.Web.Services;
using SchoolManagement.Web.Shared;

namespace SchoolManagement.Web.Pages.Admission;

/// <summary>
/// Builds a printable admit-card page from the API's admit-card data, embedding each card's QR code
/// (fetched with the bearer token, so the page works offline once opened).
/// </summary>
public static class AdmitCardPrinter
{
    private const string Style = """
        body{font-family:'Segoe UI','Noto Sans Bengali',sans-serif;margin:16px;color:#1b1b1b}
        .card{border:2px solid #1e3a5f;border-radius:8px;padding:14px 18px;margin:0 0 16px;display:flex;gap:16px;page-break-inside:avoid;max-width:720px}
        .card h2{margin:0 0 6px;font-size:15pt;color:#1e3a5f}
        .card td{padding:2px 10px 2px 0;font-size:11pt;vertical-align:top}
        .card img{width:120px;height:120px}
        .no{font-size:16pt;font-weight:700;letter-spacing:1px}
        """;

    public static async Task<ApiFile> BuildAsync(ApiClient api, IReadOnlyList<AdmissionAdmitCardView> cards)
    {
        static string E(string? v) => WebUtility.HtmlEncode(v ?? "—");

        var html = new StringBuilder($"<!doctype html><html><head><meta charset=\"utf-8\"><title>Admit cards</title><style>{Style}</style></head><body>");
        foreach (var c in cards)
        {
            var qr = await api.GetFileAsync($"api/admission/test-sessions/admit-cards/{Uri.EscapeDataString(c.AdmitCardNo)}/qr");
            html.Append($"""
                <div class="card"><div style="flex:1">
                <h2>{E(c.CampusName)} — Admission test admit card</h2>
                <div class="no">{E(c.AdmitCardNo)}</div>
                <table>
                <tr><td>Applicant</td><td><b>{E(c.Name)}</b> ({E(c.ApplicationNo)})</td></tr>
                <tr><td>Class</td><td>{E(c.ClassName)}</td></tr>
                <tr><td>Test</td><td>{E(c.SessionName)}</td></tr>
                <tr><td>Date &amp; time</td><td>{E(Format.DateTime(c.StartsAtUtc))} ({c.DurationMinutes} min)</td></tr>
                <tr><td>Room / seat</td><td>{E(c.Room)} / <b>{E(c.SeatNo)}</b></td></tr>
                </table></div>
                <img src="data:{qr.ContentType};base64,{Convert.ToBase64String(qr.Content)}" alt="QR code"></div>
                """);
        }
        html.Append("<script>window.onload=()=>window.print()</script></body></html>");
        return new ApiFile(Encoding.UTF8.GetBytes(html.ToString()), "text/html", "admit-cards.html");
    }
}
