namespace TimeCalculation.Model;

public class Week
{
    public Week() { Shifts = []; }
    public Week(List<Shift> shifts) { Shifts = shifts; }

    public List<Shift> Shifts { get; set; }
    public decimal NonDiscretionaryBonus { get; set; }
}
