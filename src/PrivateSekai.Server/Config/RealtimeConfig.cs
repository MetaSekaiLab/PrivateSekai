namespace PrivateSekai.Config;

public sealed class RealtimeConfig
{
    public bool Enabled { get; set; }
    public string ListenAddress { get; set; } = "127.0.0.1";
    public string AdvertisedHost { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 8832;
    public int IdleTimeoutSeconds { get; set; } = 60;
}
