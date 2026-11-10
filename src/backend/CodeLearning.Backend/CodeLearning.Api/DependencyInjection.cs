using CodeLearning.Api.Extensions;
using CodeLearning.Api.Middleware;
using CodeLearning.Api.Realtime;
using CodeLearning.Application.Settings;
using CodeLearning.Core.Messages;
using CodeLearning.Infrastructure.Data;
using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Serilog;
using System.Net;
using System.Text;

namespace CodeLearning.Api;

public static class DependencyInjection
{
    public const string FrontendCorsPolicy = "AllowFrontend";

    extension(IServiceCollection services)
    {
        public IServiceCollection AddPresentation(IConfiguration configuration, IWebHostEnvironment environment)
        {
            services
                .AddApiAuthentication()
                .AddApiMessaging()
                .AddApiHealthChecks(configuration)
                .AddApiDocumentation();

            services.AddExceptionHandler<ValidationExceptionHandler>();
            services.AddExceptionHandler<GlobalExceptionHandler>();
            services.AddProblemDetails();

            services.AddApiRateLimiting();
            services.AddTrustedForwardedHeaders(configuration);

            var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
            services.AddCors(options =>
            {
                options.AddPolicy(FrontendCorsPolicy, policy =>
                {
                    policy.WithOrigins(allowedOrigins)
                          .AllowCredentials()
                          .AllowAnyHeader()
                          .AllowAnyMethod();
                });
            });

            var dataProtectionBuilder = services.AddDataProtection()
                .SetApplicationName("CodeLearning.Api");

            if (!environment.IsEnvironment("Testing"))
            {
                var keysPath = configuration["DataProtection:KeysPath"] is { Length: > 0 } path ? path : "/app/keys";
                dataProtectionBuilder.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
            }

            services.AddControllers();
            services.AddSignalR();

            return services;
        }

        // Behind a reverse proxy every request arrives from the proxy's address, so the per-IP auth rate
        // limit would put all users in one bucket. X-Forwarded-For is honoured only from the proxies
        // listed here (none by default), so clients cannot pick their own address to dodge the limit
        public IServiceCollection AddTrustedForwardedHeaders(IConfiguration configuration)
        {
            var knownProxies = ReadList(configuration, "ForwardedHeaders:KnownProxies");
            var knownNetworks = ReadList(configuration, "ForwardedHeaders:KnownNetworks");

            services.AddOptions<ForwardedHeadersOptions>().Configure<ILoggerFactory>((options, loggerFactory) =>
            {
                var logger = loggerFactory.CreateLogger("CodeLearning.Api.ForwardedHeaders");

                // One trusted hop: the nginx frontend proxying /api
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                options.ForwardLimit = 1;

                foreach (var proxy in knownProxies)
                {
                    if (IPAddress.TryParse(proxy, out var address))
                    {
                        options.KnownProxies.Add(address);
                    }
                    else
                    {
                        logger.LogWarning("Ignoring invalid ForwardedHeaders:KnownProxies entry {Entry}", proxy);
                    }
                }

                foreach (var network in knownNetworks)
                {
                    if (System.Net.IPNetwork.TryParse(network, out var parsed))
                    {
                        options.KnownIPNetworks.Add(parsed);
                    }
                    else
                    {
                        logger.LogWarning("Ignoring invalid ForwardedHeaders:KnownNetworks entry {Entry} (expected CIDR, e.g. 172.28.0.0/24)", network);
                    }
                }
            });

            return services;
        }

        public IServiceCollection AddApiAuthentication()
        {
            services.AddOptions<JwtSettings>()
                .BindConfiguration(JwtSettings.SectionName)
                .ValidateDataAnnotations()
                .ValidateOnStart();

            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                // Token selection and the revocation check both live in these events
                options.Events = JwtBearerEventHandlers.Create();
            });

            services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
                .Configure<IOptions<JwtSettings>>((options, jwtOptions) =>
                {
                    var jwtSettings = jwtOptions.Value;
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidateAudience = true,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        ValidIssuer = jwtSettings.Issuer,
                        ValidAudience = jwtSettings.Audience,
                        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.SecretKey)),
                        ClockSkew = TimeSpan.Zero
                    };
                });

            services.AddAuthorization();

            return services;
        }

        public IServiceCollection AddApiMessaging()
        {
            services.AddOptions<RabbitMqTransportOptions>().BindConfiguration("RabbitMqTransport");

            services.AddMassTransit(x =>
            {
                // Messages published while handling a request are saved with its DbContext changes and delivered afterwards
                x.AddEntityFrameworkOutbox<ApplicationDbContext>(o =>
                {
                    o.UsePostgres();
                    o.UseBusOutbox();
                });

                // Status events are only worth something while they are fresh, and every API instance has to
                // see each one to tell the browsers connected to it. So each instance gets its own queue,
                // which RabbitMQ deletes when the instance disconnects
                x.AddConsumer<SubmissionStatusChangedConsumer>()
                    .Endpoint(e =>
                    {
                        e.Temporary = true;
                        e.InstanceId = Guid.NewGuid().ToString("N");
                    });

                x.UsingRabbitMq((context, cfg) =>
                {
                    // Publishing isn't mandatory, so RabbitMQ drops a message that has no bound queue.
                    // Bind the Runner's queue to the message exchange from here too, so submissions made before
                    // the Runner first starts are kept. RabbitMQ delivers once even though the Runner adds a second path.
                    // The outbox sends already-serialized messages, which skips the per-message publish topology,
                    // so the topology is deployed when the bus starts instead
                    var messageExchange = cfg.MessageTopology.GetMessageTopology<SubmissionCreated>().EntityName;
                    cfg.Publish<SubmissionCreated>(p => p.BindQueue(
                        messageExchange,
                        MessageQueues.Submissions,
                        q => q.SetQuorumQueue()));
                    cfg.DeployPublishTopology = true;

                    cfg.ConfigureEndpoints(context);
                });
            });

            return services;
        }

        public IServiceCollection AddApiHealthChecks(IConfiguration configuration)
        {
            services.AddHealthChecks()
                .AddNpgSql(
                    configuration.GetConnectionString("DefaultConnection")!,
                    name: "postgres",
                    timeout: TimeSpan.FromSeconds(3),
                    tags: ["db", "sql", "postgres"])
                .AddRedis(
                    configuration.GetConnectionString("Redis")!,
                    name: "redis",
                    timeout: TimeSpan.FromSeconds(3),
                    tags: ["cache", "redis"]);

            return services;
        }

        public IServiceCollection AddApiDocumentation()
        {
            services.AddEndpointsApiExplorer();
            services.AddSwaggerGen(options =>
            {
                options.SwaggerDoc("v1", new OpenApiInfo
                {
                    Title = "CodeLearning API",
                    Version = "v1",
                    Description = "Educational platform for learning programming with automatic code evaluation",
                });

                options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "Bearer",
                    BearerFormat = "JWT",
                    Description = "JWT Authorization header using the Bearer scheme."
                });

                options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference("Bearer", document)] = []
                });
            });

            return services;
        }
    }

    // Accepts a JSON array or indexed keys (Section__0, Section__1) as well as one comma-separated
    // value, which is the convenient form for a single environment variable in docker compose
    private static string[] ReadList(IConfiguration configuration, string key)
    {
        var section = configuration.GetSection(key);
        var values = section.GetChildren().Any()
            ? section.Get<string[]>() ?? []
            : section.Value?.Split(',') ?? [];

        return values
            .Select(v => v.Trim())
            .Where(v => v.Length > 0)
            .ToArray();
    }

    extension(WebApplication app)
    {
        public WebApplication UseApiPipeline()
        {
            // First, so logging, HTTPS redirection and the rate limiter see the client's real address
            app.UseForwardedHeaders();

            if (!app.Environment.IsEnvironment("Testing"))
            {
                app.UseSerilogRequestLogging(options =>
                {
                    options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
                    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
                    {
                        diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value);
                        diagnosticContext.Set("RequestScheme", httpContext.Request.Scheme);
                        diagnosticContext.Set("RemoteIP", httpContext.Connection.RemoteIpAddress);
                    };
                });
            }

            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI(options =>
                {
                    options.SwaggerEndpoint("/swagger/v1/swagger.json", "CodeLearning API v1");
                    options.RoutePrefix = "swagger";
                    options.DocumentTitle = "CodeLearning API Documentation";
                });
            }

            app.UseExceptionHandler();

            // Off when TLS ends at a proxy that talks plain http to the API (compose sets Https:RedirectEnabled=false)
            if (app.Configuration.GetValue("Https:RedirectEnabled", !app.Environment.IsDevelopment()))
            {
                app.UseHttpsRedirection();
            }

            app.UseCors(FrontendCorsPolicy);

            // Revoked access tokens are rejected inside authentication (JwtBearerEvents.OnTokenValidated)
            app.UseAuthentication();
            app.UseAuthorization();

            // After authentication so the submissions policy can partition by user; the login policy
            // partitions by IP and email, which this middleware reads from the body first
            app.UseMiddleware<LoginRateLimitKeyMiddleware>();
            app.UseRateLimiter();

            app.MapHealthChecks("/health");

            app.MapControllers();
            app.MapHub<SubmissionsHub>(SubmissionsHub.Route);

            return app;
        }
    }
}
