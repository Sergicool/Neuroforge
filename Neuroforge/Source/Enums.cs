using Godot;

// Estado general del juego
public enum GameState
{
    DEPLOYMENT,
    WAITING_INPUT,
    EXECUTING_ACTION,
    COMBAT,
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
    NEXUS,
    TURRET,
    CORE,
    NOVA,
    MECHA,
    SENTINEL,
    CANINE,
    CYBORG,
    SOLDIER,
    SABOTEUR,
    SCOUT,
    PHANTOM
}

// Estado de una pieza
public enum PieceState
{
    REVEALED_FOR_PLAYER,
    REVEALED_FOR_BOT,
    REVEALED_FOR_BOTH
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