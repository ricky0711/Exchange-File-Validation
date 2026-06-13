namespace ExchangeFileValidator.Services;

public enum ContainerNeeded { None, Normal, Secure }

/// <summary>Recommended container for a demand + why.</summary>
public sealed record ContainerDecision(ContainerNeeded Needed, string Reason);

/// <summary>
/// Part D — a container frame exists for (a) busload reduction or (b) ASIL/security. Pure decision:
///  • ASIL + crosses the CGW/PIU gateway ⇒ <b>Secure</b> container (*SC_FD, MAC=x).
///  • ASIL but stays on a single FD channel ⇒ <b>Normal</b> container (*C_FD) is sufficient.
///  • No ASIL ⇒ <b>Normal</b> container, busload-driven (optional).
/// </summary>
public sealed class ContainerDecisionService
{
    public ContainerDecision Decide(bool asilRequested, bool crossesGateway, bool fdOnly)
    {
        if (asilRequested)
            return crossesGateway
                ? new ContainerDecision(ContainerNeeded.Secure, "asil")
                : new ContainerDecision(ContainerNeeded.Normal, "asil");

        // No ASIL: containerising is optional and only to reduce busload.
        return new ContainerDecision(ContainerNeeded.Normal, "busload");
    }
}
