using Godot;

public static class MovementSystem
{
    public static TileAction GetAction(Piece piece, Tile target)
    {
        if (!CanMove(piece, target)) return TileAction.NONE;
        return target.IsOccupied ? TileAction.ATTACK : TileAction.MOVE;
    }

    public static bool CanMove(Piece piece, Tile target)
    {
        if (!piece.CanMove || target.TileType == TileType.NO_PASSABLE) return false;
        if (target.IsOccupied && target.Occupant.PlayerOwner == piece.PlayerOwner) return false;

        Vector2I from = piece.CurrentTile.GridPosition;
        Vector2I to = target.GridPosition;

        // Movimiento especial Scout
        if (piece.Type == PieceType.SCOUT) return IsScoutPathValid(piece.CurrentTile, target);

        // Movimiento normal de 1 casilla
        int dx = Mathf.Abs(from.X - to.X);
        int dy = Mathf.Abs(from.Y - to.Y);
        return dx + dy == 1;
    }

    private static bool IsScoutPathValid(Tile from, Tile to)
    {
        Vector2I start = from.GridPosition;
        Vector2I end = to.GridPosition;

        if (start.X != end.X && start.Y != end.Y) return false; // líneas rectas

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