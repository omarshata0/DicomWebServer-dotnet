using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpContextAccessor();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSingleton<DicomWebFsServer.Services.DicomFileService>();

builder.Services.AddCors(options =>
{
  options.AddPolicy("AllowOHIF", policy =>
  {
    policy.AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader();
  });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
  app.UseDeveloperExceptionPage();
  app.UseSwagger();
  app.UseSwaggerUI();
}

app.UseCors("AllowOHIF");

app.UseRouting();

app.Use(async (ctx, next) =>
{
  Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {ctx.Request.Path}");
  await next();
});


app.MapControllers();
app.Run();
