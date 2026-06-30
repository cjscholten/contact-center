namespace ContactCenter.Api.Auth;

/// <summary>
/// Houdt de tenant van de huidige uitvoeringsstroom vast. AsyncLocal i.p.v. een scoped service,
/// zodat het ook werkt met de singleton <c>IDbContextFactory</c> (die contexts buiten een
/// request-scope maakt) en met de SignalR-hub. De tenant-middleware zet de waarde per request;
/// achtergrondprocessen (inbound-telefonie) laten 'm <c>null</c> en omzeilen de query-filter
/// bewust met <c>IgnoreQueryFilters()</c>.
/// </summary>
public interface ITenantAccessor
{
    /// <summary>De tenant-id van de huidige stroom, of <c>null</c> buiten een tenant-context.</summary>
    int? TenantId { get; set; }
}

public sealed class TenantAccessor : ITenantAccessor
{
    private static readonly AsyncLocal<int?> Current = new();

    public int? TenantId
    {
        get => Current.Value;
        set => Current.Value = value;
    }
}
