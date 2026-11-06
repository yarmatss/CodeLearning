using CodeLearning.Api.Extensions;
using CodeLearning.Api.Middleware;
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

            return services;
        }

        // Behind a reverse proxy every request arrives from the proxy's address, so the per-IP auth rate
        // limit would put all users in one bucket. X-Forwarded-For is honoured only from the proxies
        // listed here (none by default), so clients cannot pick their own address to dodge the limit
        public IServiceCollection AddTrustedForwardedHeaders(IConfiguration configuration)
        {
            var knownProxies = configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [];
            var knownNetworks = configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? [];

            services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                options.ForwardLimit = 1;

                foreach (var proxy in knownProxies)
                {
                    options.KnownProxies.Add(IPAddress.Parse(proxy));
                }

                foreach (var network in knownNetworks)
                {
                    options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
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
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var authHeader = context.Request.Headers.Authorization.ToString();
                        if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer "))
                        {
                            context.Token = authHeader.Substring("Bearer ".Length).Trim();
                            return Task.CompletedTask;
                        }

                        context.Token = context.Request.Cookies["access_token"];
                        return Task.CompletedTask;
                    }
                };
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

            if (!app.Environment.IsDevelopment())
            {
                app.UseHttpsRedirection();
            }

            app.UseCors(FrontendCorsPolicy);

            app.UseAuthentication();
            app.UseMiddleware<JwtBlacklistMiddleware>();
            app.UseAuthorization();

            // After authentication so the submissions policy can partition by user
            app.UseRateLimiter();

            app.MapHealthChecks("/health");

            app.MapControllers();

            return app;
        }
    }
}
