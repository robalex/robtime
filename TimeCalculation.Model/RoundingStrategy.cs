namespace TimeCalculation.Model;

public enum RoundingStrategy
{
    None,
    NearestInterval,       // Round to nearest N-minute interval always
    IntervalWithGrace,  // Round only when within grace-minute window of a boundary
}
