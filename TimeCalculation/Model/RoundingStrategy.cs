namespace TimeCalculation.Model;

public enum RoundingStrategy
{
    None,
    NearestInterval,       // Round to nearest N-minute interval always
    QuarterHourWithGrace,  // Round only when within grace-minute window of a boundary
}
