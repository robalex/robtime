using NodaTime;

namespace TimeCalculation.Model;

public class Shift
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public List<PunchPair> PunchPairs { get; set; } = new();
}
