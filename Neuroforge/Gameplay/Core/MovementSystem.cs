using Godot;

public static class MovementSystem
{
    public static TileAction GetAction(Piece piece, Tile target)
    {
        if (!CanMove(piece, target))
            return TileAction.NONE;

        return target.IsOccupied ? TileAction.ATTACK : TileAction.MOVE;
    }

    public static bool CanMove(Piece piece, Tile target)
    {
        if (!piece.CanMove)
            return false;

        if (target.TileType == TileType.NO_PASSABLE)
            return false;

        if (target.IsOccupied && target.Occupant.PlayerOwner == piece.PlayerOwner)
            return false;

        Vector2I from = piece.CurrentTile.GridPosition;
        Vector2I to = target.GridPosition;

        // SCOUT: movimiento libre en línea recta
        if (piece.Type == PieceType.SCOUT)
            return IsScoutPathValid(piece.CurrentTile, target);

        // Resto: 1 casilla ortogonal
        int dx = Mathf.Abs(from.X - to.X);
        int dy = Mathf.Abs(from.Y - to.Y);
        return dx + dy == 1;
    }

    // ================= SCOUT =================

    private static bool IsScoutPathValid(Tile from, Tile to)
    {
        Vector2I start = from.GridPosition;
        Vector2I end = to.GridPosition;

        // Debe ser línea recta
        if (start.X != end.X && start.Y != end.Y)
            return false;

        Vector2I dir = (end - start).Sign();
        Vector2I current = start + dir;

        Board board = GameManager.Instance.GetBoard();

        while (true)
        {
            Tile tile = board.GetTileAt(current);

            if (tile == null)
                return false;

            // Casilla intransitable
            if (tile.TileType == TileType.NO_PASSABLE)
                return false;

            // Pieza aliada bloquea
            if (tile.IsOccupied && tile.Occupant.PlayerOwner == from.Occupant.PlayerOwner)
                return false;

            // Si llegamos al destino
            if (current == end)
            {
                // Vacía → mover
                if (!tile.IsOccupied)
                    return true;

                // Enemiga → atacar
                return true;
            }

            // Enemigo antes del destino → no se puede pasar
            if (tile.IsOccupied && tile.Occupant.PlayerOwner != from.Occupant.PlayerOwner)
                return false;

            current += dir;
        }
    }
}