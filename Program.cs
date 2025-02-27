using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using AirbnbShopApi.Data;
using AirbnbShopApi.Services;
using AirbnbShopApi.Models;
using Telegram.Bot;
using Microsoft.AspNetCore.SignalR;
using AirbnbShopApi.Hubs;
using AirbnbShopApi.Controllers;

Console.WriteLine("Iniciando a aplicação...");

var builder = WebApplication.CreateBuilder(args);

Console.WriteLine("Carregando configurações do appsettings.json...");
var config = builder.Configuration;
Console.WriteLine($"PaymentProviders:MercadoPago:ApiUrl = {config["PaymentProviders:MercadoPago:ApiUrl"]}");

builder.Services.AddControllersWithViews();
builder.Services.AddDbContext<AppDbContext>(options =>
{
    Console.WriteLine("Configurando DbContext...");
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' não encontrada.");
    options.UseMySQL(connectionString);
    Console.WriteLine("DbContext configurado.");
});
builder.Services.AddIdentity<ApplicationUser, IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

builder.Services.AddHttpClient();

// Registro dos provedores de pagamento
builder.Services.Configure<PaymentProviderConfig>(builder.Configuration.GetSection("PaymentProviders:PIXDefault"));
builder.Services.Configure<PaymentProviderConfig>(builder.Configuration.GetSection("PaymentProviders:MercadoPago"));
builder.Services.Configure<PaymentProviderConfig>(builder.Configuration.GetSection("PaymentProviders:PayPal"));
builder.Services.Configure<PaymentProviderConfig>(builder.Configuration.GetSection("PaymentProviders:Stripe"));
builder.Services.AddScoped<IPaymentProvider, PIXDefaultPaymentProvider>();
builder.Services.AddScoped<IPaymentProvider, MercadoPagoPaymentProvider>();
builder.Services.AddScoped<IPaymentProvider, PayPalPaymentProvider>();
builder.Services.AddScoped<IPaymentProvider, StripePaymentProvider>();
builder.Services.AddScoped<IPaymentService, MultiPaymentService>();
builder.Services.AddScoped<ReconciliationController>();

builder.Services.AddSingleton<BotService>(); // Singleton para BotService
builder.Services.AddSignalR();

var app = builder.Build();

Console.WriteLine("Configurando pipeline HTTP...");

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Welcome}/{id?}",
    defaults: new { controller = "Home", action = "Welcome" }
);
app.MapHub<NotificationHub>("/notificationHub");

app.MapGet("/", context =>
{
    Console.WriteLine("Redirecionando / para /Home/Welcome...");
    context.Response.Redirect("/Home/Welcome");
    return Task.CompletedTask;
});

Console.WriteLine("Obtendo BotService...");
var botService = app.Services.GetService<BotService>();
if (botService == null)
{
    Console.WriteLine("Erro: BotService não foi resolvido.");
}
else
{
    Console.WriteLine("BotService obtido. Iniciando o bot...");
    botService.Start();
}

using (var scope = app.Services.CreateScope())
{
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

    if (!await roleManager.RoleExistsAsync("Financeiro"))
    {
        await roleManager.CreateAsync(new IdentityRole("Financeiro"));
        Console.WriteLine("Role 'Financeiro' criado.");
    }

    var user = await userManager.FindByNameAsync("admin");
    if (user == null)
    {
        user = new ApplicationUser { UserName = "admin", Email = "admin@example.com" };
        var result = await userManager.CreateAsync(user, "Admin123!");
        if (result.Succeeded)
        {
            await userManager.AddToRoleAsync(user, "Financeiro");
            Console.WriteLine("Usuário padrão 'admin' criado com role 'Financeiro'.");
        }
        else
            Console.WriteLine($"Erro ao criar usuário 'admin': {string.Join(", ", result.Errors.Select(e => e.Description))}");
    }
    else if (!await userManager.IsInRoleAsync(user, "Financeiro"))
    {
        await userManager.AddToRoleAsync(user, "Financeiro");
        Console.WriteLine("Role 'Financeiro' adicionado ao usuário 'admin'.");
    }
    else
    {
        Console.WriteLine("Usuário padrão 'admin' já existe com role 'Financeiro'.");
    }
}

Console.WriteLine($"Aplicação configurada. Iniciando servidor em http://localhost:5000...");
app.Lifetime.ApplicationStarted.Register(() => Console.WriteLine("Servidor iniciado e ouvindo conexões."));
app.Lifetime.ApplicationStopped.Register(() => Console.WriteLine("Servidor parado."));

await app.RunAsync();