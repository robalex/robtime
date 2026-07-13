namespace TimeCalculation.Model;

public class Position
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal BaseRate { get; set; }
}
