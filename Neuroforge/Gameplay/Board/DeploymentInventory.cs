using Godot;
using System;
using System.Collections.Generic;

public class DeploymentInventory
{
    public Dictionary<PieceType, int> Remaining = new();

    public DeploymentInventory()
    {
        foreach (var kv in PiecesData.Data)
        {
            Remaining[kv.Key] = kv.Value.MaxCount;
        }
    }

    public bool CanPlace(PieceType type)
        => Remaining[type] > 0;

    public void Use(PieceType type)
        => Remaining[type]--;

    public void Return(PieceType type)
        => Remaining[type]++;
}
