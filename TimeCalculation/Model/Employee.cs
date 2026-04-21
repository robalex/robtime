namespace TimeCalculation.Model;

public class Employee
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string MiddleName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Salutation { get; set; } = string.Empty;
    public string PostNominalLetters { get; set; } = string.Empty;
    public double MinimumWage { get; set; }
}
