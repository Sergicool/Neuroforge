using Godot;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

// Gestiona la selección de piezas y el resaltado de acciones posibles
public class BoardInputController
{
    private readonly Board _board;
    private readonly GameScene _game;

    private Piece _selectedPiece;
    private readonly List<Tile> _highlightedTiles = new();
    private static bool IsValidPiece(Piece piece) => Godot.GodotObject.IsInstanceValid(piece) && piece.IsInsideTree();

    public BoardInputController(Board board, GameScene game)
    {
        _board = board;
        _game  = game;
    }

    // Punto de entrada: procesa el click sobre una casilla durante la partida
    public async Task HandleTileClick(Tile tile)
    {
        ClearHighlights();
        if (_selectedPiece != null && !IsValidPiece(_selectedPiece))
        {
            ClearSelection();
            return;
        }

        if (_selectedPiece == null)
        {
            TrySelectPiece(tile);
            return;
        }

        if (_highlightedTiles.Contains(tile))
        {
            await ExecuteAction(tile);
            _game.EndTurn();
        }
        ClearSelection();
    }

    // Intenta seleccionar la pieza de la casilla; muestra sus acciones si tiene turno
    private void TrySelectPiece(Tile tile)
    {
        if (!tile.IsOccupied) return;

        Piece piece = tile.Occupant;
        if (!_game.IsPlayersTurn(piece.PlayerOwner) || !piece.CanMove) return;

        _selectedPiece = piece;
        AudioManager.PlaySfx("res://assets/sounds/SelectPiece.wav");
        ShowPossibleActions(piece);
    }

    // Resalta todas las casillas sobre las que la pieza puede actuar
    private void ShowPossibleActions(Piece piece)
    {
        ClearHighlights();

        foreach (Tile tile in _board.AllTiles)
        {
            TileAction action = MovementSystem.GetAction(piece, tile, _game.TurnNumber, _board);
            if (action == TileAction.NONE) continue;

            _highlightedTiles.Add(tile);

            if (action == TileAction.MOVE)   tile.HighlightMove();
            else if (action == TileAction.ATTACK) tile.HighlightAttack();
        }
    }

    // Ejecuta la acción de la pieza seleccionada sobre la casilla destino
    private async Task ExecuteAction(Tile target)
    {
        _game.SetState(GameState.EXECUTING_ACTION);
        TileAction action = MovementSystem.GetAction(_selectedPiece, target, _game.TurnNumber, _board);

        AudioManager.PlaySfx("res://assets/sounds/SelectTile.wav");

        if (action == TileAction.MOVE)
            await _board.MovePiece(_selectedPiece, target);
        else if (action == TileAction.ATTACK)
            await _board.ResolveCombat(_selectedPiece, target.Occupant);
    }

    // Limpia la selección y los resaltados
    public void ClearSelection()
    {
        _selectedPiece = null;
        _highlightedTiles.Clear();
    }

    private void ClearHighlights()
    {
        foreach (Tile t in _highlightedTiles) t.ClearHighlight();
    }
}