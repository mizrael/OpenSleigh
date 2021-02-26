using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Persistence.InMemory;
using OpenSleigh.Samples.Sample8.API.Sagas;

namespace OpenSleigh.Samples.Sample8.API
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllers();
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "OpenSleigh.Samples.Sample8.API", Version = "v1" });
            });
            
            services.AddOpenSleigh(cfg =>
            {
                cfg.UseInMemoryPersistence()
                    .UseInMemoryTransport();

                cfg.AddSaga<StepsSaga, StepsSagaState>()
                    .UseStateFactory<StartSaga>(msg => new StepsSagaState(msg.CorrelationId))
                    .UseStateFactory<ProcessNextStep>(msg => new StepsSagaState(msg.CorrelationId))
                    .UseStateFactory<SagaCompleted>(msg => new StepsSagaState(msg.CorrelationId))
                    .UseInMemoryTransport();
            });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseSwagger();
                app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "OpenSleigh.Samples.Sample8.API v1"));
            }

            app.UseHttpsRedirection();

            app.UseRouting();

            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }
    }
}
