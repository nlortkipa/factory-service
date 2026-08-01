using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using FactoryService.Api.Contracts;
using FactoryService.Api.Data;
using FactoryService.Api.Domain;
using FactoryService.Api.Security;
using FactoryService.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FactoryService.Api.Endpoints;

public static class FactoryServiceEndpoints
{
    public static IEndpointRouteBuilder MapFactoryServiceApi(this IEndpointRouteBuilder app)
    {
        MapAuth(app.MapGroup("/api/auth"));
        MapAgent(app.MapGroup("/api/agent"));
        MapSurvey(app.MapGroup("/api/survey").RequireRateLimiting("public"));
        MapAdmin(app.MapGroup("/api/admin"));
        return app;
    }

    private static void MapAuth(RouteGroupBuilder group)
    {
        group.MapPost("/login", async (LoginRequest request, FactoryDbContext db, ILogger<Program> logger, HttpContext context) =>
        {
            string username = request.Username.Trim().ToLowerInvariant();
            PortalUser? user = await db.PortalUsers.SingleOrDefaultAsync(x => x.Username.ToLower() == username && x.IsActive);
            if (user is null || !SecureHash.VerifyPassword(request.Password, user.PasswordHash, user.PasswordSalt))
            {
                await AuditAsync(db, null, "LoginFailure", "PortalUser", username, "", "", context);
                return Results.Unauthorized();
            }

            string code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
            var challenge = new MfaChallenge
            {
                UserId = user.Id, PhoneMasked = MaskPhone(user.Phone), CodeHash = SecureHash.Sha256(code),
                CreatedAt = DateTimeOffset.UtcNow, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5)
            };
            db.MfaChallenges.Add(challenge);
            await db.SaveChangesAsync();
            logger.LogWarning("[SMS DEV] MFA code for {Username} ({Phone}) is {Code}. This is logged instead of being sent.", user.Username, user.Phone, code);
            return Results.Ok(new { challengeId = challenge.Id, challenge.PhoneMasked, challenge.ExpiresAt });
        });

        group.MapPost("/verify", async (VerifyMfaRequest request, FactoryDbContext db, HttpContext context) =>
        {
            MfaChallenge? challenge = await db.MfaChallenges.Include(x => x.User).SingleOrDefaultAsync(x => x.Id == request.ChallengeId);
            if (challenge?.User is null || challenge.VerifiedAt is not null || challenge.ExpiresAt <= DateTimeOffset.UtcNow || challenge.Attempts >= 5)
                return Results.BadRequest(new { message = "კოდი არასწორია ან ვადა გაუვიდა." });
            challenge.Attempts++;
            if (!SecureHash.FixedEquals(challenge.CodeHash, SecureHash.Sha256(request.Code.Trim())))
            {
                await db.SaveChangesAsync();
                await AuditAsync(db, challenge.UserId, "MfaFailure", "MfaChallenge", challenge.Id.ToString(), "", "", context);
                return Results.BadRequest(new { message = "კოდი არასწორია ან ვადა გაუვიდა.", attemptsRemaining = 5 - challenge.Attempts });
            }

            challenge.VerifiedAt = DateTimeOffset.UtcNow;
            string token = SecureHash.RandomToken();
            db.PortalSessions.Add(new PortalSession
            {
                UserId = challenge.UserId, TokenHash = SecureHash.Sha256(token), CreatedAt = DateTimeOffset.UtcNow,
                LastSeen = DateTimeOffset.UtcNow, ExpiresAt = DateTimeOffset.UtcNow.AddHours(8)
            });
            await db.SaveChangesAsync();
            await AuditAsync(db, challenge.UserId, "LoginMfaSuccess", "PortalUser", challenge.UserId.ToString(), "", "", context);
            return Results.Ok(new { token, user = UserDto(challenge.User) });
        });

        group.MapGet("/me", async (ClaimsPrincipal principal, FactoryDbContext db) =>
        {
            PortalUser user = await db.PortalUsers.Include(x => x.Plant).SingleAsync(x => x.Id == principal.UserId());
            return Results.Ok(UserDto(user));
        }).RequireAuthorization();

        group.MapPost("/logout", async (HttpRequest request, FactoryDbContext db) =>
        {
            string header = request.Headers.Authorization.ToString();
            if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                string hash = SecureHash.Sha256(header[7..].Trim());
                await db.PortalSessions.Where(x => x.TokenHash == hash).ExecuteDeleteAsync();
            }
            return Results.NoContent();
        }).RequireAuthorization();
    }

    private static void MapAgent(RouteGroupBuilder group)
    {
        group.MapPost("/qr", async (AgentQrRequest request, HttpRequest httpRequest, FactoryDbContext db, QrTokenService tokens, IOptions<FactoryOptions> options) =>
        {
            Workstation? workstation = await AuthenticateAgentAsync(request.DeviceId, httpRequest, db);
            if (workstation is null || !workstation.IsActive) return Results.Unauthorized();
            string user = request.WindowsUsername.Trim().ToLowerInvariant();
            FactoryOperator? factoryOperator = await db.Operators.Include(x => x.Plant)
                .SingleOrDefaultAsync(x => x.IsActive && x.AdUsername.ToLower() == user && x.PlantId == workstation.PlantId);

            workstation.ActiveWindowsUser = request.WindowsUsername;
            workstation.ClientVersion = request.ClientVersion;
            workstation.LastSeen = DateTimeOffset.UtcNow;
            workstation.Status = factoryOperator is null ? WorkstationStatus.Warning : WorkstationStatus.Online;
            workstation.LastErrorCode = factoryOperator is null ? "OPERATOR_NOT_MAPPED" : "";
            if (factoryOperator is null)
            {
                await db.SaveChangesAsync();
                return Results.NotFound(new { code = "OPERATOR_NOT_MAPPED", message = "შეფასება დროებით მიუწვდომელია", helpdeskReference = $"USR-{workstation.Id.ToString()[..8].ToUpperInvariant()}" });
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;
            var session = new QrSession
            {
                OperatorId = factoryOperator.Id, PlantId = factoryOperator.PlantId, WorkstationId = workstation.Id,
                IssuedAt = now, ExpiresAt = now.AddMinutes(options.Value.QrLifetimeMinutes), Nonce = SecureHash.RandomToken(16),
                TokenHash = "pending", IsTest = request.IsTest
            };
            string token = tokens.Create(new QrTokenPayload(session.Id, session.OperatorId, session.PlantId, session.WorkstationId,
                now.ToUnixTimeSeconds(), session.ExpiresAt.ToUnixTimeSeconds(), session.Nonce));
            session.TokenHash = SecureHash.Sha256(token);
            db.QrSessions.Add(session);
            workstation.LastQrRenderedAt = now;
            await db.SaveChangesAsync();
            return Results.Ok(new
            {
                token,
                surveyUrl = $"{PublicUrlResolver.Resolve(options.Value.PublicBaseUrl)}/s/{Uri.EscapeDataString(token)}",
                expiresAt = session.ExpiresAt,
                operatorName = string.IsNullOrWhiteSpace(factoryOperator.PublicDisplayName) ? factoryOperator.DisplayName : factoryOperator.PublicDisplayName,
                plantName = factoryOperator.Plant!.Name,
                terminal = workstation.TerminalNumber
            });
        });

        group.MapPost("/heartbeat", async (AgentHeartbeatRequest request, HttpRequest httpRequest, FactoryDbContext db) =>
        {
            Workstation? workstation = await AuthenticateAgentAsync(request.DeviceId, httpRequest, db);
            if (workstation is null) return Results.Unauthorized();
            workstation.ComputerName = request.ComputerName;
            workstation.ActiveWindowsUser = request.WindowsUsername;
            workstation.ClientVersion = request.ClientVersion;
            workstation.LastSeen = DateTimeOffset.UtcNow;
            workstation.LastQrRenderedAt = request.LastRenderAt;
            workstation.LastErrorCode = request.ErrorCode ?? "";
            workstation.Status = Enum.TryParse(request.Status, true, out WorkstationStatus status) ? status : WorkstationStatus.Warning;
            await db.SaveChangesAsync();
            return Results.Ok(new { nextHeartbeatSeconds = 60 });
        });
    }

    private static void MapSurvey(RouteGroupBuilder group)
    {
        group.MapGet("/{token}", async (string token, HttpContext context, FactoryDbContext db, QrTokenService tokenService, IOptions<FactoryOptions> options) =>
        {
            if (!tokenService.TryValidate(token, out QrTokenPayload? payload) || payload is null)
                return Results.BadRequest(new { code = "QR_EXPIRED", message = "QR კოდს ვადა გაუვიდა. გთხოვთ თავიდან დაასკანეროთ ეკრანი." });
            QrSession? session = await db.QrSessions.Include(x => x.Operator).Include(x => x.Plant).Include(x => x.Workstation)
                .SingleOrDefaultAsync(x => x.Id == payload.SessionId && x.TokenHash == SecureHash.Sha256(token));
            if (session is null || session.Status is QrSessionStatus.Revoked or QrSessionStatus.Expired)
                return Results.BadRequest(new { code = "QR_INVALID", message = "QR კოდი აღარ არის მოქმედი." });

            string deviceToken = GetOrSetDeviceToken(context, options.Value.DeviceCookieSecure);
            string deviceHash = SecureHash.Sha256(deviceToken);
            RespondentDevice? device = await db.RespondentDevices.SingleOrDefaultAsync(x => x.DeviceTokenHash == deviceHash);
            DateTimeOffset? last = device is null ? null : await db.SurveyResponses.Where(x => x.RespondentDeviceId == device.Id && !x.IsTest).MaxAsync(x => (DateTimeOffset?)x.SubmittedAt);
            DateTimeOffset? next = last?.AddDays(options.Value.RepeatWindowDays);
            bool blocked = next > DateTimeOffset.UtcNow;
            if (session.OpenedAt is null)
            {
                session.OpenedAt = DateTimeOffset.UtcNow;
                session.SurveyExpiresAt = DateTimeOffset.UtcNow.AddMinutes(options.Value.SurveyLifetimeMinutes);
                await db.SaveChangesAsync();
            }
            string privacy = await db.Settings.Where(x => x.Key == "privacyNotice").Select(x => x.Value).SingleOrDefaultAsync()
                ?? "შეფასების სიხშირის კონტროლისთვის ინახება ანონიმური ტექნიკური ნიშანი.";
            return Results.Ok(new
            {
                sessionId = session.Id, plant = session.Plant!.Name,
                operatorName = string.IsNullOrWhiteSpace(session.Operator!.PublicDisplayName) ? session.Operator.DisplayName : session.Operator.PublicDisplayName,
                terminal = session.Workstation!.TerminalNumber, surveyExpiresAt = session.SurveyExpiresAt,
                blocked, nextAllowedAt = blocked ? next : null, privacyNotice = privacy
            });
        });

        group.MapPost("/{token}", async (string token, SurveySubmitRequest request, HttpContext context, FactoryDbContext db, QrTokenService tokenService, IOptions<FactoryOptions> options) =>
        {
            if (!tokenService.TryValidate(token, out QrTokenPayload? payload, requireUnexpired: false) || payload is null)
                return Results.BadRequest(new { code = "QR_INVALID", message = "QR კოდი არასწორია." });
            QrSession? session = await db.QrSessions.Include(x => x.Operator).Include(x => x.Plant)
                .SingleOrDefaultAsync(x => x.Id == payload.SessionId && x.TokenHash == SecureHash.Sha256(token));
            if (session?.SurveyExpiresAt is null || session.SurveyExpiresAt <= DateTimeOffset.UtcNow || session.Status != QrSessionStatus.Active)
                return Results.BadRequest(new { code = "SURVEY_EXPIRED", message = "შეფასების სესიას ვადა გაუვიდა. თავიდან დაასკანერეთ QR კოდი." });
            if (request.Ratings.Count != 3 || request.Ratings.Select(x => x.ServiceType).Distinct().Count() != 3 ||
                request.Ratings.Any(x => x.Score is < 1 or > 5 || (x.Comment?.Length ?? 0) > 1000))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["ratings"] = ["სამივე შეფასება, 1-დან 5 ქულამდე, სავალდებულოა."] });

            SurveyResponse? duplicate = await db.SurveyResponses.SingleOrDefaultAsync(x => x.IdempotencyKey == request.IdempotencyKey);
            if (duplicate is not null) return Results.Ok(new { responseId = duplicate.Id, duplicate = true, nextAllowedAt = duplicate.SubmittedAt.AddDays(options.Value.RepeatWindowDays) });

            string deviceHash = SecureHash.Sha256(GetOrSetDeviceToken(context, options.Value.DeviceCookieSecure));
            RespondentDevice? device = await db.RespondentDevices.SingleOrDefaultAsync(x => x.DeviceTokenHash == deviceHash);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (device is not null)
            {
                DateTimeOffset? last = await db.SurveyResponses.Where(x => x.RespondentDeviceId == device.Id && !x.IsTest).MaxAsync(x => (DateTimeOffset?)x.SubmittedAt);
                if (last?.AddDays(options.Value.RepeatWindowDays) > now)
                {
                    db.RateLimitDecisions.Add(new RateLimitDecision { KeyHash = deviceHash, Scope = "Global", WindowStart = last.Value, WindowEnd = last.Value.AddDays(options.Value.RepeatWindowDays), Decision = "Blocked", Reason = "Completed response exists in rolling window" });
                    await db.SaveChangesAsync();
                    return Results.Json(new { code = "REPEAT_BLOCKED", message = "შეფასება უკვე დაფიქსირებულია.", nextAllowedAt = last.Value.AddDays(options.Value.RepeatWindowDays) }, statusCode: 429);
                }
                device.LastSeen = now;
            }
            else
            {
                device = new RespondentDevice { DeviceTokenHash = deviceHash, FirstSeen = now, LastSeen = now };
                db.RespondentDevices.Add(device);
            }

            await using var transaction = await db.Database.BeginTransactionAsync();
            var response = new SurveyResponse
            {
                QrSessionId = session.Id, RespondentDevice = device, SubmittedAt = now, PlantId = session.PlantId,
                OperatorId = session.OperatorId, WorkstationId = session.WorkstationId,
                IdempotencyKey = request.IdempotencyKey, IsTest = session.IsTest
            };
            foreach (RatingInput rating in request.Ratings)
            {
                response.Ratings.Add(new Rating
                {
                    ServiceType = rating.ServiceType, Score = rating.Score, Comment = CleanComment(rating.Comment),
                    AttributionType = rating.ServiceType == ServiceType.Sales ? "Operator" : "Plant",
                    OperatorId = rating.ServiceType == ServiceType.Sales ? session.OperatorId : null, PlantId = session.PlantId
                });
            }
            db.SurveyResponses.Add(response);
            session.Status = QrSessionStatus.Completed;
            await db.SaveChangesAsync(); await transaction.CommitAsync();
            return Results.Ok(new { responseId = response.Id, duplicate = false, nextAllowedAt = now.AddDays(options.Value.RepeatWindowDays), message = "გმადლობთ შეფასებისთვის!" });
        });
    }

    private static void MapAdmin(RouteGroupBuilder group)
    {
        group.MapGet("/dashboard", DashboardAsync).RequireAuthorization("Reports");
        group.MapGet("/responses", ResponsesAsync).RequireAuthorization("Reports");
        group.MapGet("/exports/{format}", ExportAsync).RequireAuthorization("Reports");

        group.MapGet("/plants", async (FactoryDbContext db, ClaimsPrincipal user) =>
        {
            IQueryable<Plant> query = db.Plants.AsNoTracking();
            if (user.PlantScope() is Guid scope) query = query.Where(x => x.Id == scope);
            return Results.Ok(await query.OrderBy(x => x.Name).ToListAsync());
        }).RequireAuthorization("Reports");
        group.MapPost("/plants", async (PlantInput input, FactoryDbContext db, ClaimsPrincipal user, HttpContext context) =>
        {
            if (!user.IsSystemAdmin()) return Results.Forbid();
            var entity = new Plant { Code = input.Code.Trim(), Name = input.Name.Trim(), Address = input.Address.Trim(), TimeZone = input.TimeZone.Trim(), IsActive = input.IsActive };
            db.Plants.Add(entity); await db.SaveChangesAsync(); await AuditAsync(db, user.UserId(), "Create", "Plant", entity.Id.ToString(), "", JsonSerializer.Serialize(entity), context); return Results.Created($"/api/admin/plants/{entity.Id}", entity);
        }).RequireAuthorization("Manage");
        group.MapPut("/plants/{id:guid}", async (Guid id, PlantInput input, FactoryDbContext db, ClaimsPrincipal user, HttpContext context) =>
        {
            Plant? entity = await db.Plants.FindAsync(id); if (entity is null) return Results.NotFound(); if (!user.IsSystemAdmin() && user.PlantScope() != id) return Results.Forbid(); string before = JsonSerializer.Serialize(entity);
            entity.Code = input.Code.Trim(); entity.Name = input.Name.Trim(); entity.Address = input.Address.Trim(); entity.TimeZone = input.TimeZone.Trim(); entity.IsActive = input.IsActive;
            await db.SaveChangesAsync(); await AuditAsync(db, user.UserId(), "Update", "Plant", id.ToString(), before, JsonSerializer.Serialize(entity), context); return Results.Ok(entity);
        }).RequireAuthorization("Manage");
        group.MapDelete("/plants/{id:guid}", async (Guid id, FactoryDbContext db, ClaimsPrincipal user, HttpContext context) =>
        {
            if (!user.IsSystemAdmin()) return Results.Forbid(); Plant? entity = await db.Plants.FindAsync(id); if (entity is null) return Results.NotFound();
            entity.IsActive = false; await db.SaveChangesAsync(); await AuditAsync(db, user.UserId(), "Deactivate", "Plant", id.ToString(), "active", "inactive", context); return Results.NoContent();
        }).RequireAuthorization("Manage");

        group.MapGet("/operators", async (FactoryDbContext db, ClaimsPrincipal user) =>
        {
            IQueryable<FactoryOperator> query = db.Operators.AsNoTracking().Include(x => x.Plant).Include(x => x.Workstation);
            if (user.PlantScope() is Guid scope) query = query.Where(x => x.PlantId == scope);
            return Results.Ok(await query.OrderBy(x => x.DisplayName).Select(x => new { x.Id, x.AdUsername, x.DisplayName, x.PublicDisplayName, x.PlantId, PlantName = x.Plant!.Name, x.WorkstationId, Workstation = x.Workstation == null ? "" : x.Workstation.ComputerName, x.IsActive }).ToListAsync());
        }).RequireAuthorization("Reports");
        group.MapPost("/operators", async (OperatorInput input, FactoryDbContext db, ClaimsPrincipal user, HttpContext context) =>
        {
            if (!CanAccessPlant(user, input.PlantId)) return Results.Forbid();
            var entity = new FactoryOperator { AdUsername = input.AdUsername.Trim(), DisplayName = input.DisplayName.Trim(), PublicDisplayName = input.PublicDisplayName.Trim(), PlantId = input.PlantId, WorkstationId = input.WorkstationId, IsActive = input.IsActive };
            db.Operators.Add(entity); await db.SaveChangesAsync(); await AuditAsync(db, user.UserId(), "Create", "Operator", entity.Id.ToString(), "", JsonSerializer.Serialize(entity), context); return Results.Created($"/api/admin/operators/{entity.Id}", entity);
        }).RequireAuthorization("Manage");
        group.MapPut("/operators/{id:guid}", async (Guid id, OperatorInput input, FactoryDbContext db, ClaimsPrincipal user, HttpContext context) =>
        {
            FactoryOperator? entity = await db.Operators.FindAsync(id); if (entity is null) return Results.NotFound(); if (!CanAccessPlant(user, entity.PlantId) || !CanAccessPlant(user, input.PlantId)) return Results.Forbid(); string before = JsonSerializer.Serialize(entity);
            entity.AdUsername = input.AdUsername.Trim(); entity.DisplayName = input.DisplayName.Trim(); entity.PublicDisplayName = input.PublicDisplayName.Trim(); entity.PlantId = input.PlantId; entity.WorkstationId = input.WorkstationId; entity.IsActive = input.IsActive;
            await db.SaveChangesAsync(); await AuditAsync(db, user.UserId(), "Update", "Operator", id.ToString(), before, JsonSerializer.Serialize(entity), context); return Results.Ok(entity);
        }).RequireAuthorization("Manage");
        group.MapDelete("/operators/{id:guid}", async (Guid id, FactoryDbContext db, ClaimsPrincipal user, HttpContext context) =>
        {
            FactoryOperator? entity = await db.Operators.FindAsync(id); if (entity is null) return Results.NotFound(); if (!CanAccessPlant(user, entity.PlantId)) return Results.Forbid();
            entity.IsActive = false; entity.EffectiveTo = DateTimeOffset.UtcNow; await db.SaveChangesAsync(); await AuditAsync(db, user.UserId(), "Deactivate", "Operator", id.ToString(), "active", "inactive", context); return Results.NoContent();
        }).RequireAuthorization("Manage");

        group.MapGet("/workstations", async (FactoryDbContext db, ClaimsPrincipal user) =>
        {
            IQueryable<Workstation> query = db.Workstations.AsNoTracking().Include(x => x.Plant);
            if (user.PlantScope() is Guid scope) query = query.Where(x => x.PlantId == scope);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            var items = await query.OrderBy(x => x.ComputerName).ToListAsync();
            return Results.Ok(items.Select(x => new { x.Id, x.DeviceId, x.ComputerName, x.PlantId, PlantName = x.Plant!.Name, x.TerminalNumber, x.ScreenModel, x.Serial, x.ClientVersion, x.ActiveWindowsUser, x.LastSeen, x.LastQrRenderedAt, Status = EffectiveStatus(x, now), x.LastErrorCode, x.IsActive }));
        }).RequireAuthorization("Reports");
        group.MapPost("/workstations", async (WorkstationInput input, FactoryDbContext db, ClaimsPrincipal user, HttpContext context) =>
        {
            if (!CanAccessPlant(user, input.PlantId)) return Results.Forbid();
            var entity = new Workstation { DeviceId = input.DeviceId.Trim(), ComputerName = input.ComputerName.Trim(), PlantId = input.PlantId, TerminalNumber = input.TerminalNumber.Trim(), ScreenModel = input.ScreenModel.Trim(), Serial = input.Serial.Trim(), AgentKeyHash = SecureHash.Sha256(input.AgentKey), IsActive = input.IsActive };
            db.Workstations.Add(entity); await db.SaveChangesAsync(); await AuditAsync(db, user.UserId(), "Create", "Workstation", entity.Id.ToString(), "", "created", context); return Results.Created($"/api/admin/workstations/{entity.Id}", new { entity.Id });
        }).RequireAuthorization("Manage");
        group.MapPut("/workstations/{id:guid}", async (Guid id, WorkstationInput input, FactoryDbContext db, ClaimsPrincipal user, HttpContext context) =>
        {
            Workstation? entity = await db.Workstations.FindAsync(id); if (entity is null) return Results.NotFound(); if (!CanAccessPlant(user, entity.PlantId) || !CanAccessPlant(user, input.PlantId)) return Results.Forbid();
            entity.DeviceId = input.DeviceId.Trim(); entity.ComputerName = input.ComputerName.Trim(); entity.PlantId = input.PlantId; entity.TerminalNumber = input.TerminalNumber.Trim(); entity.ScreenModel = input.ScreenModel.Trim(); entity.Serial = input.Serial.Trim(); entity.IsActive = input.IsActive; if (!string.IsNullOrWhiteSpace(input.AgentKey)) entity.AgentKeyHash = SecureHash.Sha256(input.AgentKey);
            await db.SaveChangesAsync(); await AuditAsync(db, user.UserId(), "Update", "Workstation", id.ToString(), "", "updated", context); return Results.Ok(new { entity.Id });
        }).RequireAuthorization("Manage");
        group.MapDelete("/workstations/{id:guid}", async (Guid id, FactoryDbContext db, ClaimsPrincipal user, HttpContext context) =>
        {
            Workstation? entity = await db.Workstations.FindAsync(id); if (entity is null) return Results.NotFound(); if (!CanAccessPlant(user, entity.PlantId)) return Results.Forbid();
            entity.IsActive = false; entity.Status = WorkstationStatus.Offline; await db.QrSessions.Where(x => x.WorkstationId == id && x.Status == QrSessionStatus.Active).ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Status, QrSessionStatus.Revoked));
            await db.SaveChangesAsync(); await AuditAsync(db, user.UserId(), "Deactivate", "Workstation", id.ToString(), "active", "inactive", context); return Results.NoContent();
        }).RequireAuthorization("Manage");

        group.MapGet("/settings", async (FactoryDbContext db) => Results.Ok(await db.Settings.AsNoTracking().ToDictionaryAsync(x => x.Key, x => x.Value))).RequireAuthorization("SystemAdmin");
        group.MapPut("/settings", async (Dictionary<string, string> values, FactoryDbContext db, ClaimsPrincipal user, HttpContext context) =>
        {
            foreach ((string key, string value) in values)
            {
                ApplicationSetting? setting = await db.Settings.FindAsync(key);
                if (setting is null) db.Settings.Add(new ApplicationSetting { Key = key, Value = value, UpdatedAt = DateTimeOffset.UtcNow, UpdatedBy = user.UserId() });
                else { setting.Value = value; setting.UpdatedAt = DateTimeOffset.UtcNow; setting.UpdatedBy = user.UserId(); }
            }
            await db.SaveChangesAsync(); await AuditAsync(db, user.UserId(), "Update", "ApplicationSettings", "global", "", JsonSerializer.Serialize(values), context); return Results.NoContent();
        }).RequireAuthorization("SystemAdmin");
        group.MapGet("/audit", async (FactoryDbContext db, ClaimsPrincipal user, int take = 100) =>
        {
            IQueryable<AuditLog> query = db.AuditLogs.AsNoTracking();
            return Results.Ok(await query.OrderByDescending(x => x.Timestamp).Take(Math.Clamp(take, 1, 500)).ToListAsync());
        }).RequireAuthorization("Reports");
    }

    private static async Task<IResult> DashboardAsync(FactoryDbContext db, ClaimsPrincipal user, DateTimeOffset? from, DateTimeOffset? to, Guid? plantId)
    {
        DateTimeOffset start = from ?? DateTimeOffset.UtcNow.AddDays(-30); DateTimeOffset end = to ?? DateTimeOffset.UtcNow.AddDays(1);
        if (user.PlantScope() is Guid scope) plantId = scope;
        IQueryable<SurveyResponse> baseQuery = db.SurveyResponses.AsNoTracking().Where(x => !x.IsTest && x.SubmittedAt >= start && x.SubmittedAt < end);
        if (plantId is Guid selected) baseQuery = baseQuery.Where(x => x.PlantId == selected);
        var responses = await baseQuery.Include(x => x.Plant).Include(x => x.Ratings).ToListAsync();
        int total = responses.Count; int uniqueDevices = responses.Select(x => x.RespondentDeviceId).Distinct().Count();
        var ratings = responses.SelectMany(x => x.Ratings).ToList();
        object Metric(ServiceType type) { var values = ratings.Where(x => x.ServiceType == type).ToList(); return new { average = values.Count == 0 ? 0 : Math.Round(values.Average(x => x.Score), 2), count = values.Count, lowPercent = values.Count == 0 ? 0 : Math.Round(values.Count(x => x.Score <= 2) * 100d / values.Count, 1), distribution = Enumerable.Range(1, 5).Select(score => new { score, count = values.Count(x => x.Score == score) }) }; }
        var trend = responses.GroupBy(x => DateOnly.FromDateTime(x.SubmittedAt.LocalDateTime.Date)).OrderBy(x => x.Key).Select(g => new { date = g.Key, average = Math.Round(g.SelectMany(x => x.Ratings).Average(x => x.Score), 2), count = g.Count() });
        var plants = responses.GroupBy(x => new { x.PlantId, x.Plant!.Name }).Select(g => new { id = g.Key.PlantId, name = g.Key.Name, average = Math.Round(g.SelectMany(x => x.Ratings).Average(x => x.Score), 2), count = g.Count() }).OrderByDescending(x => x.average);
        DateTimeOffset now = DateTimeOffset.UtcNow; var workstations = await db.Workstations.AsNoTracking().ToListAsync();
        var health = Enum.GetValues<WorkstationStatus>().Select(status => new { status, count = workstations.Count(x => EffectiveStatus(x, now) == status) });
        int blocked = await db.RateLimitDecisions.CountAsync(x => x.Decision == "Blocked" && x.WindowStart >= start);
        return Results.Ok(new { total, uniqueDevices, sales = Metric(ServiceType.Sales), scale = Metric(ServiceType.Scale), loading = Metric(ServiceType.Loading), trend, plants, health, blockedAttempts = blocked });
    }

    private static async Task<IResult> ResponsesAsync(FactoryDbContext db, ClaimsPrincipal user, DateTimeOffset? from, DateTimeOffset? to, Guid? plantId, Guid? operatorId, ServiceType? serviceType, int? score, string? search, int page = 1, int pageSize = 50)
    {
        IQueryable<Rating> query = BuildReportQuery(db, user, from, to, plantId, operatorId, serviceType, score, search);
        int total = await query.CountAsync();
        var items = await query.OrderByDescending(x => x.SurveyResponse!.SubmittedAt).Skip((Math.Max(1, page) - 1) * Math.Clamp(pageSize, 1, 200)).Take(Math.Clamp(pageSize, 1, 200))
            .Select(x => new { x.Id, x.SurveyResponse!.SubmittedAt, Plant = x.SurveyResponse.Plant!.Name, Operator = x.SurveyResponse.Operator!.DisplayName, Workstation = x.SurveyResponse.Workstation!.ComputerName, x.ServiceType, x.Score, x.Comment }).ToListAsync();
        return Results.Ok(new { total, page = Math.Max(1, page), pageSize = Math.Clamp(pageSize, 1, 200), items });
    }

    private static async Task<IResult> ExportAsync(string format, FactoryDbContext db, ClaimsPrincipal user, HttpContext context, DateTimeOffset? from, DateTimeOffset? to, Guid? plantId, Guid? operatorId, ServiceType? serviceType, int? score, string? search)
    {
        var rows = await BuildReportQuery(db, user, from, to, plantId, operatorId, serviceType, score, search).OrderByDescending(x => x.SurveyResponse!.SubmittedAt)
            .Select(x => new ReportRow(x.SurveyResponse!.SubmittedAt, x.SurveyResponse.Plant!.Name, x.SurveyResponse.Operator!.DisplayName, x.SurveyResponse.Workstation!.ComputerName, x.ServiceType, x.Score, x.Comment)).ToListAsync();
        byte[] bytes; string contentType; string extension;
        switch (format.ToLowerInvariant())
        {
            case "xlsx": bytes = ReportExportService.Xlsx(rows); contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"; extension = "xlsx"; break;
            case "pdf": bytes = ReportExportService.Pdf(rows); contentType = "application/pdf"; extension = "pdf"; break;
            default: bytes = ReportExportService.Csv(rows); contentType = "text/csv; charset=utf-8"; extension = "csv"; break;
        }
        await AuditAsync(db, user.UserId(), "Export", "Report", extension, "", JsonSerializer.Serialize(new { from, to, plantId, operatorId, serviceType, score, search, rows = rows.Count }), context);
        return Results.File(bytes, contentType, $"factory-service-report-{DateTime.Now:yyyyMMdd}.{extension}");
    }

    private static IQueryable<Rating> BuildReportQuery(FactoryDbContext db, ClaimsPrincipal user, DateTimeOffset? from, DateTimeOffset? to, Guid? plantId, Guid? operatorId, ServiceType? serviceType, int? score, string? search)
    {
        DateTimeOffset start = from ?? DateTimeOffset.UtcNow.AddDays(-30), end = to ?? DateTimeOffset.UtcNow.AddDays(1);
        IQueryable<Rating> query = db.Ratings.AsNoTracking().Where(x => !x.SurveyResponse!.IsTest && x.SurveyResponse.SubmittedAt >= start && x.SurveyResponse.SubmittedAt < end);
        if (user.PlantScope() is Guid scope) plantId = scope;
        if (plantId is Guid p) query = query.Where(x => x.PlantId == p);
        if (operatorId is Guid op) query = query.Where(x => x.SurveyResponse!.OperatorId == op && x.ServiceType == ServiceType.Sales);
        if (serviceType is ServiceType service) query = query.Where(x => x.ServiceType == service);
        if (score is >= 1 and <= 5) query = query.Where(x => x.Score == score);
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(x => x.Comment.Contains(search));
        return query;
    }

    private static async Task<Workstation?> AuthenticateAgentAsync(string deviceId, HttpRequest request, FactoryDbContext db)
    {
        string key = request.Headers["X-Agent-Key"].ToString(); if (string.IsNullOrWhiteSpace(key)) return null;
        Workstation? workstation = await db.Workstations.SingleOrDefaultAsync(x => x.DeviceId == deviceId);
        return workstation is not null && SecureHash.FixedEquals(workstation.AgentKeyHash, SecureHash.Sha256(key)) ? workstation : null;
    }

    private static string GetOrSetDeviceToken(HttpContext context, bool secure)
    {
        const string name = "fs_device";
        if (context.Request.Cookies.TryGetValue(name, out string? existing) && !string.IsNullOrWhiteSpace(existing)) return existing;
        string token = SecureHash.RandomToken();
        context.Response.Cookies.Append(name, token, new CookieOptions { HttpOnly = true, Secure = secure, SameSite = SameSiteMode.Lax, IsEssential = true, MaxAge = TimeSpan.FromDays(400), Path = "/" });
        return token;
    }

    private static string CleanComment(string? value) => Regex.Replace(value?.Trim() ?? "", "<[^>]+>", "").Replace("javascript:", "", StringComparison.OrdinalIgnoreCase);
    private static string MaskPhone(string phone) => phone.Length < 7 ? "***" : $"{phone[..4]}•••{phone[^3..]}";
    private static bool CanAccessPlant(ClaimsPrincipal user, Guid plantId) => user.IsSystemAdmin() || user.PlantScope() == plantId;
    private static WorkstationStatus EffectiveStatus(Workstation workstation, DateTimeOffset now) => workstation.LastSeen is null || now - workstation.LastSeen > TimeSpan.FromMinutes(15) ? WorkstationStatus.Offline : now - workstation.LastSeen > TimeSpan.FromMinutes(5) ? WorkstationStatus.Warning : workstation.Status;
    private static object UserDto(PortalUser user) => new { user.Id, user.Username, user.DisplayName, role = user.Role, user.PlantId, plant = user.Plant?.Name };

    private static async Task AuditAsync(FactoryDbContext db, Guid? actorId, string action, string objectType, string objectId, string before, string after, HttpContext context)
    {
        db.AuditLogs.Add(new AuditLog { ActorId = actorId, Action = action, ObjectType = objectType, ObjectId = objectId, BeforeJson = before, AfterJson = after, IpAddress = context.Connection.RemoteIpAddress?.ToString() ?? "", Timestamp = DateTimeOffset.UtcNow, CorrelationId = context.TraceIdentifier });
        await db.SaveChangesAsync();
    }
}
