using NodaTime;

namespace TimeCalculation.Model;

public class Punch
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public Instant PunchTime { get; set; } // NodaTime
    public PunchType PunchType { get; set; }
    public DateTime CreatedDate { get; set; }
    public string CreatedBy { get; set; } = string.Empty;

    public required Employee Employee { get; set; }
}
