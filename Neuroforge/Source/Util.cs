using Godot;
using System.Collections.Generic;

public static class Util
{
    public static void Shuffle<T>(List<T> list)
    {
        RandomNumberGenerator rng = new();
        rng.Randomize();

        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.RandiRange(0, i);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}