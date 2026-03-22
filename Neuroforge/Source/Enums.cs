using Godot;

// Estado general del juego
public enum GameState
{
    DEPLOYMENT,
    WAITING_INPUT,
    PIECE_SELECTED,
    GAME_OVER
}

// Resultado de combate entre piezas
public enum CombatResult
{
    ATTACKER_DIES,
    DEFENDER_DIES,
    BOTH_DIE
}

// Propietario de la pieza
public enum PieceOwner
{
    PLAYER,
    BOT
}

// Tipo de pieza
public enum PieceType
{
    ENERGY_CORE,
    TURRET,
    CORE,
    GUARD,
    MECHA,
    ANDROID,
    COMBAT_UNIT,
    ARMORER,
    SOLDIER,
    SABOTEUR,
    SCOUT,
    PHANTOM
}

// Estado de una pieza
public enum PieceState
{
    HIDDEN,
    REVEALED,
    DESTROYED
}

public enum BotKnowledgeState
{
    UNKNOWN,
    KNOWN
}

// Tipo de tile
public enum TileType
{
    PASSABLE,
    NO_PASSABLE,
    PLAYER_DEPLOYMENT,
    BOT_DEPLOYMENT
}

// Acción posible sobre un tile
public enum TileAction
{
    NONE,
    MOVE,
    ATTACK
}