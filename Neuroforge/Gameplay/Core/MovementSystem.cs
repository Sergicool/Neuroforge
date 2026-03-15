using Godot;

public static class MovementSystem
{
    // Se obtiene la accion que se puede llevar a cabo en una casilla
    public static TileAction GetAction(Piece piece, Tile target)
    {
        if (!CanMove(piece, target)) return TileAction.NONE;
        
        return target.IsOccupied ? TileAction.ATTACK : TileAction.MOVE;
    }

    // Determina si una pieza se puede mover a una casilla
    public static bool CanMove(Piece piece, Tile target)
    {
        // No puede moverse a una casilla intransitable o que tenga una pieza del mismo equipo
        if (!piece.CanMove || target.TileType == TileType.NO_PASSABLE) return false;
        if (target.IsOccupied && target.Occupant.PlayerOwner == piece.PlayerOwner) return false;

        Vector2I from = piece.CurrentTile.GridPosition;
        Vector2I to = target.GridPosition;

        // Movimiento especial SCOUT: Cualquier casilla en horizontal, hasta encontrarse con una obstaculo (pieza aliada, enemiga o casilla intransitable)
        if (piece.Type == PieceType.SCOUT) return IsScoutPathValid(piece.CurrentTile, target);

        // Movimiento normal: 1 casilla en las 4 direcciones
        int dx = Mathf.Abs(from.X - to.X);
        int dy = Mathf.Abs(from.Y - to.Y);
        return dx + dy == 1;
    }

    // Determina si una casilla esta en el posible movimiento de un SCOUT
    private static bool IsScoutPathValid(Tile from, Tile to)
    {
        Vector2I start = from.GridPosition;
        Vector2I end = to.GridPosition;

        if (start.X != end.X && start.Y != end.Y) return false;

        Vector2I dir = (end - start).Sign();
        Vector2I current = start + dir;
        Board board = GameManager.Instance.GetBoard();

        while (true)
        {
            Tile tile = board.GetTileAt(current);
            if (tile == null) return false;
            if (tile.TileType == TileType.NO_PASSABLE) return false;

            if (tile.IsOccupied)
            {
                if (tile.Occupant.PlayerOwner == from.Occupant.PlayerOwner) return false;
                if (current != end) return false;
            }

            if (current == end) return true;
            current += dir;
        }
    }
}