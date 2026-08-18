using WebDatLichKhamBenh.Application;
using WebDatLichKhamBenh.Infrastructure;
using FluentValidation;
using System.Text.Json.Serialization;
using WebDatLichKhamBenh.Api.Services;

var builder = WebApplication.CreateBuilder(args);

const string AllowFrontendDevPolicy = "AllowFrontendDev";

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(options =>
{
    options.AddPolicy(AllowFrontendDevPolicy, policy =>
    {
        policy
            .WithOrigins(
                "http://localhost:3000",
                "https://localhost:3000",
                "http://localhost:5173",
                "https://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHostedService<AppointmentSlotReconciliationService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors(AllowFrontendDevPolicy);

app.MapControllers();


app.Run();
