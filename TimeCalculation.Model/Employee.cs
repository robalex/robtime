namespace TimeCalculation.Model;

public class Employee
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string MiddleName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Salutation { get; set; } = string.Empty;
    public string PostNominalLetters { get; set; } = string.Empty;
    public decimal MinimumWage { get; set; }
    public string HomeTimeZoneId { get; set; } = "America/New_York";
    public string State { get; set; } = string.Empty;
    public bool IsDeleted { get; set; }
}
