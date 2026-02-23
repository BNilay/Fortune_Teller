using fortune.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace fortune.Controllers
{
    public class FalController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _config;

        public FalController(ApplicationDbContext context, IHttpClientFactory httpClientFactory, IConfiguration config)
        {
            _context = context;
            _httpClientFactory = httpClientFactory;
            _config = config;
        }

        // GET: /Fal
        public async Task<IActionResult> Index()
        {
            var kartlar = await _context.Kartlar
                .AsNoTracking()
                .OrderBy(_ => Guid.NewGuid())
                .ToListAsync();

            return View(kartlar);
        }

        // POST: /Fal/Yorumla
        // JS payload: { kartIdleri: [1,2,3], prompt: "..." }
        [HttpPost]
        public async Task<IActionResult> Yorumla([FromBody] AiReadingRequest req)
        {
            try
            {
                // 1) Validasyon
                if (req?.KartIdleri == null || req.KartIdleri.Count != 3)
                    return BadRequest(new { message = "3 kart seçmelisin." });

                if (string.IsNullOrWhiteSpace(req.Prompt))
                    return BadRequest(new { message = "Lütfen fal sorunu yaz." });

                // 2) Kartları DB'den çek
                var kartlarDb = await _context.Kartlar
                    .AsNoTracking()
                    .Where(k => req.KartIdleri.Contains(k.Id))
                    .ToListAsync();

                if (kartlarDb.Count != 3)
                    return BadRequest(new { message = "Seçilen kartlar bulunamadı." });

                // 3) Seçim sırasını KORU (Geçmiş/Şimdi/Gelecek)
                var secilenKartlar = req.KartIdleri
                    .Select(id => kartlarDb.First(k => k.Id == id))
                    .ToList();

                
                var apiKey = _config["OpenAI:ApiKey"];
                var model = _config["OpenAI:Model"] ?? "gpt-4o-mini";

                if (string.IsNullOrWhiteSpace(apiKey))
                    return StatusCode(500, new { message = "OpenAI ApiKey eksik. appsettings.json / User Secrets içine eklemelisin." });

                
                var cardsText =
                    $"Geçmiş: {secilenKartlar[0].kartAdi}\n" +
                    $"Şimdi: {secilenKartlar[1].kartAdi}\n" +
                    $"Gelecek: {secilenKartlar[2].kartAdi}";

                var system = """
Sen deneyimli bir tarot yorumcususun ve biçim kurallarına %100 uyan bir metin yazarsın.

ÖNCELİK 1 — KİMİN HAKKINDA? (ZORUNLU)
Kullanıcı sorusunda geçen kişi/kişileri doğru tespit et:
- Eğer soru “benim / ben” diyorsa: “sen” üzerinden yaz.
- Eğer soru “X ve Y”, “arkadaşım”, “başka bir çift”, “onların ilişkisi” diyorsa: asla “sen” deme; mutlaka “onlar / çift” üzerinden yaz.
- Eğer soru belirsizse, varsayılan olarak “soruda adı geçen kişiler” üzerinden yaz.

ÖNCELİK 2 — SORUYA DOĞRUDAN CEVAP
Her bölümde (geçmiş/şimdi/gelecek) soruya doğrudan temas et. Genel geçer laflar yazma.

ÖNCELİK 3 — UZUNLUK
Her kart için 2–4 cümle yaz. Aşırı uzatma.

FORMAT (ZORUNLU)
Cevap TAM OLARAK şu şablonda olmalı. Başlıklar mutlaka kalın olacak:

**Geçmiş (KART_ADI)**
2–4 cümlelik yorum.

**Şimdi (KART_ADI)**
2–4 cümlelik yorum.

**Gelecek (KART_ADI)**
2–4 cümlelik yorum.

**Öneriler**
- 1 cümlelik öneri
- 1 cümlelik öneri
- (isteğe bağlı) 1 cümlelik öneri

DİL KURALLARI
- Türkçe karakterlere dikkat et.
- Samimi ve net ol.
- Emojiler en fazla 2 adet (istersen hiç kullanma).
- “Ben bir yapay zekayım” gibi ifadeler yazma.

KONTROL LİSTESİ (YANIT VERME ÖNCESİ)
1) “Sen” mi yazdım yoksa “onlar/çift” mi? Soru hangisini gerektiriyorsa onu kullandım mı?
2) Başlıklar kalın mı?
3) Her kart 2–4 cümle mi?
4) Öneriler 2–3 madde mi?
""";

                var user = $"""
SORU:
{req.Prompt}

KARTLAR:
{cardsText}
""";

                
                var payload = new
                {
                    model = model,
                    messages = new object[]
                    {
                        new { role = "system", content = system },
                        new { role = "user", content = user }
                    },
                    temperature = 0.8
                };

                var client = _httpClientFactory.CreateClient();

                
                client.Timeout = TimeSpan.FromSeconds(60);

                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", apiKey);

                var json = JsonSerializer.Serialize(payload);
                using var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

                var resp = await client.PostAsync("https://api.openai.com/v1/chat/completions", httpContent);
                var respText = await resp.Content.ReadAsStringAsync();

                if (!resp.IsSuccessStatusCode)
                {
                   
                    return StatusCode((int)resp.StatusCode, new { message = respText });
                }

                
                string reading = "";
                using (var doc = JsonDocument.Parse(respText))
                {
                    reading = doc.RootElement
                        .GetProperty("choices")[0]
                        .GetProperty("message")
                        .GetProperty("content")
                        .GetString() ?? "";
                }

                if (string.IsNullOrWhiteSpace(reading))
                    reading = "Yorum üretilemedi. Lütfen tekrar dene.";

                
                return Ok(new
                {
                    cards = secilenKartlar.Select(x => new { x.Id, x.kartAdi, x.ResimYolu }),
                    reading = reading
                });
            }
            catch (Exception ex)
            {
                
                return StatusCode(500, new { message = $"Sunucu hatası: {ex.Message}" });
            }
        }
    }

    public class AiReadingRequest
    {
       
        public List<int> KartIdleri { get; set; } = new();
        public string Prompt { get; set; } = "";
    }
}
