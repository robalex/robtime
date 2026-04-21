namespace TimeCalculation.Model;

public class PayRule
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public double MaxShiftLengthHours { get; set; } = 15;
    public double DistanceBetweenShiftsHours { get; set; } = 6;
}
