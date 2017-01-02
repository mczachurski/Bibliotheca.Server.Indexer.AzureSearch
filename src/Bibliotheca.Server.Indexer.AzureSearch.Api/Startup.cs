using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Bibliotheca.Server.Mvc.Middleware.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Versioning;
using Swashbuckle.Swagger.Model;
using Bibliotheca.Server.Indexer.AzureSearch.Core.Services;
using Bibliotheca.Server.Indexer.AzureSearch.Core.Parameters;
using Bibliotheca.Server.ServiceDiscovery.ServiceClient;
using System;

namespace Bibliotheca.Server.Indexer.AzureSearch.Api
{
    public class Startup
    {
        public IConfigurationRoot Configuration { get; }

        protected bool UseServiceDiscovery { get; set; } = true;

        public Startup(IHostingEnvironment env)
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(env.ContentRootPath)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true)
                .AddEnvironmentVariables();
            Configuration = builder.Build();
        }

        public void ConfigureServices(IServiceCollection services)
        {
            services.Configure<ApplicationParameters>(Configuration);

            services.AddCors(options =>
            {
                options.AddPolicy("AllowAllOrigins", builder =>
                {
                    builder.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
                });
            });

            services.AddMvc(config =>
            {
                var policy = new AuthorizationPolicyBuilder()
                    .AddAuthenticationSchemes(SecureTokenDefaults.AuthenticationScheme)
                    .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
                    .RequireAuthenticatedUser()
                    .Build();
            }).AddJsonOptions(options =>
            {
                options.SerializerSettings.DateTimeZoneHandling = Newtonsoft.Json.DateTimeZoneHandling.Utc;
            });

            services.AddApiVersioning(options =>
            {
                options.AssumeDefaultVersionWhenUnspecified = true;
                options.DefaultApiVersion = new ApiVersion(1, 0);
                options.ReportApiVersions = true;
                options.ApiVersionReader = new QueryStringOrHeaderApiVersionReader("api-version");
            });

            services.AddSwaggerGen();
            services.ConfigureSwaggerGen(options =>
            {
                options.SingleApiVersion(new Info
                {
                    Version = "v1",
                    Title = "Indexer AzureSearch API",
                    Description = "Microservice for Azure search feature for Bibliotheca.",
                    TermsOfService = "None"
                });
            });

            services.AddScoped<ISearchService, SearchService>();
        }

        public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory, ISearchService searchService)
        {
            if (UseServiceDiscovery)
            {
                RegisterClient();
            }

            loggerFactory.AddConsole(Configuration.GetSection("Logging"));
            loggerFactory.AddDebug();

            if (!string.IsNullOrWhiteSpace(Configuration["AzureSearchApiKey"]))
            {
                searchService.CreateOrUpdateIndexAsync().Wait();
            }

            app.UseExceptionHandler();

            app.UseCors("AllowAllOrigins");

            var secureTokenOptions = new SecureTokenOptions
            {
                SecureToken = Configuration["SecureToken"],
                AuthenticationScheme = SecureTokenDefaults.AuthenticationScheme,
                Realm = SecureTokenDefaults.Realm
            };
            app.UseSecureTokenAuthentication(secureTokenOptions);

            var jwtBearerOptions = new JwtBearerOptions
            {
                Authority = Configuration["OAuthAuthority"],
                Audience = Configuration["OAuthAudience"],
                AutomaticAuthenticate = true,
                AutomaticChallenge = true
            };
            app.UseBearerAuthentication(jwtBearerOptions);

            app.UseMvc();

            app.UseSwagger();
            app.UseSwaggerUi();
        }

        private void RegisterClient()
        {
            var serviceDiscoveryConfiguration = Configuration.GetSection("ServiceDiscovery");
            var clientOptions = new ClientOptions
            {
                ServiceId = serviceDiscoveryConfiguration["ServiceId"],
                ServiceName = serviceDiscoveryConfiguration["ServiceName"],
                AgentAddress = serviceDiscoveryConfiguration["AgentAddress"],
                Datacenter = serviceDiscoveryConfiguration["Datacenter"],
                ClientPort = GetPort()
            };
            var serviceDiscovery = new ServiceDiscoveryClient();
            serviceDiscovery.Register(clientOptions);
        }

        private int GetPort()
        {
            var address = Configuration["server.urls"];
            if (!string.IsNullOrWhiteSpace(address))
            {
                var url = new Uri(address);
                return url.Port;
            }

            return 5000;
        }
    }
}
