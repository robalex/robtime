using TimeCalculation.Model;

namespace TimeCalculation.Pipeline.Differentials;

internal readonly record struct DifferentialCandidate(DifferentialRule Rule, AppliedDifferential Applied);
