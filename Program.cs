using fortune.Data;
using fortune.Models;
using Microsoft.EntityFrameworkCore;
using System.IO;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddHttpClient();

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

var app = builder.Build();

// Seed
using (var scope = app.Services.CreateScope())
{
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var env = scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>();

        var cardsFolder = Path.Combine(env.WebRootPath, "img", "cards");

        if (Directory.Exists(cardsFolder) && !db.Kartlar.Any())
        {
            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".jpg", ".jpeg", ".png", ".webp" };

            var files = Directory.EnumerateFiles(cardsFolder)
                .Where(f => allowed.Contains(Path.GetExtension(f)))
                .OrderBy(f => f)
                .ToList();

            foreach (var file in files)
            {
                var fileName = Path.GetFileName(file);

                db.Kartlar.Add(new Kart
                {
                    kartAdi = GetCardDisplayName(fileName),
                    ResimYolu = "/img/cards/" + fileName
                });
            }

            db.SaveChanges();
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine("DB Seed hata: " + ex.Message);
    }
}

// Pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Fal}/{action=Index}/{id?}"); // ✅ Fal/Index ile başlat

app.Run();

static string GetCardDisplayName(string fileName)
{
    var raw = Path.GetFileNameWithoutExtension(fileName);

    if (raw.Contains("-"))
    {
        return raw.Split('-', 2)[1]
                  .Replace("_", " ")
                  .Trim();
    }

    var suitMap = new Dictionary<string, string>
    {
        { "Cups", "of Cups" },
        { "Swords", "of Swords" },
        { "Pentacles", "of Pentacles" },
        { "Wands", "of Wands" }
    };

    foreach (var suit in suitMap)
    {
        if (raw.StartsWith(suit.Key))
        {
            var number = raw.Substring(suit.Key.Length);

            return number switch
            {
                "01" => "Ace " + suit.Value,
                "11" => "Page " + suit.Value,
                "12" => "Knight " + suit.Value,
                "13" => "Queen " + suit.Value,
                "14" => "King " + suit.Value,
                _ => $"{int.Parse(number)} {suit.Value}"
            };
        }
    }

    return raw;
}
