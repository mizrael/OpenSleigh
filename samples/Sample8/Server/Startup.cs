using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Linq;
using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Persistence.InMemory;
using OpenSleigh.Samples.Sample8.Server.Hubs;
using OpenSleigh.Samples.Sample8.Server.Sagas;

namespace OpenSleigh.Samples.Sample8.Server
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
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
            
            services.AddControllersWithViews();
            services.AddRazorPages();

            services.AddSignalR();
            services.AddResponseCompression(opts =>
            {
                opts.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
                    new[] { "application/octet-stream" });
            });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            app.UseResponseCompression();
            
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseWebAssemblyDebugging();
            }
            else
            {
                app.UseExceptionHandler("/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseBlazorFrameworkFiles();
            app.UseStaticFiles();

            app.UseRouting();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapRazorPages();
                endpoints.MapControllers();
                endpoints.MapHub<SagaHub>("/sagahub");
                endpoints.MapFallbackToFile("index.html");
            });
        }
    }
}
