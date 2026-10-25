using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Mail;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using NotesApi.Controllers;
using NotesApi.Middleware;
using NotesApi.Models;
using NotesApi.Repositories;
using NotesApi.Services;

// In-memory users for authentication (preserved)
var users = new List<User>();
var usersLock = new object();

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Example: \"Bearer {token}\"",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddMemoryCache();

// Register repository, service, and controller
builder.Services.AddSingleton<INotesRepository, InMemoryNotesRepository>();
builder.Services.AddScoped<NotesService>();
builder.Services.AddControllers();

var jwtKey = builder.Configuration["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key is not configured");
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "NotesApi";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "NotesApi";
var jwtExpiresHours = builder.Configuration.GetValue("Jwt:ExpiresHours", 24);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = JwtRegisteredClaimNames.Sub
        };
        options.Events = new JwtBearerEvents
        {
            OnChallenge = async context =>
            {
                context.HandleResponse();
                var traceId = context.HttpContext.Items["TraceId"]?.ToString() ?? Guid.NewGuid().ToString();
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(
                    new ErrorResponse(401, "Unauthorized", "Missing or invalid token", traceId));
            }
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("NotesPolicy", opt =>
    {
        opt.PermitLimit = 10;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueLimit = 0;
    });
    options.OnRejected = async (ctx, token) =>
    {
        ctx.HttpContext.Response.StatusCode = 429;
        await ctx.HttpContext.Response.WriteAsJsonAsync(new { error = "Too Many Requests", message = "Rate limit exceeded: 10 requests per minute per IP on /notes" });
    };
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins("http://localhost:5173")
              .AllowAnyHeader()
              .AllowAnyMethod());
});

var app = builder.Build();

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseRateLimiter();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/", () => Results.Redirect("/swagger"));

var authGroup = app.MapGroup("/auth");

authGroup.MapPost("/signup", (HttpContext httpContext, AuthRequest dto, IPasswordHasher<User> passwordHasher) =>
{
    var traceId = httpContext.Items["TraceId"]?.ToString() ?? Guid.NewGuid().ToString();
    var errors = ValidateAuthRequest(dto, requirePasswordStrength: true);
    if (errors.Count > 0)
    {
        var message = string.Join("; ", errors);
        return Results.BadRequest(new ErrorResponse(400, "ValidationError", message, traceId));
    }

    var email = NormalizeEmail(dto.Email!);

    User user;
    lock (usersLock)
    {
        if (users.Any(u => string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase)))
        {
            return Results.Conflict(new ErrorResponse(409, "Conflict", "An account with this email already exists", traceId));
        }

        user = new User { Id = Guid.NewGuid(), Email = email };
        user.PasswordHash = passwordHasher.HashPassword(user, dto.Password!);
        users.Add(user);
    }

    return Results.Created($"/auth/signup", new SignupResponse(user.Id, user.Email));
});

authGroup.MapPost("/login", (HttpContext httpContext, AuthRequest dto, IPasswordHasher<User> passwordHasher) =>
{
    var traceId = httpContext.Items["TraceId"]?.ToString() ?? Guid.NewGuid().ToString();
    var errors = ValidateAuthRequest(dto, requirePasswordStrength: false);
    if (errors.Count > 0)
    {
        var message = string.Join("; ", errors);
        return Results.BadRequest(new ErrorResponse(400, "ValidationError", message, traceId));
    }

    var email = NormalizeEmail(dto.Email!);
    User? user;
    lock (usersLock)
    {
        user = users.FirstOrDefault(u => string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase));
    }

    if (user is null ||
        passwordHasher.VerifyHashedPassword(user, user.PasswordHash, dto.Password!) == PasswordVerificationResult.Failed)
    {
        return Results.Json(
            new ErrorResponse(401, "Unauthorized", "Invalid email or password", traceId),
            statusCode: StatusCodes.Status401Unauthorized);
    }

    var expiresAt = DateTime.UtcNow.AddHours(jwtExpiresHours);
    var token = CreateJwt(user, jwtKey, jwtIssuer, jwtAudience, expiresAt);
    return Results.Ok(new LoginResponse(token, expiresAt, user.Id, user.Email));
});

// Map controller routes for notes
app.MapControllers().RequireRateLimiting("NotesPolicy").RequireAuthorization();

app.Run();

static List<string> ValidateAuthRequest(AuthRequest dto, bool requirePasswordStrength)
{
    var errors = new List<string>();
    var email = dto.Email?.Trim();

    if (string.IsNullOrWhiteSpace(email))
        errors.Add("Email is required");
    else if (!MailAddress.TryCreate(email, out _))
        errors.Add("Email is invalid");

    if (string.IsNullOrWhiteSpace(dto.Password))
        errors.Add("Password is required");
    else if (requirePasswordStrength && dto.Password.Length < 8)
        errors.Add("Password must be at least 8 characters");

    return errors;
}

static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

static string CreateJwt(User user, string key, string issuer, string audience, DateTime expiresAt)
{
    var claims = new[]
    {
        new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new Claim(JwtRegisteredClaimNames.Email, user.Email)
    };

    var credentials = new SigningCredentials(
        new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
        SecurityAlgorithms.HmacSha256);

    var token = new JwtSecurityToken(
        issuer: issuer,
        audience: audience,
        claims: claims,
        expires: expiresAt,
        signingCredentials: credentials);

    return new JwtSecurityTokenHandler().WriteToken(token);
}