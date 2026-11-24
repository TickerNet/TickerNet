namespace TickerNet.EntityFrameworkCore.Customizer;

/// <summary>
/// Whether TickerNet adds its entities to the application's EF Core model.
/// </summary>
public enum ConfigurationType
{
    /// <summary>
    /// TickerNet adds its three entities to the application's model.
    /// </summary>
    UseModelCustomizer,

    /// <summary>
    /// The application applies TickerNet's entity configurations itself in
    /// <c>OnModelCreating</c>.
    /// </summary>
    IgnoreModelCustomizer,
}