using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace SensattaReports.Api;

/// <summary>
/// Autenticacao JWT Bearer para os consumidores externos (Protheus e painel).
/// Cada cliente (clientId+secret, definidos em Jwt:Clients) obtem um token em
/// POST /auth/token e envia "Authorization: Bearer &lt;token&gt;" em cada chamada.
///
/// A protecao so e ATIVADA quando Jwt:Secret esta configurado — sem ele (dev),
/// as rotas ficam abertas, para facilitar o desenvolvimento local.
/// </summary>
public static class Auth
{
    public const string PolicyReports = "ReportsConsumer"; // qualquer token JWT valido
    public const string PolicyPanel = "PanelApi";          // token JWT com role admin

    public sealed class JwtClient
    {
        public string Id { get; set; } = "";
        public string Secret { get; set; } = "";
        public string Role { get; set; } = "reports";
    }

    public static bool IsEnabled(IConfiguration cfg)
        => !string.IsNullOrWhiteSpace(cfg["Jwt:Secret"]);

    /// <summary>Configura o esquema JWT Bearer.</summary>
    public static void AddReportsJwt(this AuthenticationBuilder builder, IConfiguration cfg)
    {
        var secret = cfg["Jwt:Secret"]!; // so chamado quando IsEnabled
        var issuer = cfg["Jwt:Issuer"] ?? "sensatta-reports";
        var audience = cfg["Jwt:Audience"] ?? "sensatta-reports";
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));

        builder.AddJwtBearer(o =>
        {
            o.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = issuer,
                ValidateAudience = true,
                ValidAudience = audience,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = key,
                ClockSkew = TimeSpan.FromSeconds(30)
            };
            // Para o preview em <iframe> (o navegador nao manda header): aceita
            // ?access_token= apenas nas rotas GET de geracao de relatorio.
            o.Events = new JwtBearerEvents
            {
                OnMessageReceived = ctx =>
                {
                    if (string.IsNullOrEmpty(ctx.Token))
                    {
                        var qt = ctx.Request.Query["access_token"].ToString();
                        if (!string.IsNullOrEmpty(qt) && ctx.Request.Path.StartsWithSegments("/reports"))
                            ctx.Token = qt;
                    }
                    return Task.CompletedTask;
                }
            };
        });
    }

    /// <summary>Policies: /reports exige Bearer válido; /api exige Bearer + role admin.</summary>
    public static void AddReportsPolicies(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddPolicy(PolicyReports, p => p
                .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser())
            .AddPolicy(PolicyPanel, p => p
                .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .RequireRole("admin"));
    }

    /// <summary>POST /auth/token — fluxo client-credentials: devolve o JWT.</summary>
    public static void MapTokenEndpoint(this WebApplication app)
    {
        app.MapPost("/auth/token", (TokenRequest req, IConfiguration cfg) =>
        {
            if (!IsEnabled(cfg))
                return Results.Problem("JWT não habilitado (defina Jwt:Secret).", statusCode: 503);

            var clients = cfg.GetSection("Jwt:Clients").Get<List<JwtClient>>() ?? new();
            var client = clients.FirstOrDefault(c =>
                !string.IsNullOrEmpty(c.Id) &&
                string.Equals(c.Id, req.ClientId, StringComparison.Ordinal) &&
                string.Equals(c.Secret, req.ClientSecret, StringComparison.Ordinal));
            if (client is null)
                return Results.Json(new { error = "Credenciais inválidas." }, statusCode: 401);

            var minutes = cfg.GetValue("Jwt:ExpiresMinutes", 60);
            var issuer = cfg["Jwt:Issuer"] ?? "sensatta-reports";
            var audience = cfg["Jwt:Audience"] ?? "sensatta-reports";
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(cfg["Jwt:Secret"]!));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var now = DateTime.UtcNow;

            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, client.Id),
                new(ClaimTypes.Role, string.IsNullOrWhiteSpace(client.Role) ? "reports" : client.Role),
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };
            var token = new JwtSecurityToken(issuer, audience, claims, now, now.AddMinutes(minutes), creds);
            var jwt = new JwtSecurityTokenHandler().WriteToken(token);

            return Results.Ok(new
            {
                access_token = jwt,
                token_type = "Bearer",
                expires_in = minutes * 60,
                role = claims[1].Value
            });
        }).DisableAntiforgery().WithTags("Autenticação");
    }
}

public record TokenRequest(string? ClientId, string? ClientSecret);
