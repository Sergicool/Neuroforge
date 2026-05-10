using Godot;
using System;

public struct PieceDefinition
{
    public PieceType Type;
    public int Rank;
    public bool CanMove;
    public int MaxCount;
    public int AtlasColumn;
}
