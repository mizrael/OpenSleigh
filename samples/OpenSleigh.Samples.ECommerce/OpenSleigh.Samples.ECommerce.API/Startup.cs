using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using OpenSleigh.Persistence.SQL;
using OpenSleigh.Persistence.PostgreSQL;
using OpenSleigh.Transport.RabbitMQ;
using OpenSleigh.DependencyInjection;

namespace OpenSleigh.Samples.ECommerce.API;

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
            c.SwaggerDoc("v1", new OpenApiInfo { Title = "OpenSleigh.Samples.ECommerce.API", Version = "v1" });
        });
        
        services.AddOpenSleigh(cfg =>
        {
            var sqlConnStr = Configuration.GetConnectionString("sql");
            var sqlConfig = new SqlConfiguration(sqlConnStr);

            var rabbitSection = Configuration.GetSection("Rabbit");
            var rabbitCfg = new RabbitConfiguration(
                hostName: rabbitSection["HostName"],
                vhost: rabbitSection["VHost"],
                userName: rabbitSection["UserName"],
                password: rabbitSection["Password"]);
            
            cfg.SetPublishOnly()
                .UseRabbitMQTransport(rabbitCfg)
                .UsePostgreSqlPersistence(sqlConfig);
        });
    }

    // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
            app.UseSwagger();
            app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "OpenSleigh.Samples.ECommerce.API v1"));
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
