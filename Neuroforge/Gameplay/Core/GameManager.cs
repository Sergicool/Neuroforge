using Godot;

public enum GameState
{
    WAITING_INPUT,
    PIECE_SELECTED,
    BUSY
}

public partial class GameManager : Node
{
    public static GameManager Instance { get; private set; }

    private const string BOARD_SCENE_PATH = "res://Gameplay/Board/Board.tscn";

    private Board _board;

    private Camera2D _camera;

    public PieceOwner CurrentTurn { get; private set; } = PieceOwner.PLAYER;
    public GameState State { get; private set; } = GameState.WAITING_INPUT;

    public override void _Ready()
    {
        Instance = this;

        SpawnBoard();
        SpawnCamera();
    }

    public Board GetBoard() => _board;

    // ================= SETUP =================

    private void SpawnBoard()
    {
        PackedScene boardScene = GD.Load<PackedScene>(BOARD_SCENE_PATH);
        _board = boardScene.Instantiate<Board>();

        AddChild(_board);
        _board.Initialize(this);
    }

    private void SpawnCamera()
    {
        _camera = new Camera2D
        {
            Zoom = new Vector2(0.8f, 0.8f),
            Position = _board.GetBoardCenter(),
            Enabled = true
        };

        AddChild(_camera);
    }

    // ================= TURN FLOW =================

    public bool CanInteract()
        => State == GameState.WAITING_INPUT;

    public bool IsPlayersTurn(PieceOwner owner)
        => owner == CurrentTurn;

    public void EndTurn()
    {
        CurrentTurn = CurrentTurn == PieceOwner.PLAYER
            ? PieceOwner.BOT
            : PieceOwner.PLAYER;

        State = GameState.WAITING_INPUT;

        GD.Print($"Turno actual: {CurrentTurn}");
    }

    public void SetBusy()
        => State = GameState.BUSY;
}