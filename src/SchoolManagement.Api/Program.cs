using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Scalar.AspNetCore;
using SchoolManagement.Api.Modules.Admission;
using SchoolManagement.Api.Modules.Admission.Services;
using SchoolManagement.Api.Modules.Students.Services;
using SchoolManagement.Infrastructure;
using SchoolManagement.Infrastructure.Approvals;
using SchoolManagement.Infrastructure.Jobs;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration, builder.Environment.ContentRootPath);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();

// ---- Auth (spec 5.3 / 5.5): short-lived JWT + rotating refresh tokens, permission policies per module.action
var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
if (Encoding.UTF8.GetByteCount(jwt.SigningKey) < 32)
    throw new InvalidOperationException("Jwt:SigningKey must be at least 32 bytes. Set it with user-secrets or an environment variable.");
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.AddScoped<TokenService>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = System.Security.Claims.ClaimTypes.Name
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, PermissionHandler>();

// ---- Rate limiting (spec 5.2 edge: rate limiting; 5.5: brute-force protection)
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy(RateLimitPolicies.Login, ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
    o.AddPolicy(RateLimitPolicies.PublicForms, ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1) }));
});

// ---- Modules
builder.Services.AddScoped<AdmissionNotifier>();
builder.Services.AddScoped<EnquiryIntake>();
builder.Services.AddScoped<ApplicationService>();
builder.Services.AddScoped<SelectionService>();
builder.Services.AddScoped<EnrolmentService>();
builder.Services.AddSingleton<IScheduledJob, OfferExpiryJob>();

builder.Services.AddScoped<IGuardianAccountService, GuardianAccountService>();
builder.Services.AddScoped<ISectionAllocator, SectionAllocator>();
builder.Services.AddScoped<IPromotionService, PromotionService>();
builder.Services.AddScoped<IRolloverService, RolloverService>();
builder.Services.AddScoped<WithdrawalService>();
builder.Services.AddScoped<IdCardService>();
builder.Services.AddScoped<StudentImportService>();
builder.Services.AddScoped<IApprovalHandler, WithdrawalApprovalHandler>();
builder.Services.AddScoped<IClearanceCheck, AdmissionDuesClearance>();
foreach (var area in new[] { "Tuition & other fees (FEE)", "Library (LIB)", "Transport (TRN)", "Store / uniform (INV)" })
    builder.Services.AddScoped<IClearanceCheck>(_ => new PendingModuleClearance(area));

// ---- MVC, validation, errors, OpenAPI
builder.Services.AddValidatorsFromAssemblyContaining<Program>();
builder.Services.AddControllers(o => o.Filters.Add<ValidationFilter>())
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddOpenApi(o =>
{
    o.AddDocumentTransformer((doc, _, _) =>
    {
        doc.Info = new OpenApiInfo
        {
            Title = "School Management API",
            Version = "v1",
            Description = "Release 1 — CORE (platform, administration & security), ADM (enquiry, admission & enrolment), SIS (student information). " +
                          "Feature IDs follow EMSL/LIS/SMS/2026-10."
        };
        doc.Components ??= new OpenApiComponents();
        doc.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        doc.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT", Description = "Paste the accessToken from POST /api/auth/login"
        };
        doc.Security ??= [];
        doc.Security.Add(new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", doc)] = [] });
        return Task.CompletedTask;
    });
});

builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [])
    .AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

app.UseExceptionHandler();
app.Use(async (ctx, next) =>
{
    // Security headers (spec 5.5). The API serves JSON plus a few printable HTML pages with inline styles and data: images.
    var h = ctx.Response.Headers;
    h["X-Content-Type-Options"] = "nosniff";
    h["X-Frame-Options"] = "DENY";
    h["Referrer-Policy"] = "no-referrer";
    if (ctx.Request.Path.StartsWithSegments("/api"))
        h["Content-Security-Policy"] = "default-src 'none'; img-src 'self' data:; style-src 'unsafe-inline'; frame-ancestors 'none'";
    else if (!ctx.Request.Path.StartsWithSegments("/scalar") && !ctx.Request.Path.StartsWithSegments("/openapi"))
        // Blazor WebAssembly UI (SchoolManagement.Web): needs wasm-unsafe-eval, MudBlazor inline styles and Google Fonts.
        h["Content-Security-Policy"] = "default-src 'self'; script-src 'self' 'wasm-unsafe-eval'; " +
                                       "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; font-src 'self' https://fonts.gstatic.com; " +
                                       "img-src 'self' data: blob:; connect-src 'self'; frame-ancestors 'none'";
    await next();
});

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(o => o.WithTitle("School Management API"));
}
else
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
// Serves the Blazor WebAssembly UI (SchoolManagement.Web, incl. _framework) from the same origin as the API.
app.MapStaticAssets();
// Unknown API paths stay 404; every other path is a UI route handled by the Blazor router.
app.Map("/api/{**rest}", () => Results.NotFound()).AllowAnonymous().ExcludeFromDescription();
app.MapFallbackToFile("index.html");

await using (var scope = app.Services.CreateAsyncScope())
{
    var options = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<DatabaseOptions>>().Value;
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (options.MigrateOnStartup) await db.Database.MigrateAsync();
    if (options.SeedOnStartup) await scope.ServiceProvider.GetRequiredService<DataSeeder>().SeedAsync();
}

app.Run();

public partial class Program;
