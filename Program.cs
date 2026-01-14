global using Tmds.DBus.Protocol;
using System.Diagnostics;
using System.Web;
using DiscordRPC;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using strawberry.DBus;


var discordRpc = new DiscordRpcClient("1307839840422858784");
discordRpc.Initialize();

while (Address.Session is null)
    Thread.Sleep(1000);

var connection = new Connection(Address.Session);
await connection.ConnectAsync();

var service = new strawberryService(connection, "org.mpris.MediaPlayer2.strawberry");
var playerProxy = service.CreatePlayer("/org/mpris/MediaPlayer2");

(string title, string? url)? cachedThumbnail = null;

while (true)
{
    await Task.Delay(5_000);

    try
    {
        var metadata = await playerProxy.GetMetadataAsync();
        var playbackStatus = await playerProxy.GetPlaybackStatusAsync();
        var position = await playerProxy.GetPositionAsync();

        if (playbackStatus != "Playing" || metadata is null)
        {
            discordRpc.ClearPresence();
            continue;
        }

        var artist = metadata.TryGetValue("xesam:artist", out var _artist) ? string.Join(", ", _artist.GetArray<string>()) : "Unknown Artist";
        var title = metadata.TryGetValue("xesam:title", out var _title) ? _title.GetString() : "Unknown Title";
        if (title.Contains("Otographic") && !title.Contains("[otographic"))
            title = metadata.TryGetValue("xesam:url", out var _url) ? HttpUtility.UrlDecode(Path.GetFileName(_url.GetString())) : title;

        var album = metadata.TryGetValue("xesam:album", out var _album) ? _album.GetString() : "Unknown Album";
        long? length =
            metadata.TryGetValue("mpris:length", out var _length1) ? _length1.GetInt64()
            : metadata.TryGetValue("xesam:length", out var _length2) ? _length2.GetInt64()
            : null;

        if (cachedThumbnail?.title != title)
        {
            string? url = null;
            try
            {
                url = await getThumbnailUrl(title);
            }
            catch { }

            cachedThumbnail = (title, url);
        }

        // Kenji Sekiguchi & Nhato - Otographic Arts 091 2017-07-04 [otographic_kenji-sekiguchi-nhato-otographic-arts-091-2017-07-04].opus
        title = Path.ChangeExtension(title, null);
        if (title.Contains("[otographic"))
            title = title.Split('[')[0].TrimEnd();

        if (length is { } len)
        {
            var duration = TimeSpan.FromMicroseconds(len);
            if (duration.Hours > 0)
                title += $" ({duration:hh\\:mm\\:ss})";
            else
                title += $" ({duration:mm\\:ss})";
        }


        discordRpc.SetPresence(new RichPresence()
        {
            Details = title,
            State = $"by {artist}",
            Assets = new Assets()
            {
                LargeImageKey = cachedThumbnail?.url ?? "main",
                LargeImageText = album,
            },
            Timestamps = new Timestamps()
            {
                Start = DateTime.UtcNow - TimeSpan.FromMicroseconds(position),
            }
        });
    }
    catch (Exception ex)
    {
        if (Debugger.IsAttached)
            Console.WriteLine($"Error fetching MPRIS data: {ex.Message}");

        discordRpc.ClearPresence();
    }
}


static async Task<string?> getThumbnailUrl(string name)
{
    if (name.Contains("[otographic", StringComparison.Ordinal))
    {
        const string query = """
            query cloudcastQuery($lookup: CloudcastLookup!) {
                cloudcast: cloudcastLookup(lookup: $lookup) {
                    picture {
                        urlRoot
                    }
                }
            }
            """;

        var content = new StringContent(JsonConvert.SerializeObject(new
        {
            id = "cloudcastQuery",
            query,
            variables = new
            {
                lookup = new
                {
                    username = "otographic",
                    slug = name.Split("[otographic_")[1].Split(']')[0], // "aran-otographic-arts-150-2022-06-12"
                }
            }
        }))
        { Headers = { ContentType = new("application/json") } };

        using var response = await new HttpClient().PostAsync("https://app.mixcloud.com/graphql", content);

        var json = await response.Content.ReadAsStringAsync();
        var urlpart = JObject.Parse(json)?["data"]?["cloudcast"]?["picture"]?["urlRoot"]?.Value<string>();
        return $"https://thumbnailer.mixcloud.com/unsafe/378x378/{urlpart}";
    }

    return null;
}
