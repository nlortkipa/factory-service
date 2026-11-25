using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
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
        group.MapPost("/login", async (FactoryDbContext db, SmsGateway smsGateway, ILogger<Program> logger) =>
        {
            PortalUser? user = await db.PortalUsers
                .OrderBy(x => x.Username)
                .FirstOrDefaultAsync(x => x.IsActive && x.Role == PortalRole.SystemAdministrator);
            if (user is null)
                return Results.Problem("აქტიური ადმინისტრატორი ვერ მოიძებნა.", statusCode: StatusCodes.Status503ServiceUnavailable);

            string code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
            var challenge = new MfaChallenge
            {
                UserId = user.Id, PhoneMasked = MaskPhone(user.Phone), CodeHash = SecureHash.Sha256(code),
                CreatedAt = DateTimeOffset.UtcNow, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5), SendStatus = "Sent"
            };
            try
            {
                await smsGateway.SendCodeAsync(user.Phone, code);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Failed to send an administrator MFA code through the SMS provider.");
                return Results.Problem("SMS კოდის გაგზავნა ვერ მოხერხდა. გთხოვთ სცადოთ ხელახლა.", statusCode: StatusCodes.Status502BadGateway);
            }
            db.MfaChallenges.Add(challenge);
            await db.SaveChangesAsync();
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
        group.MapPost("/qr", async (AgentQrRequest request, FactoryDbContext db, QrTokenService tokens, IOptions<FactoryOptions> options) =>
        {
            string operatorId = request.OperatorId?.Trim() ?? "";
            if (operatorId.Length is 0 or > 64)
                return Results.BadRequest(new { code = "INVALID_OPERATOR_ID", message = "მიუთითეთ სწორი ოპერატორის ID." });
            FactoryOperator? factoryOperator = await db.Operators.Include(x => x.Plant)
                .SingleOrDefaultAsync(x => x.OperatorId == operatorId && x.IsActive && x.Plant!.IsActive);
            if (factoryOperator is null)
                return Results.NotFound(new { code = "OPERATOR_NOT_FOUND", message = "ოპერატორი ვერ მოიძებნა." });

            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (!string.IsNullOrEmpty(request.CurrentToken)
                && tokens.TryValidate(request.CurrentToken, out QrTokenPayload? current) && current is not null)
            {
                QrSession? existing = await db.QrSessions.SingleOrDefaultAsync(x => x.Id == current.SessionId
                    && x.TokenHash == SecureHash.Sha256(request.CurrentToken));
                if (existing is not null && existing.Status == QrSessionStatus.Active && existing.ExpiresAt > now
                    && existing.OperatorId == factoryOperator.Id && existing.PlantId == factoryOperator.PlantId)
                    return Results.Ok(new
                    {
                        token = request.CurrentToken,
                        surveyUrl = $"{PublicUrlResolver.Resolve(options.Value.PublicBaseUrl)}/s/{Uri.EscapeDataString(request.CurrentToken)}",
                        expiresAt = existing.ExpiresAt,
                        operatorName = factoryOperator.DisplayName,
                        plantName = factoryOperator.Plant!.Name
                    });
            }
            var session = new QrSession
            {
                OperatorId = factoryOperator.Id, PlantId = factoryOperator.PlantId,
                IssuedAt = now, ExpiresAt = now.AddMinutes(options.Value.QrLifetimeMinutes), Nonce = SecureHash.RandomToken(16),
                TokenHash = "pending"
            };
            string token = tokens.Create(new QrTokenPayload(session.Id, session.OperatorId, session.PlantId,
                now.ToUnixTimeSeconds(), session.ExpiresAt.ToUnixTimeSeconds(), session.Nonce));
            session.TokenHash = SecureHash.Sha256(token);
            db.QrSessions.Add(session);
            await db.SaveChangesAsync();
            return Results.Ok(new
            {
                token,
                surveyUrl = $"{PublicUrlResolver.Resolve(options.Value.PublicBaseUrl)}/s/{Uri.EscapeDataString(token)}",
                expiresAt = session.ExpiresAt,
                operatorName = factoryOperator.DisplayName,
                plantName = factoryOperator.Plant!.Name
            });
        });
    }

    private static void MapSurvey(RouteGroupBuilder group)
    {
        group.MapGet("/{token}", async (string token, HttpContext context, FactoryDbContext db, QrTokenService tokenService, IOptions<FactoryOptions> options) =>
        {
            if (!tokenService.TryValidate(token, out QrTokenPayload? payload) || payload is null)
                return Results.BadRequest(new { code = "QR_EXPIRED", message = "QR კოდს ვადა გაუვიდა. გთხოვთ თავიდან დაასკანეროთ ეკრანი." });
            QrSession? session = await db.QrSessions.Include(x => x.Operator).Include(x => x.Plant)
                .SingleOrDefaultAsync(x => x.Id == payload.SessionId && x.TokenHash == SecureHash.Sha256(token));
            if (session is null || session.Status is QrSessionStatus.Revoked or QrSessionStatus.Expired)
                return Results.BadRequest(new { code = "QR_INVALID", message = "QR კოდი აღარ არის მოქმედი." });

            string deviceToken = GetOrSetDeviceToken(context, options.Value.DeviceCookieSecure);
            string deviceHash = SecureHash.Sha256(deviceToken);
            RespondentDevice? device = await db.RespondentDevices.SingleOrDefaultAsync(x => x.DeviceTokenHash == deviceHash);
            DateTimeOffset? last = device is null ? null : await db.SurveyResponses.Where(x => x.RespondentDeviceId == device.Id && !x.IsTest).MaxAsync(x => (DateTimeOffset?)x.SubmittedAt);
            int repeatWindowDays = await RepeatWindowDaysAsync(db, options.Value);
            DateTimeOffset? next = last?.AddDays(repeatWindowDays);
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
                operatorName = session.Operator!.DisplayName,
                surveyExpiresAt = session.SurveyExpiresAt,
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
            string? carPlate = NormalizeCarPlate(request.CarPlate);
            if (carPlate is null)
                return Results.BadRequest(new { code = "CAR_PLATE_INVALID", message = "ნომერი უნდა იყოს ფორმატით AA123AA ან AA-123-AA." });

            int repeatWindowDays = await RepeatWindowDaysAsync(db, options.Value);
            SurveyResponse? duplicate = await db.SurveyResponses.SingleOrDefaultAsync(x => x.IdempotencyKey == request.IdempotencyKey);
            if (duplicate is not null) return Results.Ok(new { responseId = duplicate.Id, duplicate = true, nextAllowedAt = duplicate.SubmittedAt.AddDays(repeatWindowDays) });

            string deviceHash = SecureHash.Sha256(GetOrSetDeviceToken(context, options.Value.DeviceCookieSecure));
            RespondentDevice? device = await db.RespondentDevices.SingleOrDefaultAsync(x => x.DeviceTokenHash == deviceHash);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (device is not null)
            {
                DateTimeOffset? last = await db.SurveyResponses.Where(x => x.RespondentDeviceId == device.Id && !x.IsTest).MaxAsync(x => (DateTimeOffset?)x.SubmittedAt);
                if (last?.AddDays(repeatWindowDays) > now)
                {
                    db.RateLimitDecisions.Add(new RateLimitDecision { KeyHash = deviceHash, Scope = "Global", WindowStart = last.Value, WindowEnd = last.Value.AddDays(repeatWindowDays), Decision = "Blocked", Reason = "Completed response exists in rolling window" });
                    await db.SaveChangesAsync();
                    return Results.Json(new { code = "REPEAT_BLOCKED", message = "შეფასება უკვე დაფიქსირებულია.", nextAllowedAt = last.Value.AddDays(repeatWindowDays) }, statusCode: 429);
                }
                device.LastSeen = now;
            }
            else
            {
                device = new RespondentDevice { DeviceTokenHash = deviceHash, FirstSeen = now, LastSeen = now };
                db.RespondentDevices.Add(device);
            }

            var response = new SurveyResponse
            {
                QrSessionId = session.Id, RespondentDevice = device, SubmittedAt = now, PlantId = session.PlantId,
                OperatorId = session.OperatorId,
                IdempotencyKey = request.IdempotencyKey, CarPlate = carPlate, IsTest = session.IsTest
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
            await db.SaveChangesAsync();
            return Results.Ok(new { responseId = response.Id, duplicate = false, nextAllowedAt = now.AddDays(repeatWindowDays), message = "გმადლობთ შეფასებისთვის!" });
        });
    }

    private static void MapAdmin(RouteGroupBuilder group)
    {
        group.MapGet("/dashboard", DashboardAsync).RequireAuthorization("Reports");
        group.MapGet("/responses", ResponsesAsync).RequireAuthorization("Reports");
        group.MapDelete("/responses/{id:guid}", async (Guid id, FactoryDbContext db, ClaimsPrincipal user, HttpContext context) =>
        {
            SurveyResponse? entity = await db.SurveyResponses.Include(x => x.Ratings).SingleOrDefaultAsync(x => x.Id == id);
            if (entity is null) return Results.NotFound(new { message = "შეფასება ვერ მოიძებნა." });
            string before = JsonSerializer.Serialize(new { entity.Id, entity.SubmittedAt, entity.PlantId, entity.OperatorId, Ratings = entity.Ratings.Select(x => new { x.ServiceType, x.Score, x.Comment }) });
            db.SurveyResponses.Remove(entity);
            await db.SaveChangesAsync();
            await AuditAsync(db, user.UserId(), "Delete", "SurveyResponse", id.ToString(), before, "", context);
            return Results.NoContent();
        }).RequireAuthorization("SystemAdmin");
        group.MapGet("/exports/{format}", ExportAsync).RequireAuthorization("Reports");

        group.MapGet("/plants", async (FactoryDbContext db, ClaimsPrincipal user) =>
        {
            IQueryable<Plant> query = db.Plants.AsNoTracking().Where(x => x.IsActive);
            if (user.PlantScope() is Guid scope) query = query.Where(x => x.Id == scope);
            return Results.Ok(await query.OrderBy(x => x.Name).Select(x => new { x.Id, x.Name, x.Address, x.TimeZone }).ToListAsync());
        }).RequireAuthorization("Reports");
        group.MapPost("/plants", async (PlantInput input, FactoryDbContext db, ClaimsPrincipal user, HttpContext context) =>
        {
            if (!user.IsSystemAdmin()) return Results.Forbid();
            var entity = new Plant { Name = input.Name.Trim(), Address = input.Address.Trim(), TimeZone = input.TimeZone.Trim() };
            db.Plants.Add(entity); await db.SaveChangesAsync(); await AuditAsync(db, user.UserId(), "Create", "Plant", entity.Id.ToString(), "", JsonSerializer.Serialize(entity), context); return Results.Created($"/api/admin/plants/{entity.Id}", entity);
        }).RequireAuthorization("Manage");
        group.MapPut("/plants/{id:guid}", async (Guid id, PlantInput input, FactoryDbContext db, ClaimsPrincipal user, HttpContext context) =>
        {
            Plant? entity = await db.Plants.FindAsync(id); if (entity is null) return Results.NotFound(); if (!user.IsSystemAdmin() && user.PlantScope() != id) return Results.Forbid(); string before = JsonSerializer.Serialize(entity);
            entity.Name = input.Name.Trim(); entity.Address = input.Address.Trim(); entity.TimeZone = input.TimeZone.Trim();
            await db.SaveChangesAsync(); await AuditAsync(db, user.UserId(), "Update", "Plant", id.ToString(), before, JsonSerializer.Serialize(entity), context); return Results.Ok(entity);
        }).RequireAuthorization("Manage");
        group.MapDelete("/plants/{id:guid}", async (Guid id, FactoryDbContext db, ClaimsPrincipal user, HttpContext context) =>
        {
            if (!user.IsSystemAdmin()) return Results.Forbid(); Plant? entity = await db.Plants.FindAsync(id); if (entity is null || !entity.IsActive) return Results.NotFound();
            bool hasOperators = await db.Operators.AnyAsync(x => x.PlantId == id && x.IsActive);
            if (hasOperators) return Results.Conflict(new { message = "ქარხანა დაკავშირებულია ოპერატორთან და ვერ წაიშლება." });
            string before = JsonSerializer.Serialize(entity); entity.IsActive = false; await db.SaveChangesAsync();
            await AuditAsync(db, user.UserId(), "Delete", "Plant", id.ToString(), before, "", context); return Results.NoContent();
        }).RequireAuthorization("Manage");

        group.MapGet("/operators", async (FactoryDbContext db, ClaimsPrincipal user) =>
        {
            IQueryable<FactoryOperator> query = db.Operators.AsNoTracking().Include(x => x.Plant).Where(x => x.IsActive);
            if (user.PlantScope() is Guid scope) query = query.Where(x => x.PlantId == scope);
            return Results.Ok(await query.OrderBy(x => x.DisplayName).Select(x => new { x.Id, x.OperatorId, x.DisplayName, x.PlantId, PlantName = x.Plant!.Name }).ToListAsync());
        }).RequireAuthorization("Reports");
        group.MapPost("/operators", async (OperatorInput input, FactoryDbContext db, ClaimsPrincipal user, HttpContext context) =>
        {
            if (!CanAccessPlant(user, input.PlantId)) return Results.Forbid();
            var entity = new FactoryOperator { OperatorId = SecureHash.RandomToken(24), DisplayName = input.DisplayName.Trim(), PlantId = input.PlantId };
            db.Operators.Add(entity); await db.SaveChangesAsync(); await AuditAsync(db, user.UserId(), "Create", "Operator", entity.Id.ToString(), "", JsonSerializer.Serialize(entity), context); return Results.Created($"/api/admin/operators/{entity.Id}", entity);
        }).RequireAuthorization("Manage");
        group.MapPut("/operators/{id:guid}", async (Guid id, OperatorInput input, FactoryDbContext db, ClaimsPrincipal user, HttpContext context) =>
        {
            FactoryOperator? entity = await db.Operators.FindAsync(id); if (entity is null) return Results.NotFound(); if (!CanAccessPlant(user, entity.PlantId) || !CanAccessPlant(user, input.PlantId)) return Results.Forbid(); string before = JsonSerializer.Serialize(entity);
            entity.DisplayName = input.DisplayName.Trim(); entity.PlantId = input.PlantId;
            await db.SaveChangesAsync(); await AuditAsync(db, user.UserId(), "Update", "Operator", id.ToString(), before, JsonSerializer.Serialize(entity), context); return Results.Ok(entity);
        }).RequireAuthorization("Manage");
        group.MapDelete("/operators/{id:guid}", async (Guid id, FactoryDbContext db, ClaimsPrincipal user, HttpContext context) =>
        {
            FactoryOperator? entity = await db.Operators.FindAsync(id); if (entity is null || !entity.IsActive) return Results.NotFound(); if (!CanAccessPlant(user, entity.PlantId)) return Results.Forbid();
            string before = JsonSerializer.Serialize(entity);
            entity.IsActive = false;
            entity.EffectiveTo = DateTimeOffset.UtcNow;
            await db.QrSessions.Where(x => x.OperatorId == id && x.Status == QrSessionStatus.Active)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Status, QrSessionStatus.Revoked));
            await db.SaveChangesAsync();
            await AuditAsync(db, user.UserId(), "Delete", "Operator", id.ToString(), before, "", context); return Results.NoContent();
        }).RequireAuthorization("Manage");

        group.MapGet("/settings", async (FactoryDbContext db, IOptions<FactoryOptions> options) =>
        {
            var values = await db.Settings.AsNoTracking().ToDictionaryAsync(x => x.Key, x => x.Value);
            values["repeatWindowDays"] = (await RepeatWindowDaysAsync(db, options.Value)).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return Results.Ok(values);
        }).RequireAuthorization("SystemAdmin");
        group.MapPut("/settings", async (Dictionary<string, string> values, FactoryDbContext db, ClaimsPrincipal user, HttpContext context) =>
        {
            if (values.TryGetValue("repeatWindowDays", out string? days) && !ValidRepeatWindowDays(days, out _))
                return Results.BadRequest(new { message = "მიუთითეთ დღეების მთელი რაოდენობა 0-დან 3650-მდე." });
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

    private static async Task<IResult> DashboardAsync(FactoryDbContext db, ClaimsPrincipal user, DateTimeOffset? from, DateTimeOffset? to, Guid? plantId, Guid? operatorId)
    {
        DateTimeOffset start = from ?? DateTimeOffset.UtcNow.AddDays(-30); DateTimeOffset end = to ?? DateTimeOffset.UtcNow.AddDays(1);
        if (user.PlantScope() is Guid scope) plantId = scope;
        IQueryable<SurveyResponse> baseQuery = db.SurveyResponses.AsNoTracking().Where(x => !x.IsTest && x.SubmittedAt >= start && x.SubmittedAt < end);
        if (plantId is Guid selected) baseQuery = baseQuery.Where(x => x.PlantId == selected);
        if (operatorId is Guid selectedOperator) baseQuery = baseQuery.Where(x => x.OperatorId == selectedOperator);
        var responses = await baseQuery.Include(x => x.Plant).Include(x => x.Ratings).ToListAsync();
        int total = responses.Count; int uniqueDevices = responses.Select(x => x.RespondentDeviceId).Distinct().Count();
        var ratings = responses.SelectMany(x => x.Ratings).ToList();
        object Metric(ServiceType type) { var values = ratings.Where(x => x.ServiceType == type).ToList(); return new { average = values.Count == 0 ? 0 : Math.Round(values.Average(x => x.Score), 2), count = values.Count, lowPercent = values.Count == 0 ? 0 : Math.Round(values.Count(x => x.Score <= 2) * 100d / values.Count, 1), distribution = Enumerable.Range(1, 5).Select(score => new { score, count = values.Count(x => x.Score == score) }) }; }
        var trend = responses.GroupBy(x => DateOnly.FromDateTime(x.SubmittedAt.LocalDateTime.Date)).OrderBy(x => x.Key).Select(g => new { date = g.Key, average = Math.Round(g.SelectMany(x => x.Ratings).Average(x => x.Score), 2), count = g.Count() });
        var plants = responses.GroupBy(x => new { x.PlantId, x.Plant!.Name }).Select(g => new { id = g.Key.PlantId, name = g.Key.Name, average = Math.Round(g.SelectMany(x => x.Ratings).Average(x => x.Score), 2), count = g.Count() }).OrderByDescending(x => x.average);
        int blocked = await db.RateLimitDecisions.CountAsync(x => x.Decision == "Blocked" && x.WindowStart >= start);
        return Results.Ok(new { total, uniqueDevices, sales = Metric(ServiceType.Sales), scale = Metric(ServiceType.Scale), loading = Metric(ServiceType.Loading), trend, plants, blockedAttempts = blocked });
    }

    private static async Task<IResult> ResponsesAsync(FactoryDbContext db, ClaimsPrincipal user, DateTimeOffset? from, DateTimeOffset? to, Guid? plantId, Guid? operatorId, ServiceType? serviceType, int? score, int page = 1, int pageSize = 50)
    {
        IQueryable<SurveyResponse> query = BuildReportQuery(db, user, from, to, plantId, operatorId, serviceType, score);
        int total = await query.CountAsync();
        var responses = await query.Include(x => x.Plant).Include(x => x.Operator).Include(x => x.Ratings)
            .OrderByDescending(x => x.SubmittedAt)
            .Skip((Math.Max(1, page) - 1) * Math.Clamp(pageSize, 1, 200))
            .Take(Math.Clamp(pageSize, 1, 200))
            .ToListAsync();
        var items = responses.Select(x => new
        {
            x.Id,
            x.SubmittedAt,
            x.CarPlate,
            Plant = x.Plant!.Name,
            Operator = x.Operator!.DisplayName,
            LoadingScore = RatingScore(x, ServiceType.Loading),
            LoadingComment = RatingComment(x, ServiceType.Loading),
            SalesScore = RatingScore(x, ServiceType.Sales),
            SalesComment = RatingComment(x, ServiceType.Sales),
            ScaleScore = RatingScore(x, ServiceType.Scale),
            ScaleComment = RatingComment(x, ServiceType.Scale),
            Comment = ResponseComment(x)
        });
        return Results.Ok(new { total, page = Math.Max(1, page), pageSize = Math.Clamp(pageSize, 1, 200), items });
    }

    private static async Task<IResult> ExportAsync(string format, FactoryDbContext db, ClaimsPrincipal user, HttpContext context, DateTimeOffset? from, DateTimeOffset? to, Guid? plantId, Guid? operatorId, ServiceType? serviceType, int? score)
    {
        var responses = await BuildReportQuery(db, user, from, to, plantId, operatorId, serviceType, score)
            .Include(x => x.Plant).Include(x => x.Operator).Include(x => x.Ratings)
            .OrderByDescending(x => x.SubmittedAt).ToListAsync();
        var rows = responses.Select(x => new ReportRow(
            x.SubmittedAt,
            x.CarPlate,
            x.Plant!.Name,
            x.Operator!.DisplayName,
            RatingScore(x, ServiceType.Loading),
            RatingScore(x, ServiceType.Sales),
            RatingScore(x, ServiceType.Scale),
            ResponseComment(x))).ToList();
        byte[] bytes; string contentType; string extension;
        switch (format.ToLowerInvariant())
        {
            case "xlsx": bytes = ReportExportService.Xlsx(rows); contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"; extension = "xlsx"; break;
            case "pdf": bytes = ReportExportService.Pdf(rows); contentType = "application/pdf"; extension = "pdf"; break;
            default: bytes = ReportExportService.Csv(rows); contentType = "text/csv; charset=utf-8"; extension = "csv"; break;
        }
        await AuditAsync(db, user.UserId(), "Export", "Report", extension, "", JsonSerializer.Serialize(new { from, to, plantId, operatorId, serviceType, score, rows = rows.Count }), context);
        return Results.File(bytes, contentType, $"factory-service-report-{DateTime.Now:yyyyMMdd}.{extension}");
    }

    private static IQueryable<SurveyResponse> BuildReportQuery(FactoryDbContext db, ClaimsPrincipal user, DateTimeOffset? from, DateTimeOffset? to, Guid? plantId, Guid? operatorId, ServiceType? serviceType, int? score)
    {
        DateTimeOffset start = from ?? DateTimeOffset.UtcNow.AddDays(-30), end = to?.AddDays(1) ?? DateTimeOffset.UtcNow.AddDays(1);
        IQueryable<SurveyResponse> query = db.SurveyResponses.AsNoTracking().Where(x => !x.IsTest && x.SubmittedAt >= start && x.SubmittedAt < end);
        if (user.PlantScope() is Guid scope) plantId = scope;
        if (plantId is Guid p) query = query.Where(x => x.PlantId == p);
        if (operatorId is Guid op) query = query.Where(x => x.OperatorId == op);
        if (serviceType is ServiceType service && score is >= 1 and <= 5)
            query = query.Where(x => x.Ratings.Any(rating => rating.ServiceType == service && rating.Score == score));
        else if (serviceType is ServiceType selectedService)
            query = query.Where(x => x.Ratings.Any(rating => rating.ServiceType == selectedService));
        else if (score is >= 1 and <= 5)
            query = query.Where(x => x.Ratings.Any(rating => rating.Score == score));
        return query;
    }

    private static bool ValidRepeatWindowDays(string? value, out int days)
    {
        days = 0;
        return value is not null && Regex.IsMatch(value, @"^(0|[1-9]\d{0,3})$") && int.TryParse(value, out days) && days <= 3650;
    }

    private static async Task<int> RepeatWindowDaysAsync(FactoryDbContext db, FactoryOptions options)
    {
        string? value = await db.Settings.AsNoTracking().Where(x => x.Key == "repeatWindowDays").Select(x => x.Value).SingleOrDefaultAsync();
        return ValidRepeatWindowDays(value, out int days) ? days : Math.Clamp(options.RepeatWindowDays, 0, 3650);
    }

    private static int RatingScore(SurveyResponse response, ServiceType type) =>
        response.Ratings.FirstOrDefault(x => x.ServiceType == type)?.Score ?? 0;

    private static string RatingComment(SurveyResponse response, ServiceType type) =>
        response.Ratings.FirstOrDefault(x => x.ServiceType == type)?.Comment?.Trim() ?? "";

    private static string ResponseComment(SurveyResponse response) => string.Join(" • ", response.Ratings
        .Where(x => !string.IsNullOrWhiteSpace(x.Comment))
        .OrderBy(x => x.ServiceType)
        .Select(x => x.Comment.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase));

    private static string GetOrSetDeviceToken(HttpContext context, bool secure)
    {
        const string name = "fs_device";
        if (context.Request.Cookies.TryGetValue(name, out string? existing) && !string.IsNullOrWhiteSpace(existing)) return existing;
        string token = SecureHash.RandomToken();
        context.Response.Cookies.Append(name, token, new CookieOptions { HttpOnly = true, Secure = secure, SameSite = SameSiteMode.Lax, IsEssential = true, MaxAge = TimeSpan.FromDays(400), Path = "/" });
        return token;
    }

    private static string? NormalizeCarPlate(string? value)
    {
        // Accepts AA123AA or AA-123-AA (any case) and stores it as AA123AA.
        Match match = Regex.Match((value ?? "").Trim().ToUpperInvariant(), @"^([A-Z]{2})-?([0-9]{3})-?([A-Z]{2})$");
        return match.Success ? match.Groups[1].Value + match.Groups[2].Value + match.Groups[3].Value : null;
    }

    private static string CleanComment(string? value) => Regex.Replace(value?.Trim() ?? "", "<[^>]+>", "").Replace("javascript:", "", StringComparison.OrdinalIgnoreCase);
    private static string MaskPhone(string phone) => phone.Length < 7 ? "***" : $"{phone[..4]}•••{phone[^3..]}";
    private static bool CanAccessPlant(ClaimsPrincipal user, Guid plantId) => user.IsSystemAdmin() || user.PlantScope() == plantId;
    private static object UserDto(PortalUser user) => new { user.Id, user.Username, user.DisplayName, role = user.Role, user.PlantId, plant = user.Plant?.Name };

    private static async Task AuditAsync(FactoryDbContext db, Guid? actorId, string action, string objectType, string objectId, string before, string after, HttpContext context)
    {
        db.AuditLogs.Add(new AuditLog { ActorId = actorId, Action = action, ObjectType = objectType, ObjectId = objectId, BeforeJson = before, AfterJson = after, IpAddress = context.Connection.RemoteIpAddress?.ToString() ?? "", Timestamp = DateTimeOffset.UtcNow, CorrelationId = context.TraceIdentifier });
        await db.SaveChangesAsync();
    }
}
