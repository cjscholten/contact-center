namespace ContactCenter.Api.Data;

/// <summary>Een doorverbind-bestemming buiten de agents: naam + nummer (E.164), optioneel een afdeling.</summary>
public class Contact
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Number { get; set; }
    public string? Department { get; set; }
}
