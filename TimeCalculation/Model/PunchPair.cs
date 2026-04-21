namespace TimeCalculation.Model;

public class PunchPair
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }

    public Punch? InPunch { get; set; }
    public Punch? OutPunch { get; set; }

    // Computed property - no setter needed
    public double TotalHours => (InPunch != null && OutPunch != null)
        ? (OutPunch.PunchTime - InPunch.PunchTime).TotalHours
        : 0;

    public DateOnly PairDate { get; set; }
    public double BasePay { get; set; }
    public double Differential { get; set; }

    public bool IsMissingPunch()
    {
        return InPunch == null || OutPunch == null;
    }
}

