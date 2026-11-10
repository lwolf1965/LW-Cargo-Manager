using Keen.VRage.Library.Mathematics;

namespace ClientPlugin.Game;

/// <summary>
/// Conversions for the game's FixedPoint number type, kept in one place.
/// If FixedPoint has no explicit cast to double in your game version, fix it only here.
/// </summary>
internal static class Fx
{
    public static double ToDouble(FixedPoint value) => (double)value;
}
