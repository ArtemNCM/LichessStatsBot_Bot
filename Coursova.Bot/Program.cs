using Coursova.Bot;
using Coursova.Core;
using Coursova.Core.Mapping;
using Coursova.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http;
using System.Net.Http.Headers;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddHostedService<TelegramBotService>();

builder.Services.AddHttpClient<ILichessService, LichessService>(c =>
{
    c.BaseAddress = new Uri("https://lichess.org");
    c.DefaultRequestHeaders.Accept.Add(
        new MediaTypeWithQualityHeaderValue("application/json"));
});

builder.Services.AddScoped<IPlayerInfoRepository, PlayerInfoRepository>();

builder.Services.AddDbContext<ApplicationDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddAutoMapper(typeof(LichessMappingProfile).Assembly);

await builder.Build().RunAsync();
