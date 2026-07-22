namespace TimeCalculation.Model;

public enum ShiftDateStrategy
{
    FirstPunchLocalDate,      // Date of the first In punch in the shift's local timezone
    LastPunchLocalDate,       // Date of the last Out punch (or last In if no Out) in the shift's local timezone
    MajorityHoursLocalDate,   // Date on which the majority of shift hours fall
    SplitAtMidnight,          // Split a shift that crosses local midnight into one shift per calendar day it touches, each dated to its own day
}
