namespace ContactCenter.Api.Ari;

public sealed class AriOptions
{
    public const string SectionName = "Ari";

    public string BaseUrl { get; set; } = "http://localhost:8088/ari/";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string AppName { get; set; } = "contactcenter";
}
