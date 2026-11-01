namespace CurePlay.Models;
public sealed class AppSettings
{
    public bool SmartBoostEnabled { get; set; } = true;
    public int CheckIntervalMinutes { get; set; } = 5;
    public string[] GameExecutables { get; set; } = ["cs2", "valorant-win64-shipping", "fortniteclient-win64-shipping", "eldenring", "r5apex", "gta5", "cyberpunk2077", "overwatch", "league of legends", "minecraft", "javaw"];
}
