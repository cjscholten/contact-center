namespace ContactCenter.Api.Ami;

public sealed class AmiOptions
{
    public const string SectionName = "Ami";

    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5038;
    public string Username { get; set; } = "";
    public string Secret { get; set; } = "";
}
