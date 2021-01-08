using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Persistence.Mongo;
using OpenSleigh.Samples.Sample2.Common.Sagas;
using OpenSleigh.Transport.RabbitMQ;

namespace OpenSleigh.Samples.Sample2.API
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
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "OpenSleigh.Samples.Sample2.API", Version = "v1" });
            });
            
            services.AddOpenSleigh(cfg =>
            {
                var mongoSection = Configuration.GetSection("Mongo");
                var mongoCfg = new MongoConfiguration(mongoSection["ConnectionString"],
                    mongoSection["DbName"],
                    MongoSagaStateRepositoryOptions.Default);

                var rabbitSection = Configuration.GetSection("Rabbit");
                var rabbitCfg = new RabbitConfiguration(rabbitSection["HostName"],
                    rabbitSection["UserName"],
                    rabbitSection["Password"]);
            });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseSwagger();
                app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "OpenSleigh.Samples.Sample2.API v1"));
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
