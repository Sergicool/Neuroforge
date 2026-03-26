using Godot;

public static class MovementSystem
{
    // Devuelve la accion que se puede llevar a cabo sobre una casilla
    public static TileAction GetAction(Piece piece, Tile target, int turn, Board board)
    {
        if (!CanMove(piece, target, turn, board)) return TileAction.NONE;

        return target.IsOccupied ? TileAction.ATTACK : TileAction.MOVE;
    }

    // Determina si una pieza puede moverse a una casilla
    public static bool CanMove(Piece piece, Tile target, int turn, Board board)
    {
        if (!piece.CanMove || target.TileType == TileType.NO_PASSABLE) return false;

        // No puede moverse a una casilla ocupada por una pieza aliada
        if (target.IsOccupied && target.Occupant.PlayerOwner == piece.PlayerOwner) return false;

        // La pieza no puede volver a la misma casilla hasta dentro de 3 turnos,
        // salvo que pueda atacar una pieza rival en ella
        bool isAttack = target.IsOccupied && target.Occupant.PlayerOwner != piece.PlayerOwner;
        if (!isAttack && !piece.CanReturnToTile(target.GridPosition, turn)) return false;

        // Movimiento especial SCOUT: cualquier distancia en línea recta horizontal o vertical
        if (piece.Type == PieceType.SCOUT) return IsScoutPathValid(piece.CurrentTile, target, board);

        // Movimiento normal: 1 casilla en las 4 direcciones cardinales
        Vector2I from = piece.CurrentTile.GridPosition;
        Vector2I to   = target.GridPosition;
        int dx = Mathf.Abs(from.X - to.X);
        int dy = Mathf.Abs(from.Y - to.Y);
        return dx + dy == 1;
    }

    // Determina si el camino en línea recta del SCOUT hasta el destino es válido
    private static bool IsScoutPathValid(Tile from, Tile to, Board board)
    {
        Vector2I start = from.GridPosition;
        Vector2I end   = to.GridPosition;

        if (start.X != end.X && start.Y != end.Y) return false;

        Vector2I dir     = (end - start).Sign();
        Vector2I current = start + dir;

        while (true)
        {
            Tile tile = board.GetTileAt(current);
            if (tile == null) return false;
            if (tile.TileType == TileType.NO_PASSABLE) return false;

            if (tile.IsOccupied)
            {
                // Aliado en el camino: bloquea completamente
                if (tile.Occupant.PlayerOwner == from.Occupant.PlayerOwner) return false;
                // Enemigo: solo puede atacar si es el destino final
                if (current != end) return false;
            }

            if (current == end) return true;
            current += dir;
        }
    }
}