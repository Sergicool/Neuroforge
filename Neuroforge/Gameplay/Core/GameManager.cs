using Godot;

public partial class GameManager : Node
{
    public static GameManager Instance { get; private set; }

    private Board _board;
    private Camera2D _camera;
    private DeploymentUI _deploymentUI;
    private DeploymentController _deployment;

    public PieceOwner CurrentTurn { get; private set; } = PieceOwner.PLAYER;
    public GameState State { get; private set; } = GameState.WAITING_INPUT;

    private const string BOARD_SCENE_PATH = "res://Gameplay/Board/Board.tscn";

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

    public Board GetBoard() => _board;

    public void StartBattle() => State = GameState.WAITING_INPUT;

    public bool CanInteract() => State == GameState.WAITING_INPUT;
    public bool IsPlayersTurn(PieceOwner owner) => owner == CurrentTurn;

    public void EndTurn()
    {
        CurrentTurn = CurrentTurn == PieceOwner.PLAYER ? PieceOwner.BOT : PieceOwner.PLAYER;
        State = GameState.WAITING_INPUT;
    }
}