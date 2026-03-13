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

        // Movimiento especial del Scout, linea vertical y horizontal
        if (piece.Type == PieceType.SCOUT)
            return IsScoutPathValid(piece.CurrentTile, target);

        // Movimiento general de 1 casilla
        int dx = Mathf.Abs(from.X - to.X);
        int dy = Mathf.Abs(from.Y - to.Y);
        return dx + dy == 1;
    }

    private static bool IsScoutPathValid(Tile from, Tile to)
    {
        Vector2I start = from.GridPosition;
        Vector2I end = to.GridPosition;

        // Debe ser linea recta
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

            // Destino
            if (current == end)
            {
                // Vacía: mover
                if (!tile.IsOccupied)
                    return true;

                // Enemigo: atacar
                return true;
            }

            // Enemigo antes del destino: no se puede pasar
            if (tile.IsOccupied && tile.Occupant.PlayerOwner != from.Occupant.PlayerOwner)
                return false;

            current += dir;
        }
    }
}