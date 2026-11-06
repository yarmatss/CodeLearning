using CodeLearning.Application.Validators.Auth;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace CodeLearning.Application;

public static class DependencyInjection
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddApplication()
        {
            services.AddValidatorsFromAssemblyContaining<RegisterDtoValidator>();

            return services;
        }
    }
}
