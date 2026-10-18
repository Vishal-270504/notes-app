using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Mail;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using NotesApi.Data;
using NotesApi.Middleware;
using NotesApi.Models;

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

builder.Services.AddDbContext<NotesDbContext>(options =>
{
    options.UseSqlite("Data Source=notes.db");
    // Enable simple logging to see SQL execution times
    options.LogTo(Console.WriteLine, LogLevel.Information);
});

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

// Apply migrations on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<NotesDbContext>();
    db.Database.Migrate();
}

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

var notesGroup = app.MapGroup("/notes")
    .RequireRateLimiting("NotesPolicy")
    .RequireAuthorization();

// GET /notes with pagination (current user's notes only)
// Uses composite index IX_Notes_UserId_CreatedAt for fast filtering + ordering
notesGroup.MapGet("", async (HttpContext httpContext, int? page, int? limit, NotesDbContext db) =>
{
    int pageNumber = page.GetValueOrDefault(1);
    int pageSize = limit.GetValueOrDefault(10);
    if (pageNumber < 1) pageNumber = 1;
    if (pageSize < 1) pageSize = 10;
    if (pageSize > 100) pageSize = 100;

    var userId = GetUserId(httpContext);
    
    // Count total for pagination metadata
    int total = await db.Notes.CountAsync(n => n.UserId == userId);
    int totalPages = (int)Math.Ceiling(total / (double)pageSize);

    // The WHERE UserId = ? + ORDER BY CreatedAt DESC can be satisfied by the composite index
    // Without index: full table scan + sort O(n log n)
    // With index: index seek to user's rows in CreatedAt order O(log n + k)
    var pagedNotes = await db.Notes
        .Where(n => n.UserId == userId)
        .OrderByDescending(n => n.CreatedAt)
        .Skip((pageNumber - 1) * pageSize)
        .Take(pageSize)
        .ToListAsync();

    return Results.Ok(new { page = pageNumber, limit = pageSize, total, totalPages, notes = pagedNotes });
});

// POST /notes with validation
notesGroup.MapPost("", async (HttpContext httpContext, NoteCreateDto dto, NotesDbContext db) =>
{
    var traceId = httpContext.Items["TraceId"]?.ToString() ?? Guid.NewGuid().ToString();
    var errors = new List<string>();
    var title = dto.Title?.Trim();

    if (string.IsNullOrWhiteSpace(title))
        errors.Add("Title is required");
    else
    {
        if (title.Length < 3) errors.Add("Title must be at least 3 characters");
        if (title.Length > 200) errors.Add("Title too long (max 200 characters)");
    }

    var description = dto.Description?.Trim();
    if (!string.IsNullOrEmpty(description) && description.Length > 500)
        errors.Add("Description too long (max 500 characters)");

    if (errors.Count > 0)
    {
        // 400 Bad Request: client sent invalid data
        // Why 400 exists: client error, request can be fixed by client without server changes
        var message = string.Join("; ", errors);
        return Results.BadRequest(new ErrorResponse(400, "ValidationError", message, traceId));
    }

    var note = new Note
    {
        Id = Guid.NewGuid(),
        UserId = GetUserId(httpContext),
        Title = title!,
        Description = string.IsNullOrWhiteSpace(description) ? null : description,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    // Insert also updates the composite index, costing extra I/O/CPU per write
    await db.Notes.AddAsync(note);
    await db.SaveChangesAsync();
    return Results.Created($"/notes/{note.Id}", note);
});

// GET /notes/{id}
notesGroup.MapGet("/{id:guid}", async (HttpContext httpContext, Guid id, NotesDbContext db) =>
{
    var traceId = httpContext.Items["TraceId"]?.ToString() ?? Guid.NewGuid().ToString();
    var note = await db.Notes.FirstOrDefaultAsync(n => n.Id == id);

    if (note == null)
    {
        // 404 Not Found: resource does not exist
        // Why 404 exists: client requested a resource that isn't present; correct id needed
        return Results.NotFound(new ErrorResponse(404, "NotFound", $"Note with id {id} not found", traceId));
    }

    if (note.UserId != GetUserId(httpContext))
    {
        return Results.Json(
            new ErrorResponse(403, "Forbidden", "You do not have access to this note", traceId),
            statusCode: StatusCodes.Status403Forbidden);
    }

    return Results.Ok(note);
});

// DELETE /notes/{id}
notesGroup.MapDelete("/{id:guid}", async (HttpContext httpContext, Guid id, NotesDbContext db) =>
{
    var traceId = httpContext.Items["TraceId"]?.ToString() ?? Guid.NewGuid().ToString();
    var userId = GetUserId(httpContext);

    var note = await db.Notes.FirstOrDefaultAsync(n => n.Id == id);

    if (note == null)
    {
        // 404 Not Found: resource does not exist
        // Why 404 exists: client tried to delete a resource that isn't present
        return Results.NotFound(new ErrorResponse(404, "NotFound", $"Note with id {id} not found", traceId));
    }

    if (note.UserId != userId)
    {
        return Results.Json(
            new ErrorResponse(403, "Forbidden", "You do not have access to this note", traceId),
            statusCode: StatusCodes.Status403Forbidden);
    }

    db.Notes.Remove(note);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

// Seed endpoint for performance testing
// Inserts 100,000 notes for the authenticated user in batches
// Use with caution: this is a heavy write operation and will increase index maintenance cost
notesGroup.MapPost("/seed", async (HttpContext httpContext, NotesDbContext db) =>
{
    var traceId = httpContext.Items["TraceId"]?.ToString() ?? Guid.NewGuid().ToString();
    var userId = GetUserId(httpContext);
    const int totalCount = 100_000;
    const int batchSize = 1_000;

    // Avoid re-seeding in demo
    var existing = await db.Notes.CountAsync(n => n.UserId == userId);
    if (existing >= totalCount)
    {
        return Results.BadRequest(new ErrorResponse(400, "ValidationError", $"User already has {existing} notes. Remove notes before re-seeding.", traceId));
    }

    var now = DateTime.UtcNow;
    var sw = Stopwatch.StartNew();
    int inserted = 0;

    for (int i = 0; i < totalCount; i += batchSize)
    {
        var batch = new List<Note>();
        int currentBatch = Math.Min(batchSize, totalCount - i);
        for (int j = 0; j < currentBatch; j++)
        {
            int idx = i + j;
            batch.Add(new Note
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Title = $"Seed Note {idx}",
                Description = $"Auto-generated seed note {idx} for performance testing",
                CreatedAt = now.AddSeconds(-idx),
                UpdatedAt = now
            });
        }
        await db.Notes.AddRangeAsync(batch);
        await db.SaveChangesAsync();
        inserted += batch.Count;
    }

    sw.Stop();
    return Results.Ok(new { seeded = inserted, elapsedMs = sw.ElapsedMilliseconds, message = "Seeding complete. Use GET /notes/benchmark to measure query time." });
});

// Benchmark endpoint to measure query performance
// Shows effect of composite index on UserId + CreatedAt
notesGroup.MapGet("/benchmark", async (HttpContext httpContext, NotesDbContext db) =>
{
    var userId = GetUserId(httpContext);
    
    // Warm up
    await db.Notes.AnyAsync(n => n.UserId == userId);

    var sw = Stopwatch.StartNew();
    var total = await db.Notes.CountAsync(n => n.UserId == userId);
    var sample = await db.Notes
        .Where(n => n.UserId == userId)
        .OrderByDescending(n => n.CreatedAt)
        .Take(10)
        .ToListAsync();
    sw.Stop();

    // Without index: SQLite does full table scan + sort, query time grows with table size
    // With IX_Notes_UserId_CreatedAt: index seek + ordered scan, query time stays low even at 100k+ rows
    // Cost: each write updates the index B-tree, increasing write latency and storage
    return Results.Ok(new
    {
        elapsedMs = sw.ElapsedMilliseconds,
        totalNotes = total,
        sampleReturned = sample.Count,
        indexUsed = "IX_Notes_UserId_CreatedAt",
        note = "Measure before/after adding index by creating migration without index, test, then add index migration and re-test."
    });
});

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

static Guid GetUserId(HttpContext httpContext)
{
    var id = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? httpContext.User.FindFirstValue(JwtRegisteredClaimNames.Sub);
    return Guid.Parse(id!);
}

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
