using Godot;
using System.Collections.Generic;

// Caracteristicas que definen el comportamiento de una pieza
public struct PieceDefinition
{
    public PieceType Type;
    public int Rank;
    public bool CanMove;
    public int MaxCount;
    public int AtlasColumn;
}

// Datos de piezas
public partial class PiecesData
{
    public static readonly Texture2D Atlas = GD.Load<Texture2D>("res://assets/board/PiecesAtlas.png");
    public const int HIDDEN_ATLAS_COLUMN = 12;

    public const int ATLAS_COLUMN_WIDTH = 32;
    public const int ATLAS_HEIGHT = 64;

    public static readonly Dictionary<PieceType, PieceDefinition> Data = new()
    {
        { PieceType.TURRET,      new PieceDefinition { Type = PieceType.TURRET, Rank = 0, CanMove=false, MaxCount=6, AtlasColumn=11 } },
        { PieceType.CORE,        new PieceDefinition { Type = PieceType.CORE, Rank=10, CanMove=true, MaxCount=1, AtlasColumn=10 } },
        { PieceType.NOVA,        new PieceDefinition { Type = PieceType.NOVA, Rank=9, CanMove=true, MaxCount=1, AtlasColumn=9 } },
        { PieceType.MECHA,       new PieceDefinition { Type = PieceType.MECHA, Rank=8, CanMove=true, MaxCount=2, AtlasColumn=8 } },
        { PieceType.SENTINEL,     new PieceDefinition { Type = PieceType.SENTINEL, Rank=7, CanMove=true, MaxCount=3, AtlasColumn=7 } },
        { PieceType.CANINE,      new PieceDefinition { Type = PieceType.CANINE, Rank=6, CanMove=true, MaxCount=4, AtlasColumn=6 } },
        { PieceType.CYBORG,      new PieceDefinition { Type = PieceType.CYBORG, Rank=5, CanMove=true, MaxCount=4, AtlasColumn=5 } },
        { PieceType.SOLDIER,     new PieceDefinition { Type = PieceType.SOLDIER, Rank=4, CanMove=true, MaxCount=4, AtlasColumn=4 } },
        { PieceType.SABOTEUR,    new PieceDefinition { Type = PieceType.SABOTEUR, Rank=3, CanMove=true, MaxCount=5, AtlasColumn=3 } },
        { PieceType.SCOUT,       new PieceDefinition { Type = PieceType.SCOUT, Rank=2, CanMove=true, MaxCount=8, AtlasColumn=2 } },
        { PieceType.PHANTOM,     new PieceDefinition { Type = PieceType.PHANTOM, Rank=1, CanMove=true, MaxCount=1, AtlasColumn=1 } },
        { PieceType.NEXUS,       new PieceDefinition { Type = PieceType.NEXUS, Rank = 0, CanMove=false, MaxCount=1, AtlasColumn=0 } }
    };
}