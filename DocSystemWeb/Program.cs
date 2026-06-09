using Microsoft.AspNetCore.Authentication.Cookies; // Подключаем куки для авторизации
using Google.Cloud.Firestore;

var builder = WebApplication.CreateBuilder(args);

// 1. Указываем программе путь к нашему ключу-пропуску
Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", "firebase-key.json");

// 2. Создаем постоянное подключение к облаку Firebase
builder.Services.AddSingleton(FirestoreDb.Create("doc-system-cloud"));

// 3. Настраиваем систему входа и защиты (Cookie Authentication)
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login"; // Если пользователь не вошел, кидаем сюда
        options.AccessDeniedPath = "/Account/Login";
    });

// Add services to the container.
builder.Services.AddControllersWithViews();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

// ВАЖНО: Проверка "Кто ты такой?" (Authentication) должна идти СТРОГО перед "Что тебе разрешено?" (Authorization)
app.UseAuthentication();
app.UseAuthorization();

// Меняем контроллер по умолчанию на Document, чтобы сразу открывалась база данных
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Document}/{action=Index}/{id?}");

app.Run();