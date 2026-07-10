namespace TimeCalculation.Calculation.Premiums;

/// <summary>
/// Resolves premium rule codes (from PayRule.ActivePremiumCodes) to rule instances.  Adding a state
/// means registering one entry here.  In a DI setup these would be container-registered singletons;
/// the static map keeps the pure-library core dependency-free.
/// </summary>
public static class PremiumRegistry
{
    private static readonly IReadOnlyDictionary<string, IPremiumRule> Rules =
        new IPremiumRule[]
        {
            new CaMealPremiumRule(),
            new CaRestPremiumRule(),
            new CoRestPremiumRule(),
            new PrMealPremiumRule(),
            new OrMealPremiumRule(),
            new WaMealPremiumRule(),
        }.ToDictionary(r => r.Code);

    public static IReadOnlyList<IPremiumRule> Resolve(IEnumerable<string> codes) =>
        codes.Where(Rules.ContainsKey).Select(c => Rules[c]).ToList();

    public static IPremiumRule Get(string code) => Rules[code];

    public static IReadOnlyCollection<string> AllCodes => Rules.Keys.ToList();
}
