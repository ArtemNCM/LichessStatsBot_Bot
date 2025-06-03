using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Polling;
using Telegram.Bot.Types.Enums;
using Coursova.Core.Models.Requests;
using System.Text;
using Coursova.Core.Models.DTOs;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Net;
using Coursova.Core.Models.Entities;
using Coursova.Core;
using Coursova.Bot;
using Coursova.Infrastructure;
using System.Threading;


public class TelegramBotService : BackgroundService
{
    private readonly ILogger<TelegramBotService> _log;
    private readonly ILichessService _lichess;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TelegramBotClient _bot;
    private readonly string _lichessToken;
    private readonly long _adminChatId;
    private static readonly string[] TopBlitzUsers = new[]
    {
        "penguingim1",
        "Craze",
        "HomayooonT",
        "Infinity-Stones",
        "Ratkovic_Miloje",
        "ABachmann",
        "AnishGiri",
        "MasterAssasin123",
        "dalmatinac101",
        "Experience_Chess",
        "RealDavidNavara",
        "Dr-CRO",
        "gmmoranda",
        "FakeBruceLee",
        "S2Pac",
        "Vladimirovich9000",
        "venajalainen",
        "Chewbacca18",
        "Sigma_Tauri",
        "DrawDenied_Twitch",
        "dr_dre08",
        "Andrey11976",
        "Bestinblitz",
    };
    public TelegramBotService(IConfiguration cfg,
                              ILichessService lichess,
                              IServiceScopeFactory scopeFactory,
                              ILogger<TelegramBotService> log)
    {
        _log = log;
        _lichess = lichess;
        _scopeFactory = scopeFactory;
        _bot = new TelegramBotClient(TgToken.BotToken);
        _lichessToken = Environment.GetEnvironmentVariable("LICHESS_TOKEN") ?? cfg["Lichess:Token"]?? throw new InvalidOperationException("LICHESS_TOKEN missing");

        if (!long.TryParse(cfg["AdminChatId"], out _adminChatId))
        {
            _adminChatId = 0;
            _log.LogWarning("AdminChatId не заданий або недійсний у конфігурації. Команда /deleteplayer буде недоступна.");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var opts = new ReceiverOptions { AllowedUpdates = { } }; 
        _bot.StartReceiving(
            HandleUpdateAsync,
            HandleErrorAsync,
            opts, ct);
        var me = await _bot.GetMe(ct);
        _log.LogInformation($"Telegram bot started: @{me.Username}");
    }

    private async Task HandleUpdateAsync(ITelegramBotClient bot,
                                         Update update,
                                         CancellationToken ct)
    {
        if (update.Type != UpdateType.Message || update.Message!.Type != MessageType.Text)
            return;

        var msg = update.Message;
        var parts = msg.Text!.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var cmd = parts[0].ToLower();

        switch (cmd)
        {
            case "/start":
                await bot.SendMessage(msg.Chat.Id,
                    "👋 Я бот-статистик Lichess. \n/help - детальніше про можливості боту",
                    cancellationToken: ct);
                break;


            case "/help":
                {
                    const string help = """
                    <b>Команди LichessStats-бота</b>

                    /start – короткий вступ і приклад
                    /help  – показати цю довідку

                    /info &lt;нік&gt;
                        Показує рейтинги, дату реєстрації та іншу базову інформацію.

                    /pgn &lt;нік&gt; [N=5]
                        Надсилає .pgn-файл із останніми N партіями (1-50).

                    /fav &lt;нік&gt;
                        Улюблений контроль часу (bullet / blitz / rapid / classical),
                        його рейтинг і кількість зіграних партій.

                    /randomgame - випадкова цікава партія з топ-гравців blitz.

                    /compare &lt;нік1&gt; &lt;нік2&gt;
                        Порівняння рейтингів та W/D/L двох гравців.

                    /perf &lt;нік&gt; &lt;днів&gt;
                    /perf &lt;нік&gt; &lt;dd.MM.yyyy&gt; &lt;dd.MM.yyyy&gt;
                        Статистика W-D-L та Win-rate за вказаний проміжок.
                        Якщо днів немає – останні 30.

                    /openings &lt;нік&gt; [white|black] [top 1-10]
                        Топ дебютів гравця (за замовчуванням – топ-5 усіма кольорами).

                    /chart &lt;нік&gt; &lt;control&gt; &lt;днів&gt;
                    /chart &lt;нік&gt; &lt;control&gt; &lt;dd.MM.yyyy&gt; &lt;dd.MM.yyyy&gt;
                        PNG-графік зміни рейтингу (bullet / blitz / rapid / classical).
                        Без параметрів дат – останні 30 днів.
                    """;

                    await bot.SendMessage(
                        chatId: msg.Chat.Id,
                        text: help,
                        parseMode: ParseMode.Html,
                        cancellationToken: ct);
                }
                break;

            case "/info":
                {
                    if (parts.Length < 2)
                    {
                        await bot.SendMessage(
                            chatId: msg.Chat.Id,
                            text: "⚠️ Формат: /info <нік>",
                            cancellationToken: ct);
                        break;
                    }

                    var dto = await _lichess.GetPlayerInfoAsync(parts[1]);
                    if (dto is null)
                    {
                        await bot.SendMessage(
                            msg.Chat.Id,
                            "Не знайшов такого гравця 😢",
                            cancellationToken: ct);
                        break;
                    }

                    try
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var repo = scope.ServiceProvider
                                        .GetRequiredService<IPlayerInfoRepository>();
                        await repo.UpsertAsync(dto);
                    }
                    catch (Exception ex)
                    {
                        _log.LogError(ex,
                            "Помилка при збереженні/оновленні гравця {Username}",
                            dto.Username);
                    }

                    var flag = string.IsNullOrWhiteSpace(dto.Flag) ? "" : $" {dto.Flag}";
                    var title = string.IsNullOrWhiteSpace(dto.Title) ? "" : dto.Title;

                    var text = $"""
                    ♟️ <b>{dto.Username}</b> <i>({title}{(title != "" && flag != "" ? ", " : "")}{flag})</i>

                    <b>Рейтинги</b>
                    ▫️ Rapid — <b>{dto.OnlineRatingRapid}</b>
                    ▫️ Blitz — <b>{dto.OnlineRatingBlitz}</b>
                    ▫️ Bullet — <b>{dto.OnlineRatingBullet}</b>

                    <b>📊 Зіграно партій:</b> {dto.GamesCount:N0}
                    <b>📅 Реєстрація:</b> {dto.CreatedAt:dd.MM.yyyy}
                    <b>🕒 Останній візит:</b> {dto.LastSeen:dd.MM.yyyy}

                    <a href="https://lichess.org/@/{dto.Username}">🌐 Перейти до профілю</a>
                    """;

                    await bot.SendMessage(
                        chatId: msg.Chat.Id,
                        text: text,
                        parseMode: ParseMode.Html,
                        cancellationToken: ct);
                }
                break;

            case "/deleteplayer":
                {
                    if (msg.Chat.Id != _adminChatId)
                    {
                        return;
                    }

                    if (parts.Length < 2)
                    {
                        await bot.SendMessage(
                            chatId: msg.Chat.Id,
                            text: "⚠️ Формат: /deleteplayer <нік>",
                            cancellationToken: ct);
                        break;
                    }

                    var usernameToDelete = parts[1];

                    try
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var repo = scope.ServiceProvider
                                        .GetRequiredService<IPlayerInfoRepository>();

                        var existing = await repo.GetByUsernameAsync(usernameToDelete);
                        if (existing is null)
                        {
                            await bot.SendMessage(
                                msg.Chat.Id,
                                $"❌ У базі немає гравця з іменем «{usernameToDelete}».",
                                cancellationToken: ct);
                        }
                        else
                        {
                            await repo.DeleteAsync(existing.Id);
                            await bot.SendMessage(
                                msg.Chat.Id,
                                $"🗑️ Гравець «{usernameToDelete}» (Id={existing.Id}) успішно видалений із БД.",
                                cancellationToken: ct);
                        }
                    }
                    catch (Exception ex)
                    {
                        _log.LogError(ex,
                            "Помилка при видаленні гравця {Username}",
                            usernameToDelete);
                        await bot.SendMessage(
                            msg.Chat.Id,
                            $"❌ Сталася помилка при спробі видалити «{usernameToDelete}».",
                            cancellationToken: ct);
                    }
                }
                break;

            case "/pgn":
                {
                    if (parts.Length < 2)
                    {
                        await bot.SendMessage(
                            chatId: msg.Chat.Id,
                            text: "⚠️ Формат: /pgn <нік> [кількість_партій]",
                            cancellationToken: ct);
                        break;
                    }

                    var username = parts[1];
                    var count = 5;                        

                    if (parts.Length == 3 && int.TryParse(parts[2], out var n) && n is > 0 and <= 50)
                        count = n;

                    var pgnLines = await _lichess.GetPlayerGamesPgnAsync(username, count);
                    
                    if (pgnLines is null || !pgnLines.Any())
                    {
                        await bot.SendMessage(
                            msg.Chat.Id,
                            "Не вдалося отримати PGN (нік хибний або партій немає)",
                            cancellationToken: ct);
                        break;
                    }

                    var pgn = string.Join("\n\n", pgnLines) + "\n";
                    
                    await using var ms = new MemoryStream(Encoding.UTF8.GetBytes(pgn));
                    ms.Position = 0;                           
                    
                    var inputFile = InputFile.FromStream(ms, $"{username}_last_{count}.pgn");

                    await bot.SendDocument(
                        chatId: msg.Chat.Id,
                        document: inputFile,
                        caption: $"📥 {username}: останні {count} партій",
                        cancellationToken: ct);
                }
                break;

            case "/fav":         
                {
                    if (parts.Length < 2)
                    {
                        await bot.SendMessage(
                            chatId: msg.Chat.Id,
                            text: "⚠️ Формат: /fav <нік>",
                            cancellationToken: ct);
                        break;
                    }

                    var username = parts[1];
                    var fav = await _lichess.GetPlayerFavoriteControlAsync(username);

                    if (fav.GamesCount == 0)
                    {
                        await bot.SendMessage(
                            msg.Chat.Id,
                            $"Не знайшов даних щодо любимого контролю для «{username}» 😔",
                            cancellationToken: ct);
                        break;
                    }

                    var text = $"""
                    🏆 <b>{username}</b>

                    <b>Улюблений контроль:</b> <i>{fav.TimeControl}</i>
                    ▫️ Рейтинг: <b>{fav.Rating}</b>
                    ▫️ Зіграно партій: <b>{fav.GamesCount:N0}</b>
                    """;

                    await bot.SendMessage(
                        chatId: msg.Chat.Id,
                        text: text,
                        parseMode: ParseMode.Html,
                        cancellationToken: ct);
                }
                break;

            case "/compare":
                {
                    if (parts.Length < 3)
                    {
                        await bot.SendMessage(
                            chatId: msg.Chat.Id,
                            text: "⚠️ Формат: /compare <нік1> <нік2>",
                            cancellationToken: ct);
                        break;
                    }

                    var req = new ComparePlayersRequest
                    {
                        Username1 = parts[1],
                        Username2 = parts[2]
                    };

                    var cmp = await _lichess.ComparePlayersAsync(req);

                    if (cmp is null)
                    {
                        await bot.SendMessage(
                            msg.Chat.Id,
                            "Не вдалося знайти одного з гравців 😔",
                            cancellationToken: ct);
                        break;
                    }

                    string Format(PlayerInfoDto p, PerformanceDto perf) => $"""
                    ♟️ <b>{p.Username}</b>
                    Rapid: <b>{p.OnlineRatingRapid}</b> | Blitz: <b>{p.OnlineRatingBlitz}</b> | Bullet: <b>{p.OnlineRatingBullet}</b>
                    W/D/L: {perf.Wins}/{perf.Draws}/{perf.Losses}  (Win rate: <b>{perf.WinRate:P0}</b>)
                    """;

                    var text = $"""
                        📊 <b>Порівняння гравців</b>

                        {Format(cmp.Player1, cmp.Perf1)}

                        {Format(cmp.Player2, cmp.Perf2)}
                        """;

                    await bot.SendMessage(
                        chatId: msg.Chat.Id,
                        text: text,
                        parseMode: ParseMode.Html,
                        cancellationToken: ct);
                }
                break;

            case "/perf":
                {
                    if (parts.Length < 2)
                    {
                        await bot.SendMessage(
                            msg.Chat.Id,
                            "⚠️ Формат:\n" +
                            "/perf <нік>\n" +
                            "/perf <нік> <днів>\n" +
                            "/perf <нік> <dd.MM.yyyy> <dd.MM.yyyy>",
                            cancellationToken: ct);
                        break;
                    }

                    var username = parts[1];
                    DateTime from, to;

                    if (parts.Length == 2)
                    {
                        to = DateTime.UtcNow;
                        from = to.AddDays(-30);
                    }
                    else if (parts.Length == 3 &&
                             int.TryParse(parts[2], out var days) &&
                             days is > 0 and <= 365)
                    {
                        to = DateTime.UtcNow;
                        from = to.AddDays(-days);
                    }
                    else if (parts.Length >= 4 &&
                             DateTime.TryParseExact(parts[2], "dd.MM.yyyy", null,
                                                     System.Globalization.DateTimeStyles.None, out from) &&
                             DateTime.TryParseExact(parts[3], "dd.MM.yyyy", null,
                                                     System.Globalization.DateTimeStyles.None, out to) &&
                             to >= from)
                    {
                    }
                    else
                    {
                        await bot.SendMessage(
                            msg.Chat.Id,
                            "❌ Неправильний формат дат або діапазону",
                            cancellationToken: ct);
                        break;
                    }

                    var perf = await _lichess.GetPlayerPerformanceAsync(username, from, to);

                    if (perf.TotalGames == 0)
                    {
                        await bot.SendMessage(
                            msg.Chat.Id,
                            "Немає партій у заданому проміжку 🕳️",
                            cancellationToken: ct);
                        break;
                    }

                    var text = $"""
                        📈 <b>{username}</b> <i>({from:dd.MM.yyyy} → {to:dd.MM.yyyy})</i>

                        Зіграно: <b>{perf.TotalGames}</b>
                        ✅ Перемог: <b>{perf.Wins}</b>
                        ➖ Нічиїх: <b>{perf.Draws}</b>
                        ❌ Поразок: <b>{perf.Losses}</b>
                        Win rate: <b>{perf.WinRate:P0}</b>
                        """;

                    await bot.SendMessage(
                        chatId: msg.Chat.Id,
                        text: text,
                        parseMode: ParseMode.Html,
                        cancellationToken: ct);
                }
                break;

            case "/openings":
                {
                    if (parts.Length < 2)
                    {
                        await bot.SendMessage(
                            msg.Chat.Id,
                            "⚠️ Формат:\n" +
                            "/openings <нік> [white|black] [top 1-10]",
                            cancellationToken: ct);
                        break;
                    }

                    var username = parts[1];

                    string? color = null;   
                    int top = 5;            

                    if (parts.Length >= 3)
                    {
                        var arg2 = parts[2].ToLower();

                        if (arg2 is "white" or "black")
                        {
                            color = arg2;
                            if (parts.Length == 4 &&
                                int.TryParse(parts[3], out var t) &&
                                t is > 0 and <= 10)
                                top = t;
                        }
                        else if (int.TryParse(arg2, out var t) &&
                                 t is > 0 and <= 10)
                        {
                            top = t;
                        }
                    }

                    var list = await _lichess
                        .GetPlayerOpeningsAsync(username, color, fetch: 100, top: top);

                    if (!list.Any())
                    {
                        await bot.SendMessage(
                            msg.Chat.Id,
                            "Дані по дебютах не знайдені 😔",
                            cancellationToken: ct);
                        break;
                    }

                    var header = $"""
                    📖 <b>{username}</b> — топ {top} дебютів
                    {(color is null ? "" : $"({color})")}
                    """;

                    var sb = new StringBuilder(header.Trim());
                    sb.AppendLine("\n");

                    int i = 1;
                    foreach (var o in list)
                    {
                        sb.AppendLine(
                            $"{i}. <b>{o.Name}</b> ({o.EcoCode}) —" +
                            $" {o.GamesCount} партій, Win rate: <b>{o.WinRate:P0}</b>");
                        i++;
                    }

                    await bot.SendMessage(
                        chatId: msg.Chat.Id,
                        text: sb.ToString(),
                        parseMode: ParseMode.Html,
                        cancellationToken: ct);
                }
                break;

            case "/chart":
                {
                    if (parts.Length < 3)
                    {
                        await bot.SendMessage(
                            msg.Chat.Id,
                            "⚠️ Формат:\n" +
                            "/chart <нік> <control>\n" +
                            "/chart <нік> <control> <днів>\n" +
                            "/chart <нік> <control> <dd.MM.yyyy> <dd.MM.yyyy>",
                            cancellationToken: ct);
                        break;
                    }

                    var username = parts[1];
                    var control = parts[2].ToLower();

                    DateTime from, to;

                    if (parts.Length == 3)
                    {
                        to = DateTime.UtcNow;
                        from = to.AddDays(-30);
                    }
                    else if (parts.Length == 4 &&
                             int.TryParse(parts[3], out var days) &&
                             days is > 0 and <= 365)
                    {
                        to = DateTime.UtcNow;
                        from = to.AddDays(-days);
                    }
                    else if (parts.Length >= 5 &&
                             DateTime.TryParseExact(parts[3], "dd.MM.yyyy", null,
                                                     DateTimeStyles.None, out from) &&
                             DateTime.TryParseExact(parts[4], "dd.MM.yyyy", null,
                                                     DateTimeStyles.None, out to) &&
                             to >= from)
                    {
                    }
                    else
                    {
                        await bot.SendMessage(
                            msg.Chat.Id,
                            "❌ Неправильний формат проміжку",
                            cancellationToken: ct);
                        break;
                    }

                    var png = await _lichess.GetRatingHistoryChartAsync(username, control, from, to);
                    if (png.Length == 0)
                    {
                        await bot.SendMessage(
                            msg.Chat.Id,
                            "Дані для графіка не знайдені 😔",
                            cancellationToken: ct);
                        break;
                    }

                    await using var ms = new MemoryStream(png);
                    var file = InputFile.FromStream(ms, $"{username}_{control}.png");

                    await bot.SendPhoto(
                        chatId: msg.Chat.Id,
                        photo: file,
                        caption: $"📈 {username} — {control} ({from:dd.MM} → {to:dd.MM})",
                        cancellationToken: ct);
                }
                break;

            case "/randomgame":
                {
                    var rnd = new Random();
                    var chosenUser = TopBlitzUsers[rnd.Next(TopBlitzUsers.Length)];

                    var requestUrl =
                        $"https://lichess.org/api/games/user/{chosenUser}" +
                        "?max=1&perfType=blitz&moves=true&opening=false&pgnInJson=true";

                    try
                    {
                        using var http = new HttpClient();                 

                        var req = new HttpRequestMessage(HttpMethod.Get, requestUrl);
                        req.Headers.Accept.Add(
                            new MediaTypeWithQualityHeaderValue("application/x-ndjson"));
                        req.Headers.Authorization =
                            new AuthenticationHeaderValue("Bearer", _lichessToken);
                        req.Headers.UserAgent.ParseAdd("RandomGameBot (+mailto:me@example.com)");

                        var resp = await http.SendAsync(req, ct);

                        if (resp.StatusCode == HttpStatusCode.NotFound)
                        {
                            await bot.SendMessage(msg.Chat.Id,
                                $"⚠️ Гравця {chosenUser} не знайдено.", cancellationToken: ct);
                            break;
                        }
                        if (resp.StatusCode == (HttpStatusCode)429)
                        {
                            await bot.SendMessage(msg.Chat.Id,
                                "⏳ Багато запитів – спробуйте трохи пізніше.",
                                cancellationToken: ct);
                            break;
                        }
                        if (!resp.IsSuccessStatusCode)
                        {
                            await bot.SendMessage(msg.Chat.Id,
                                $"⚠️ Не вдалося отримати партію гравця {chosenUser}.",
                                cancellationToken: ct);
                            break;
                        }

                        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
                        using var reader = new StreamReader(stream);
                        var line = await reader.ReadLineAsync();  
                        using var doc = JsonDocument.Parse(line);

                        if (string.IsNullOrWhiteSpace(line))
                        {
                            await bot.SendMessage(msg.Chat.Id,
                                $"⚠️ У {chosenUser} ще немає blitz-партій.",
                                cancellationToken: ct);
                            break;
                        }

                        if (!doc.RootElement.TryGetProperty("id", out var idElem))
                        {
                            await bot.SendMessage(msg.Chat.Id,
                                "⚠️ Не вдалося знайти ID партії.", cancellationToken: ct);
                            break;
                        }

                        var gameId = idElem.GetString();
                        if (string.IsNullOrWhiteSpace(gameId))
                        {
                            await bot.SendMessage(msg.Chat.Id,
                                "⚠️ Некоректний ID партії.", cancellationToken: ct);
                            break;
                        }

                        var link = $"https://lichess.org/{gameId}";

                        await bot.SendMessage(
                            chatId: msg.Chat.Id,
                            text: link,
                            cancellationToken: ct);
                    }
                    catch (Exception ex)
                    {
                        _log.LogError(ex, "randomgame error for {User}", chosenUser);
                        await bot.SendMessage(msg.Chat.Id,
                            "❌ Непередбачена помилка. Спробуйте пізніше.",
                            cancellationToken: ct);
                    }
                }
                break;

        }
    }

    private Task HandleErrorAsync(ITelegramBotClient bot, Exception ex, CancellationToken _)
    {
        var err = ex switch
        {
            ApiRequestException api => $"Telegram API Error:\n[{api.ErrorCode}] {api.Message}",
            _ => ex.ToString()
        };
        _log.LogError(err);
        return Task.CompletedTask;
    }
}
