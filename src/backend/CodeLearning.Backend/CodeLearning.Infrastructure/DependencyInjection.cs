using CodeLearning.Application.Services;
using CodeLearning.Core.Entities;
using CodeLearning.Infrastructure.Data;
using CodeLearning.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace CodeLearning.Infrastructure;

public static class DependencyInjection
{
    extension(IServiceCollection services)
    {
        // Shared by the API and the Runner
        public IServiceCollection AddPersistence(IConfiguration configuration)
        {
            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseNpgsql(configuration.GetConnectionString("DefaultConnection")));

            return services;
        }

        public IServiceCollection AddInfrastructure(IConfiguration configuration)
        {
            services.AddPersistence(configuration);

            services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(
                ConfigurationOptions.Parse(configuration.GetConnectionString("Redis")
                    ?? throw new InvalidOperationException("Redis connection string not configured"))));

            // Identity core only: authentication is the JWT, so Identity's cookie schemes are not registered
            services.AddIdentityCore<User>(options =>
                {
                    options.Password.RequireDigit = true;
                    options.Password.RequireLowercase = true;
                    options.Password.RequireUppercase = true;
                    options.Password.RequireNonAlphanumeric = false;
                    options.Password.RequiredLength = 8;

                    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
                    options.Lockout.MaxFailedAccessAttempts = 5;
                    options.Lockout.AllowedForNewUsers = true;

                    options.User.RequireUniqueEmail = true;
                })
                .AddRoles<IdentityRole<Guid>>()
                .AddEntityFrameworkStores<ApplicationDbContext>()
                .AddSignInManager()
                .AddDefaultTokenProviders();

            services.AddScoped<ITokenService, TokenService>();
            services.AddScoped<IAuthService, AuthService>();
            services.AddScoped<ICourseService, CourseService>();
            services.AddScoped<IChapterService, ChapterService>();
            services.AddScoped<ISubchapterService, SubchapterService>();
            services.AddScoped<IBlockService, BlockService>();
            services.AddScoped<IEnrollmentService, EnrollmentService>();
            services.AddScoped<IProgressService, ProgressService>();
            services.AddScoped<IProblemService, ProblemService>();
            services.AddScoped<ISubmissionService, SubmissionService>();
            services.AddScoped<IQuizService, QuizService>();
            services.AddScoped<ILanguageService, LanguageService>();

            return services;
        }
    }
}
