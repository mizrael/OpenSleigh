using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Persistence.SQL;
using OpenSleigh.Transport.RabbitMQ;

namespace OpenSleigh.Samples.Sample4.API
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
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "OpenSleigh.Samples.Sample5.API", Version = "v1" });
            });
            
            services.AddOpenSleigh(cfg =>
            {
                var sqlConnStr = Configuration.GetConnectionString("sql");
                var sqlConfig = new SqlConfiguration(sqlConnStr);

                var rabbitSection = Configuration.GetSection("Rabbit");
                var rabbitCfg = new RabbitConfiguration(rabbitSection["HostName"],
                    rabbitSection["UserName"],
                    rabbitSection["Password"]);
                
                cfg.UseRabbitMQTransport(rabbitCfg)
                    .UseSqlPersistence(sqlConfig);
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
