namespace DshDesktop.DeepLink;

public enum DeepLinkAction
{
    Launch,
    Session,
}

public sealed record DeepLinkRequest(DeepLinkAction Action, string? SessionId)
{
    public static bool TryParse(string arg, out DeepLinkRequest request)
    {
        request = new DeepLinkRequest(DeepLinkAction.Launch, null);
        if (string.IsNullOrWhiteSpace(arg)) return false;

        var rest = arg.Trim();
        const string prefix = "dsh-desktop:";
        if (!rest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        rest = rest[prefix.Length..].TrimStart('/');

        if (rest.Length == 0) return true; // 裸 dsh-desktop: / dsh-desktop://
        if (rest.StartsWith("session/", StringComparison.OrdinalIgnoreCase))
        {
            var id = rest["session/".Length..];
            if (id.Length == 0) return false;
            request = new DeepLinkRequest(DeepLinkAction.Session, id);
            return true;
        }
        return rest.StartsWith("launch", StringComparison.OrdinalIgnoreCase);
    }
}
