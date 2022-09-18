namespace LavaCat;

struct HeatProperties
{
    public float? DryTemp { get; set; }
    public float Conductivity { get; set; }
    public float EatSpeed { get; set; }
    public bool IsEdible => EatSpeed > 0;
}
