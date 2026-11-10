#nullable enable

using System.Text.Json;
using Capability39.SocialTasks;

internal sealed class PlaySocialInbox
{
    private const int Capacity = 8;
    private const double HistorySeconds = 30;
    private readonly List<JsonElement> messages = new();

    public void Add(JsonElement message) => messages.Add(message.Clone());

    public void RetireOffer(string offer)
    {
        messages.RemoveAll(message => IsOffer(message)
            && message.TryGetProperty("offerReference", out var reference) && reference.GetString() == offer);
    }

    public JsonElement Snapshot(double gameTime)
    {
        messages.RemoveAll(message => gameTime - message.GetProperty("gameTime").GetDouble()
            >= (IsOffer(message) ? SocialTaskFamily.NativeOfferLifetimeSeconds : HistorySeconds));
        while (messages.Count > Capacity)
        {
            // 正在等待回应的交接优先于旧发言，避免一条长消息把交接挤出菜单。
            int history = messages.FindIndex(message => !IsOffer(message));
            messages.RemoveAt(history >= 0 ? history : 0);
        }
        return JsonSerializer.SerializeToElement(messages);
    }

    private static bool IsOffer(JsonElement message) => message.GetProperty("kind").GetString() == "itemOffered";
}
