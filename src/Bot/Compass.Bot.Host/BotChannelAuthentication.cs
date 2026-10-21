using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace Compass.Bot.Host;

/// <summary>
/// Validates the token Azure Bot Service attaches to every inbound activity. This protects the bot's endpoint
/// itself and is separate from the Teams SSO token that identifies the person (dev spec §9 hop 1).
/// </summary>
public static class BotChannelAuthentication
{
    public const string SectionName = "Bot:Channel";

    public static IHostApplicationBuilder AddBotChannelAuthentication(this IHostApplicationBuilder builder)
    {
        var section = builder.Configuration.GetSection(SectionName);
        var audiences = section.GetSection("ValidAudiences").Get<string[]>() ?? [];
        var issuers = section.GetSection("ValidIssuers").Get<string[]>() ??
        [
            "https://api.botframework.com",
        ];

        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MetadataAddress = section["MetadataAddress"] ?? "https://login.botframework.com/v1/.well-known/openidconfiguration";
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuers = issuers,
                    ValidateAudience = true,
                    ValidAudiences = audiences,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                };
            });

        return builder;
    }
}
