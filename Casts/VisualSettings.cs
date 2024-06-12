namespace RuneLover.Casts;

public class VisualSettings
{
    public Vector3 Scale = new(1, 1, 1);

    public Color MainColor = Color.white;

    private Color? _secondaryColor = null;

    public Color SecondaryColor
    {
        get => _secondaryColor ?? MainColor;
        set => _secondaryColor = value;
    }

    private Color? _lightColor = null;

    public Color LightColor
    {
        get => _lightColor ?? SecondaryColor;
        set => _lightColor = value;
    }

    private Color? _particleColor = null;

    public Color ParticleColor
    {
        get => _particleColor ?? LightColor;
        set => _particleColor = value;
    }

    private Color? _particleColor2 = null;

    public Color ParticleColor2
    {
        get => _particleColor2 ?? ParticleColor;
        set => _particleColor2 = value;
    }
}

public static class ColorExt
{
    public static Color FromVector3(this Vector3 v) => new(v.x, v.y, v.z);
    public static Vector3 ToVector3(this Color c) => new(c.r, c.g, c.b);
}