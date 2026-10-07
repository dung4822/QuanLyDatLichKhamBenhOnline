namespace WebDatLichKhamBenh.Infrastructure.AI;

public class DeepSeekSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "deepseek-flash";
    public int MaxOutputTokens { get; set; } = 1024;
}
