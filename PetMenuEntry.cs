namespace QuotaWisp;

// Both native menu surfaces render the same commands and current settings.
public sealed record PetMenuEntry(string Text, Action? Action = null, bool Enabled = true,
    bool? Checked = null, List<PetMenuEntry>? Children = null)
{
    public bool IsSeparator => Text.Length == 0;
    public static PetMenuEntry Separator() => new("");
}
