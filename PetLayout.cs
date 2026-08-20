namespace QuotaWisp;

public sealed record PetLayout(
    double WindowWidth,
    double WindowHeight,
    double OrbitSize,
    double OrbitRadius,
    double MoonSize,
    double SatelliteScale)
{
    public static PetLayout For(PetSize size) => size switch
    {
        PetSize.Small => new(280, 240, 220, 78, 123.2, 0.82),
        PetSize.Large => new(440, 400, 380, 145, 228.8, 1.18),
        _ => new(360, 320, 300, 112, 176, 1)
    };
}
