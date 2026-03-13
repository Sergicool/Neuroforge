using Godot;

public enum GameState
{
    DEPLOYMENT,
    WAITING_INPUT,
    PIECE_SELECTED,
    GAME_OVER
}

public partial class GameManager : Node
{
    public static GameManager Instance { get; private set; }
    private DeploymentUI _deploymentUI;
    private DeploymentController _deployment;

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

        _deploymentUI = GetNode<DeploymentUI>("DeploymentUI");

        _deployment = new DeploymentController();
        AddChild(_deployment);

        _deployment.Initialize(this, _board, _deploymentUI);

        State = GameState.DEPLOYMENT;

        _deploymentUI.ShowUI();
    }

    public Board GetBoard() => _board;

    // SETUP 

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

    public void StartBattle()
    {
        State = GameState.WAITING_INPUT;
    }

    // TURN FLOW 

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
    }

}