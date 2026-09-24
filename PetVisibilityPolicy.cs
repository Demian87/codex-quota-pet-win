namespace QuotaWisp;

public static class PetVisibilityPolicy
{
    public static bool ShouldShow(bool manuallyVisible, bool fullscreenSuppressed, bool codexOnly, bool codexActive) =>
        manuallyVisible && !fullscreenSuppressed && (!codexOnly || codexActive);
}
